using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using DifferentGames.Multiplayer.Attributes;
using DifferentGames.Multiplayer.Components;
using DifferentGames.Multiplayer.Core;
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

        [Header("Session Contract")]
        [Tooltip("Configuration defining delta-compression and buffers limit")]
        [SerializeField] private NetworkConfig _config = NetworkConfig.Default;

        [Header("Callbacks")]
        [Tooltip("Component that will listen for network events (INetworkCallbacks).")]
        [SerializeField] private MonoBehaviour _callbacksTarget;

        // ── Public State ──────────────────────────────────────────────────────
        
        public NetworkConfig Config => _config;

        public bool IsRunning { get; private set; }
        public bool IsServer { get; private set; }
        public bool IsClient => !IsServer;
        public NetworkPlayerRef LocalPlayer { get; private set; } = NetworkPlayerRef.None;

        /// <summary>
        /// Current simulation tick. During prediction or resimulation, this matches the currently executing tick.
        /// </summary>
        public NetworkTick CurrentTick { get; private set; } = NetworkTick.Invalid;
        
        /// <summary>
        /// The latest verified and authoritative tick from the server.
        /// </summary>
        public NetworkTick ServerTick { get; private set; } = NetworkTick.Invalid;
        
        /// <summary>
        /// The client's predicted local tick (ahead of the server).
        /// </summary>
        public NetworkTick LocalTick { get; private set; } = NetworkTick.Invalid;

        /// <summary>
        /// Ring buffer storing inputs sent to the server.
        /// </summary>
        public Core.NetworkInputBuffer InputBuffer { get; private set; }

        /// <summary>
        /// Interpolation alpha value in the render frame. Between 0-1.
        /// NetworkTransform.Render() uses this value.
        /// </summary>
        public float InterpolationAlpha { get; private set; }

        // ── Internal Data Structures ──────────────────────────────────────────

        private INetworkCallbacks _callbacks;
        private InterestManager _interestManager;
        private readonly Dictionary<NetworkObjectId, NetworkObject> _spawnedObjects = new();
        private readonly Dictionary<NetworkPlayerRef, IPEndPoint> _playerEndpoints = new();
        private readonly Dictionary<NetworkPlayerRef, NetworkTick> _playerAckedTicks = new();
        private readonly Dictionary<NetworkPlayerRef, NetworkObject> _playerAnchors = new();
        private uint _nextObjectId = 1;

        // ── Prefab Registry & Per-Player Spawn Tracking ───────────────────────

        [Header("Prefab Registry")]
        [Tooltip("All NetworkObject prefabs for this session. Index+1 = prefabId. Must be identical on Server and all Clients.")]
        [SerializeField] private List<GameObject> _networkPrefabs = new();

        // Maps prefabId (1-based list index) → prefab GameObject for fast O(1) lookup
        private readonly Dictionary<uint, GameObject> _prefabRegistry = new();

        // Tracks which ObjectIds each player has received a Spawn (0x04) packet for.
        //   Global/OwnerOnly objects: added once at object-creation or player-connect time.
        //   Spatial objects: added/removed each tick by SendLifecyclePackets when crossing AOI boundary.
        private readonly Dictionary<NetworkPlayerRef, HashSet<NetworkObjectId>> _spawnedForPlayer = new();

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
            ServerTick = new NetworkTick(0);
            LocalTick = new NetworkTick(0);
            InputBuffer = new Core.NetworkInputBuffer(_config.StateHistorySize);
            _interestManager = new InterestManager(this, _config, _playerAnchors);
            BuildPrefabRegistry();
            _spawnedForPlayer.Clear();

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
            InputBuffer = new Core.NetworkInputBuffer(_config.StateHistorySize);
            _callbacks = _callbacksTarget as INetworkCallbacks;
            BuildPrefabRegistry(); // Client also needs the registry for HandleSpawnPacket

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
            _playerAckedTicks.Clear();
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

            var id    = new NetworkObjectId(_nextObjectId++);
            var owner = inputAuthority.IsNone ? LocalPlayer : inputAuthority;

            // Stamp the prefabId so Spawn packets can tell clients what to Instantiate
            uint prefabId = GetPrefabIdFromRegistry(prefab);
            netObj.InternalSetPrefabId(prefabId);
            netObj.NetworkInitialize(this, id, owner);
            _spawnedObjects[id] = netObj;

            _interestManager?.AddObject(netObj);

            // Immediately notify connected players for Global and OwnerOnly objects.
            // Spatial objects are picked up lazily next tick by SendLifecyclePackets.
            var scopingMode = netObj.Scoping?.Mode ?? ScopingMode.Global;
            NotifyPlayersOfNewSpawn(netObj, owner, scopingMode);

            Debug.Log($"[NetworkRunner] Spawned {prefab.name} → {id} (owner: {owner}, prefabId: {prefabId})");
            return netObj;
        }

        /// <summary>
        /// Despawns an object on the network and notifies all clients.
        /// </summary>
        public void Despawn(NetworkObject netObj)
        {
            if (netObj == null) return;
            _interestManager?.RemoveObject(netObj);
            _spawnedObjects.Remove(netObj.ObjectId);
            Destroy(netObj.gameObject);
        }

        /// <summary>
        /// Registers a NetworkObject as the physical anchor (center point) for a player's Area of Interest.
        /// </summary>
        public void SetPlayerAnchor(NetworkPlayerRef player, NetworkObject anchor)
        {
            _playerAnchors[player] = anchor;
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
            if (IsServer)
            {
                ServerTick = ServerTick.Next;
                CurrentTick = ServerTick;

                foreach (var netObj in _spawnedObjects.Values)
                {
                    foreach (var nb in netObj.Behaviours)
                        nb.FixedUpdateNetwork();
                        
                    _interestManager?.UpdateObjectPosition(netObj);
                }

                // Record state to history for Delta Compression baseline
                foreach (var netObj in _spawnedObjects.Values)
                    foreach (var nb in netObj.Behaviours)
                        nb.RecordCurrentState();

                // Update visibility BitSets for ALL players based on this tick's positions.
                // This MUST happen before SendLifecyclePackets so JustEntered/JustExited is accurate.
                if (_interestManager != null)
                    foreach (var player in _playerEndpoints.Keys)
                        _interestManager.UpdateVisibilityForPlayer(player, (int)_nextObjectId);

                SendLifecyclePackets(); // Spawn/Despawn notifications for AOI boundary crossings
                SendSnapshots();        // Delta state — only for objects already Spawned to each player
            }
            else
            {
                LocalTick = LocalTick.Next;
                CurrentTick = LocalTick;

                // Fire Input prediction (Callback for developers)
                var inputProvider = new Core.NetworkInputProvider(InputBuffer, LocalTick);
                _callbacks?.OnProvideInput(this, inputProvider);

                foreach (var netObj in _spawnedObjects.Values)
                    foreach (var nb in netObj.Behaviours)
                        nb.FixedUpdateNetwork();

                // Predictive record: We must save our local guess so we can compare it with Server Snapshot later!
                foreach (var netObj in _spawnedObjects.Values)
                    foreach (var nb in netObj.Behaviours)
                        nb.RecordCurrentState();

                // Send Inputs dynamically (unreliable usually)
                SendInputsToServer();
            }
        }
        
        private void SendInputsToServer()
        {
            // Implementation mapping inputs into 0x04 Packet
        }

        private void Resimulate(NetworkTick startTick)
        {
            // Snap C# memory back to startTick
            foreach(var obj in _spawnedObjects.Values)
                foreach(var nb in obj.Behaviours)
                    nb.SnapToTick(startTick);

            // Resimulate from startTick+1 to LocalTick
            for (int t = startTick.Value + 1; t <= LocalTick.Value; t++)
            {
                CurrentTick = new NetworkTick(t);
                
                foreach(var obj in _spawnedObjects.Values)
                    foreach(var nb in obj.Behaviours)
                        nb.FixedUpdateNetwork();
                        
                // Re-record locally predictive states to history!
                foreach(var obj in _spawnedObjects.Values)
                    foreach(var nb in obj.Behaviours)
                        nb.RecordCurrentState();
            }
            
            CurrentTick = LocalTick;
        }

        // ── Snapshot Broadcast ────────────────────────────────────────────────

        private unsafe void SendSnapshots()
        {
            Span<byte> snapshotBuffer = stackalloc byte[8192];

            foreach (var kvp in _playerEndpoints)
            {
                NetworkPlayerRef targetPlayer = kvp.Key;
                IPEndPoint ep = kvp.Value;
                
                // Fetch the last confirmed tick for this client. 
                // Assumed 0 if they haven't sent any ACKs yet.
                NetworkTick baselineTick = _playerAckedTicks[targetPlayer];
                
                var writer = new NetworkWriter(snapshotBuffer);
                writer.WriteByte(0x01);               // Packet Type: Snapshot
                writer.WriteInt(CurrentTick.Value);
                writer.WriteInt(baselineTick.Value);  // Server informs client about diff-baseline

                // Only serialize state for objects this player has already received a Spawn packet for.
                // Visibility is pre-computed in SimulateTick; lifecycle packets handle enter/exit.
                _spawnedForPlayer.TryGetValue(targetPlayer, out var spawnedSet);

                foreach (var netObj in _spawnedObjects.Values)
                {
                    // Skip objects not yet registered in this player's active spawn set
                    if (spawnedSet != null && !spawnedSet.Contains(netObj.ObjectId)) continue;

                    // If the object JUST entered the AOI this tick, bypass Delta compression:
                    // send a Full Snapshot so the client establishes a valid baseline immediately.
                    NetworkTick objectBaseline = baselineTick;
                    if (_interestManager != null && _interestManager.JustEntered(targetPlayer, netObj))
                    {
                        objectBaseline = NetworkTick.Invalid;
                    }

                    writer.WriteInt((int)netObj.ObjectId.Value);
                    foreach (var nb in netObj.Behaviours)
                        nb.SerializeDeltaState(ref writer, objectBaseline);
                }

                // Packet termination marker
                writer.WriteInt(0); 

                var data = writer.ToSpan().ToArray();
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
                        if (IsServer) HandleConnectRequest(remote, ref reader);
                        break;

                    case 0x01: // Snapshot
                        if (!IsServer) HandleSnapshot(ref reader);
                        break;

                    case 0x02: // RPC
                        HandleRpc(ref reader, remote);
                        break;

                    case 0x04: // SpawnObject — client instantiates a new NetworkObject entering its AOI
                        if (!IsServer) HandleSpawnPacket(ref reader);
                        break;

                    case 0x05: // DespawnObject — client destroys a NetworkObject leaving its AOI
                        if (!IsServer) HandleDespawnPacket(ref reader);
                        break;

                    default:
                        Debug.LogWarning($"[NetworkRunner] Unknown packet type: {packetType}");
                        break;
                }
            }
        }

        // ── Packet Handlers ───────────────────────────────────────────────────

        private void HandleConnectRequest(IPEndPoint remote, ref NetworkReader reader)
        {
            // Read Handshake payload: NetworkConfig
            int clientMaxVars = reader.ReadInt();
            int clientBuffer = reader.ReadInt();
            int clientGrid = reader.ReadInt();

            if (clientMaxVars != _config.MaxNetworkedVariables || clientBuffer != _config.StateHistorySize || clientGrid != _config.AOIGridCellSize)
            {
                Debug.LogWarning($"[NetworkRunner] Client connection rejected: Contract mismatch. Server(Vars:{_config.MaxNetworkedVariables}, Buf:{_config.StateHistorySize}, Grid:{_config.AOIGridCellSize}) vs Client({clientMaxVars}, {clientBuffer}, {clientGrid})");
                return; // Disconnect silently or send a specialized rejection packet
            }

            // Allocate new player ID
            var playerId = new NetworkPlayerRef(_playerEndpoints.Count + 1);
            _playerEndpoints[playerId] = remote;
            _playerAckedTicks[playerId] = new NetworkTick(0);
            _spawnedForPlayer[playerId] = new HashSet<NetworkObjectId>();

            // Immediately send Spawn packets for all currently active Global and OwnerOnly objects.
            // Spatial objects will be picked up by SendLifecyclePackets next tick.
            SendInitialSpawnPackets(playerId, remote);

            Debug.Log($"[NetworkRunner] Player joined: {playerId} from {remote} with Valid Contract");
            _callbacks?.OnPlayerJoined(playerId);

            // Send acceptance packet
            byte[] accept = { 0x03, (byte)(playerId.Id) };
            _udpClient.Send(accept, accept.Length, remote);
        }

        private unsafe void HandleSnapshot(ref NetworkReader reader)
        {
            int serverTickVal = reader.ReadInt();
            int baselineTick = reader.ReadInt();
            
            // Server just approved this tick
            ServerTick = new NetworkTick(serverTickVal);
            bool stateChanged = false;

            // The client might be freshly initializing or heavily decoupled
            if (!LocalTick.IsValid || ServerTick.Value > LocalTick.Value) 
            {
                LocalTick = ServerTick;
                CurrentTick = ServerTick;
            }

            while (reader.Remaining >= 4)
            {
                uint objectId = (uint)reader.ReadInt();
                if (objectId == 0) break; // End of packet

                var netId = new NetworkObjectId(objectId);
                if (_spawnedObjects.TryGetValue(netId, out var netObj))
                {
                    netObj.LastReceivedSnapshotTick = ServerTick;
                    
                    // Awaken from AOI Object Pooling dynamically
                    if (_config.EnableAOI && !netObj.gameObject.activeSelf)
                        netObj.gameObject.SetActive(true);

                    foreach (var nb in netObj.Behaviours)
                    {
                        if (nb.DeserializeDeltaState(ref reader, new NetworkTick(baselineTick), ServerTick))
                        {
                            stateChanged = true;
                        }
                    }
                }
            }

            // Post-Process: Interest Management Disabling
            if (_config.EnableAOI)
            {
                foreach (var netObj in _spawnedObjects.Values)
                {
                    // If server hasn't sent us an update for this tick, it's culled (or we're ignoring updates for our own predicted items? No, server always sends our own).
                    if (netObj.LastReceivedSnapshotTick.Value != ServerTick.Value && netObj.InputAuthority != LocalPlayer)
                    {
                        if (netObj.gameObject.activeSelf)
                            netObj.gameObject.SetActive(false); // Put back to pool / sleep
                    }
                }
            }

            // Client-Side Reconciliation Trigger (Rollback & Repay)
            if (stateChanged && ServerTick.Value < LocalTick.Value)
            {
                Resimulate(ServerTick);
            }
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


        private unsafe void SendConnectRequest()
        {
            Span<byte> buffer = stackalloc byte[16];
            var writer = new NetworkWriter(buffer);
            writer.WriteByte(0x00); // 0x00: Connect Request
            writer.WriteInt(_config.MaxNetworkedVariables); // Session Contract: Networked Vars Limit
            writer.WriteInt(_config.StateHistorySize);      // Session Contract: Buffer Size
            writer.WriteInt(_config.AOIGridCellSize);       // Session Contract: Grid Cell Size

            var packet = writer.ToSpan().ToArray();
            _udpClient?.Send(packet, packet.Length, _serverEndPoint);
        }

        // ── Spawn / Despawn Protocol ─────────────────────────────────────────

        private void BuildPrefabRegistry()
        {
            _prefabRegistry.Clear();
            for (int i = 0; i < _networkPrefabs.Count; i++)
                if (_networkPrefabs[i] != null)
                    _prefabRegistry[(uint)(i + 1)] = _networkPrefabs[i];

            Debug.Log($"[NetworkRunner] Prefab registry: {_prefabRegistry.Count} entries.");
        }

        private uint GetPrefabIdFromRegistry(GameObject prefab)
        {
            for (int i = 0; i < _networkPrefabs.Count; i++)
                if (_networkPrefabs[i] == prefab) return (uint)(i + 1);

            Debug.LogWarning($"[NetworkRunner] Prefab '{prefab.name}' is not in the Prefab Registry! Add it to NetworkRunner._networkPrefabs list.");
            return 0;
        }

        /// <summary>
        /// Proactively sends Spawn packets to already-connected players when a new object spawns.
        /// Global → all players. OwnerOnly → only the owner. Spatial → skipped (handled per-tick).
        /// </summary>
        private void NotifyPlayersOfNewSpawn(NetworkObject netObj, NetworkPlayerRef owner, ScopingMode scopingMode)
        {
            if (scopingMode == ScopingMode.Spatial) return;

            foreach (var kvp in _playerEndpoints)
            {
                var player = kvp.Key;
                var ep     = kvp.Value;

                if (!_spawnedForPlayer.TryGetValue(player, out var knownObjects)) continue;
                if (knownObjects.Contains(netObj.ObjectId)) continue;

                bool shouldSend = scopingMode == ScopingMode.Global ||
                                  (scopingMode == ScopingMode.OwnerOnly && player == owner);

                if (shouldSend)
                {
                    SendSpawnPacket(netObj, ep);
                    knownObjects.Add(netObj.ObjectId);
                }
            }
        }

        /// <summary>
        /// Sends Spawn packets for all currently active Global and OwnerOnly objects
        /// to a player who just connected. Spatial objects are picked up next tick.
        /// </summary>
        private void SendInitialSpawnPackets(NetworkPlayerRef player, IPEndPoint ep)
        {
            if (!_spawnedForPlayer.TryGetValue(player, out var knownObjects)) return;

            foreach (var netObj in _spawnedObjects.Values)
            {
                var mode = netObj.Scoping?.Mode ?? ScopingMode.Global;

                bool send = mode == ScopingMode.Global ||
                            (mode == ScopingMode.OwnerOnly && netObj.InputAuthority == player);

                if (send && !knownObjects.Contains(netObj.ObjectId))
                {
                    SendSpawnPacket(netObj, ep);
                    knownObjects.Add(netObj.ObjectId);
                }
            }
        }

        /// <summary>
        /// Called each server tick BEFORE SendSnapshots.
        /// Sends Spawn (0x04) for Spatial objects entering a player's AOI,
        /// and Despawn (0x05) for Spatial objects exiting it.
        /// </summary>
        private void SendLifecyclePackets()
        {
            if (_interestManager == null) return;

            foreach (var kvp in _playerEndpoints)
            {
                var player = kvp.Key;
                var ep     = kvp.Value;

                if (!_spawnedForPlayer.TryGetValue(player, out var knownObjects)) continue;

                foreach (var netObj in _spawnedObjects.Values)
                {
                    if (netObj.Scoping?.Mode != ScopingMode.Spatial) continue;

                    bool isKnown     = knownObjects.Contains(netObj.ObjectId);
                    bool justEntered = _interestManager.JustEntered(player, netObj);
                    bool justExited  = _interestManager.JustExited(player, netObj);

                    if (!isKnown && justEntered)
                    {
                        SendSpawnPacket(netObj, ep);
                        knownObjects.Add(netObj.ObjectId);
                    }
                    else if (isKnown && justExited)
                    {
                        SendDespawnPacket(netObj, ep);
                        knownObjects.Remove(netObj.ObjectId);
                    }
                }
            }
        }

        /// <summary>
        /// Packet 0x04 Spawn — tells the client to Instantiate a new NetworkObject.
        /// Layout: [0x04][objectId:4][prefabId:4][ownerId:4][pos:12][rot:7] ≈ 32 bytes
        /// </summary>
        private unsafe void SendSpawnPacket(NetworkObject netObj, IPEndPoint ep)
        {
            Span<byte> buffer = stackalloc byte[64];
            var writer = new NetworkWriter(buffer);
            writer.WriteByte(0x04);
            writer.WriteUInt(netObj.ObjectId.Value);
            writer.WriteUInt(netObj.PrefabId);
            writer.WriteInt(netObj.InputAuthority.Id);
            writer.WriteVector3(netObj.transform.position);
            writer.WriteQuaternionCompressed(netObj.transform.rotation);

            var data = writer.ToSpan().ToArray();
            _udpClient?.Send(data, data.Length, ep);
        }

        /// <summary>
        /// Packet 0x05 Despawn — tells the client to Destroy a NetworkObject leaving its AOI.
        /// Layout: [0x05][objectId:4] = 5 bytes
        /// </summary>
        private unsafe void SendDespawnPacket(NetworkObject netObj, IPEndPoint ep)
        {
            Span<byte> buffer = stackalloc byte[8];
            var writer = new NetworkWriter(buffer);
            writer.WriteByte(0x05);
            writer.WriteUInt(netObj.ObjectId.Value);

            var data = writer.ToSpan().ToArray();
            _udpClient?.Send(data, data.Length, ep);
        }

        /// <summary>
        /// Client-side: Handles packet 0x04 (SpawnObject).
        /// Instantiates a new NetworkObject from the prefab registry at the given transform.
        /// </summary>
        private unsafe void HandleSpawnPacket(ref NetworkReader reader)
        {
            uint       objectId = reader.ReadUInt();
            uint       prefabId = reader.ReadUInt();
            int        ownerId  = reader.ReadInt();
            Vector3    pos      = reader.ReadVector3();
            Quaternion rot      = reader.ReadQuaternionCompressed();

            var netId = new NetworkObjectId(objectId);

            // Already know this object? Reactivate and reposition (handles duplicate/reconnect)
            if (_spawnedObjects.TryGetValue(netId, out var existing))
            {
                existing.transform.SetPositionAndRotation(pos, rot);
                existing.gameObject.SetActive(true);
                return;
            }

            if (!_prefabRegistry.TryGetValue(prefabId, out var prefab))
            {
                Debug.LogWarning($"[NetworkRunner] Spawn packet: Unknown prefabId={prefabId}. Is it in the Prefab Registry?");
                return;
            }

            var go     = Instantiate(prefab, pos, rot);
            var netObj = go.GetComponent<NetworkObject>();

            if (netObj == null)
            {
                Debug.LogError("[NetworkRunner] Spawned prefab has no NetworkObject component!");
                Destroy(go);
                return;
            }

            netObj.InternalSetPrefabId(prefabId);
            netObj.NetworkInitialize(this, netId, new NetworkPlayerRef(ownerId));
            _spawnedObjects[netId] = netObj;

            _callbacks?.OnObjectSpawned(netObj);
            Debug.Log($"[NetworkRunner] Client: Instantiated object {netId} (prefabId={prefabId})");
        }

        /// <summary>
        /// Client-side: Handles packet 0x05 (DespawnObject).
        /// Destroys the NetworkObject that has exited the local player's Area of Interest.
        /// </summary>
        private void HandleDespawnPacket(ref NetworkReader reader)
        {
            uint objectId = reader.ReadUInt();
            var  netId    = new NetworkObjectId(objectId);

            if (_spawnedObjects.TryGetValue(netId, out var netObj))
            {
                _callbacks?.OnObjectDespawned(netObj);
                _spawnedObjects.Remove(netId);
                Destroy(netObj.gameObject);
                Debug.Log($"[NetworkRunner] Client: Destroyed object {netId} (left AOI)");
            }
        }

        // ── Unity Lifecycle ────────────────────────────────────────────────────

        private void OnDestroy() => Shutdown();

        private void OnApplicationQuit() => Shutdown();
    }
}
