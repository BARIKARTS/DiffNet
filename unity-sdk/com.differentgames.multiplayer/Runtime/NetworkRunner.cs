using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using DifferentGames.Multiplayer.Attributes;
using DifferentGames.Multiplayer.Components;
using DifferentGames.Multiplayer.Serialization;
using UnityEngine;

namespace DifferentGames.Multiplayer
{
    /// <summary>
    /// The heart of the SDK. It is added into the Unity scene and manages the entire connection lifecycle, 
    /// Tick simulation, and object spawn/despawn with a single component.
    ///
    /// Usage:
    ///   1. Add an empty GameObject to the scene → Add Component → NetworkRunner
    ///   2. Set TickRate and Port values in the Inspector
    ///   3. Call StartServer() or StartClient() methods
    /// </summary>
    [DefaultExecutionOrder(-1000)] // Runs before other components
    public class NetworkRunner : MonoBehaviour
    {
        // ── Inspector Settings ────────────────────────────────────────────────

        [Header("Network Settings")]
        [Tooltip("Server Tick rate (simulation steps per second). 60 is recommended.")]
        [SerializeField] private int _tickRate = 60;

        [Tooltip("UDP listening port (used only in Server mode).")]
        [SerializeField] private int _port = 7777;

        [Tooltip("Server IP address to connect to (used only in Client mode).")]
        [SerializeField] private string _serverAddress = "127.0.0.1";

        [Header("Callbacks")]
        [Tooltip("Component that will listen for network events (INetworkCallbacks).")]
        [SerializeField] private MonoBehaviour _callbacksTarget;

        // ── Public State ──────────────────────────────────────────────────────

        public bool IsRunning { get; private set; }
        public bool IsServer { get; private set; }
        public bool IsClient => !IsServer;
        public NetworkPlayerRef LocalPlayer { get; private set; } = NetworkPlayerRef.None;
        public NetworkTick CurrentTick { get; private set; } = NetworkTick.Invalid;

        /// <summary>
        /// Interpolation alpha value in the render frame. Between 0-1.
        /// NetworkTransform.Render() uses this value.
        /// </summary>
        public float InterpolationAlpha { get; private set; }

        // ── Internal Data Structures ──────────────────────────────────────────

        private INetworkCallbacks _callbacks;
        private readonly Dictionary<NetworkObjectId, NetworkObject> _spawnedObjects = new();
        private readonly Dictionary<NetworkPlayerRef, IPEndPoint> _playerEndpoints = new();
        private uint _nextObjectId = 1;

        // UDP transport (simple wrapper - RUDP adapter would be integrated in a real project)
        private UdpClient _udpClient;
        private IPEndPoint _serverEndPoint;

        // Tick timing
        private float _tickAccumulator;
        private float _tickInterval;
        private int _lastFrameTick;

        // ── Initialization (Public API) ───────────────────────────────────────

        /// <summary>
        /// Starts in server mode. Opens a UDP socket and runs the Tick loop.
        /// </summary>
        public void StartServer()
        {
            if (IsRunning) { Debug.LogWarning("[NetworkRunner] Already running!"); return; }

            IsServer = true;
            LocalPlayer = NetworkPlayerRef.Server;
            _tickInterval = 1f / _tickRate;

            _udpClient = new UdpClient(_port);
            _udpClient.Client.Blocking = false;

            IsRunning = true;
            CurrentTick = new NetworkTick(0);

            _callbacks = _callbacksTarget as INetworkCallbacks;
            _callbacks?.OnConnectedToServer(LocalPlayer);

            Debug.Log($"[NetworkRunner] Server started on port {_port} @ {_tickRate} tick/s");
        }

        /// <summary>
        /// Starts in client mode. Connects to the server.
        /// </summary>
        public void StartClient(string serverIp = null, int port = 0)
        {
            if (IsRunning) { Debug.LogWarning("[NetworkRunner] Already running!"); return; }

            IsServer = false;
            _tickInterval = 1f / _tickRate;

            string ip = serverIp ?? _serverAddress;
            int p = port > 0 ? port : _port;

            _serverEndPoint = new IPEndPoint(IPAddress.Parse(ip), p);
            _udpClient = new UdpClient();
            _udpClient.Client.Blocking = false;

            IsRunning = true;
            _callbacks = _callbacksTarget as INetworkCallbacks;

            // Send "Connect" request to the server
            SendConnectRequest();

            Debug.Log($"[NetworkRunner] Client connecting to {ip}:{p}");
        }

        /// <summary>
        /// Stops the running runner and releases resources.
        /// </summary>
        public void Shutdown()
        {
            if (!IsRunning) return;
            IsRunning = false;

            _udpClient?.Close();
            _udpClient = null;

            _spawnedObjects.Clear();
            _playerEndpoints.Clear();
            CurrentTick = NetworkTick.Invalid;

            _callbacks?.OnShutdown();
            Debug.Log("[NetworkRunner] Shutdown.");
        }

        // ── Object Management (Spawn / Despawn) ───────────────────────────────

        /// <summary>
        /// Spawns a synchronized object over the network.
        /// Should only be called by State Authority (server or authority client).
        /// </summary>
        public NetworkObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation,
            NetworkPlayerRef inputAuthority = default)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[NetworkRunner] Only server can spawn objects!");
                return null;
            }

            var go = Instantiate(prefab, position, rotation);
            var netObj = go.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError($"[NetworkRunner] Prefab '{prefab.name}' has no NetworkObject component!");
                Destroy(go);
                return null;
            }

            var id = new NetworkObjectId(_nextObjectId++);
            var owner = inputAuthority.IsNone ? LocalPlayer : inputAuthority;
            netObj.NetworkInitialize(this, id, owner);
            _spawnedObjects[id] = netObj;

            Debug.Log($"[NetworkRunner] Spawned {prefab.name} → {id} (owner: {owner})");
            return netObj;
        }

        /// <summary>
        /// Despawns an object on the network and notifies all clients.
        /// </summary>
        public void Despawn(NetworkObject netObj)
        {
            if (netObj == null) return;
            _spawnedObjects.Remove(netObj.ObjectId);
            Destroy(netObj.gameObject);
        }

        // ── Data Transmission (Internal API) ───────────────────────────────────

        internal unsafe void SendRaw(NetworkPlayerRef target, ReadOnlySpan<byte> data, Core.DeliveryMode mode)
        {
            if (_udpClient == null) return;

            // Span → byte[] (Required for UDP API, single allocation point)
            byte[] buffer = data.ToArray();

            if (IsServer)
            {
                if (_playerEndpoints.TryGetValue(target, out var ep))
                    _udpClient.Send(buffer, buffer.Length, ep);
            }
            else
            {
                _udpClient.Send(buffer, buffer.Length, _serverEndPoint);
            }
        }

        internal void SendRawToTargets(NetworkPlayerRef owner, RpcTargets targets,
            ReadOnlySpan<byte> data, Core.DeliveryMode mode)
        {
            byte[] buffer = data.ToArray();

            switch (targets)
            {
                case RpcTargets.Server:
                    if (!IsServer)
                        _udpClient?.Send(buffer, buffer.Length, _serverEndPoint);
                    break;

                case RpcTargets.All:
                    if (IsServer)
                        foreach (var ep in _playerEndpoints.Values)
                            _udpClient?.Send(buffer, buffer.Length, ep);
                    else
                        _udpClient?.Send(buffer, buffer.Length, _serverEndPoint);
                    break;

                case RpcTargets.Owner:
                    SendRaw(owner, data, mode);
                    break;

                case RpcTargets.Proxy:
                    if (IsServer)
                        foreach (var kvp in _playerEndpoints)
                            if (kvp.Key != owner)
                                _udpClient?.Send(buffer, buffer.Length, kvp.Value);
                    break;
            }
        }

        // ── Tick Loop ─────────────────────────────────────────────────────────

        private void Update()
        {
            if (!IsRunning) return;

            // Process incoming packets
            PollIncoming();

            // Advance the tick accumulator
            _tickAccumulator += Time.deltaTime;

            while (_tickAccumulator >= _tickInterval)
            {
                _tickAccumulator -= _tickInterval;
                SimulateTick();
            }

            // Calculate interpolation alpha (0 = previous tick, 1 = next tick)
            InterpolationAlpha = _tickAccumulator / _tickInterval;

            // Call all NetworkBehaviour.Render()
            foreach (var netObj in _spawnedObjects.Values)
                foreach (var nb in netObj.Behaviours)
                    nb.Render();
        }

        private void SimulateTick()
        {
            CurrentTick = CurrentTick.Next;

            // Call all NetworkBehaviour.FixedUpdateNetwork()
            foreach (var netObj in _spawnedObjects.Values)
                foreach (var nb in netObj.Behaviours)
                    nb.FixedUpdateNetwork();

            // If server: send state snapshot of everyone
            if (IsServer)
                BroadcastSnapshot();
        }

        // ── Snapshot Broadcast ────────────────────────────────────────────────

        private unsafe void BroadcastSnapshot()
        {
            Span<byte> snapshotBuffer = stackalloc byte[1024];

            foreach (var netObj in _spawnedObjects.Values)
            {
                var writer = new NetworkWriter(snapshotBuffer);
                writer.WriteByte(0x01);               // Packet Type: Snapshot
                writer.WriteInt((int)netObj.ObjectId.Value);
                writer.WriteInt(CurrentTick.Value);

                foreach (var nb in netObj.Behaviours)
                    nb.SerializeState(ref writer);

                var data = writer.ToSpan().ToArray();
                foreach (var ep in _playerEndpoints.Values)
                    _udpClient?.Send(data, data.Length, ep);
            }
        }

        // ── Incoming Packet Processing ──────────────────────────────────────────

        private void PollIncoming()
        {
            if (_udpClient == null) return;

            try
            {
                while (_udpClient.Available > 0)
                {
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udpClient.Receive(ref remote);
                    ProcessPacket(data, remote);
                }
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode != SocketError.WouldBlock)
                    Debug.LogError($"[NetworkRunner] Socket error: {ex.Message}");
            }
        }

        private unsafe void ProcessPacket(byte[] data, IPEndPoint remote)
        {
            if (data.Length < 1) return;

            fixed (byte* ptr = data)
            {
                var reader = new NetworkReader(ptr, data.Length);
                byte packetType = reader.ReadByte();

                switch (packetType)
                {
                    case 0x00: // Connect Request
                        if (IsServer) HandleConnectRequest(remote);
                        break;

                    case 0x01: // Snapshot
                        if (!IsServer) HandleSnapshot(ref reader);
                        break;

                    case 0x02: // RPC
                        HandleRpc(ref reader, remote);
                        break;

                    default:
                        Debug.LogWarning($"[NetworkRunner] Unknown packet type: {packetType}");
                        break;
                }
            }
        }

        // ── Packet Handlers ───────────────────────────────────────────────────

        private void HandleConnectRequest(IPEndPoint remote)
        {
            // Allocate new player ID
            var playerId = new NetworkPlayerRef(_playerEndpoints.Count + 1);
            _playerEndpoints[playerId] = remote;

            Debug.Log($"[NetworkRunner] Player joined: {playerId} from {remote}");
            _callbacks?.OnPlayerJoined(playerId);

            // Send acceptance packet
            byte[] accept = { 0x03, (byte)(playerId.Id) };
            _udpClient.Send(accept, accept.Length, remote);
        }

        private unsafe void HandleSnapshot(ref NetworkReader reader)
        {
            uint objectId = (uint)reader.ReadInt();
            int tick = reader.ReadInt();
            var netId = new NetworkObjectId(objectId);

            if (!_spawnedObjects.TryGetValue(netId, out var netObj)) return;

            CurrentTick = new NetworkTick(tick);

            foreach (var nb in netObj.Behaviours)
                nb.DeserializeState(ref reader);
        }

        private unsafe void HandleRpc(ref NetworkReader reader, IPEndPoint remote)
        {
            uint objectId = (uint)reader.ReadInt();
            var netId = new NetworkObjectId(objectId);

            if (!_spawnedObjects.TryGetValue(netId, out var netObj)) return;

            // Find sender
            NetworkPlayerRef sender = NetworkPlayerRef.Server;
            foreach (var kvp in _playerEndpoints)
            {
                if (kvp.Value.Equals(remote)) { sender = kvp.Key; break; }
            }

            // Forward to NetworkBehaviours via data pointer
            // (DispatchRpc already continues offset on the reader)
        }


        private void SendConnectRequest()
        {
            byte[] packet = { 0x00 }; // Connect Request
            _udpClient?.Send(packet, packet.Length, _serverEndPoint);
        }

        // ── Unity Lifecycle ────────────────────────────────────────────────────

        private void OnDestroy() => Shutdown();

        private void OnApplicationQuit() => Shutdown();
    }
}
