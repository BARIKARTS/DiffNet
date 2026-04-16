using DifferentGames.Multiplayer;
using DifferentGames.Multiplayer.Attributes;
using DifferentGames.Multiplayer.Components;
using UnityEngine;

/// <summary>
/// SDK Usage Example.
/// Attach this script to a GameObject that has a NetworkRunner component.
///
/// Scene Setup:
///   1. Empty GameObject → Add Component → NetworkRunner
///   2. Empty GameObject → Add Component → NetworkBootstrap (this script)
///   3. Assign playerPrefab and callbacksTarget from Inspector
/// </summary>
public class NetworkBootstrap : MonoBehaviour, INetworkCallbacks
{
    [Header("Setup")]
    [SerializeField] private NetworkRunner _runner;
    [SerializeField] private GameObject _playerPrefab;
    [SerializeField] private bool _startAsServer = true;

    private void Start()
    {
        if (_runner == null)
            _runner = FindObjectOfType<NetworkRunner>();

        if (_startAsServer)
            _runner.StartServer();
        else
            _runner.StartClient();
    }

    // ── INetworkCallbacks ─────────────────────────────────────────────────────

    public void OnConnectedToServer(NetworkPlayerRef localPlayer)
    {
        Debug.Log($"[Bootstrap] Connected as {localPlayer}");
    }

    public void OnPlayerJoined(NetworkPlayerRef player)
    {
        Debug.Log($"[Bootstrap] Player joined: {player}");

        // Spawn character object for the new player on server side
        if (_runner.IsServer && _playerPrefab != null)
        {
            _runner.Spawn(
                _playerPrefab,
                position: Vector3.zero,
                rotation: Quaternion.identity,
                inputAuthority: player
            );
        }
    }

    public void OnPlayerLeft(NetworkPlayerRef player)
    {
        Debug.Log($"[Bootstrap] Player left: {player}");
    }

    public void OnShutdown()
    {
        Debug.Log("[Bootstrap] Network shutdown.");
    }
}

/// <summary>
/// Sample Player component. Shows how to use the SDK API.
/// </summary>
public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float _speed = 5f;

    // ─── [Networked] Variables: Automatically synchronized ─────────────────

    [Networked] public float Health { get; set; } = 100f;
    [Networked] public int Score { get; set; } = 0;
    [Networked(serverOnly: true)] public bool IsAlive { get; set; } = true;

    // ─── Deterministic Loop ─────────────────────────────────────────────────

    public override void FixedUpdateNetwork()
    {
        // Only the client with InputAuthority for this object reads input
        if (!HasInputAuthority) return;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        var dir = new Vector3(h, 0, v).normalized;

        if (dir.sqrMagnitude > 0.01f)
        {
            // Send input to server
            RpcSendInput(dir, _speed);
        }
    }

    public override void Render()
    {
        // Visual update - interpolation is done here (NetworkTransform handles it automatically)
    }

    // ─── [Rpc] Methods ───────────────────────────────────────────────────────

    /// <summary>Client → Server: Send movement input</summary>
    [Rpc(RpcTargets.Server, Reliable = false, Channel = 0)]
    public void RpcSendInput(Vector3 direction, float speed)
    {
        // Server receives this method and performs physics calculation
        if (!HasStateAuthority) return;
        transform.position += direction * speed * (1f / 60f); // Normalize with TickRate
    }

    /// <summary>Server → All: Take damage</summary>
    [Rpc(RpcTargets.All, Reliable = true)]
    public void RpcTakeDamage(float amount, NetworkPlayerRef attacker)
    {
        if (HasStateAuthority)
        {
            Health -= amount;
            if (Health <= 0) IsAlive = false;
        }
        // Play effect on all clients:
        Debug.Log($"{name} took {amount} damage from {attacker}! HP: {Health}");
    }

    /// <summary>Server → Owner: Update score (only owner sees it)</summary>
    [Rpc(RpcTargets.Owner)]
    public void RpcUpdateScore(int newScore)
    {
        Score = newScore;
        Debug.Log($"Your score: {Score}");
    }

    // ─── Manual Data Transmission ────────────────────────────────────────────────

    public void SendCustomData(byte[] customPacket)
    {
        // Advanced: Raw byte transmission (specify DeliveryMode)
        SendManual(customPacket, DeliveryMode.ReliableOrdered);
    }

    private void OnEnable()
    {
        // Listen to incoming Manual data
        OnManualDataReceived += HandleManualData;
    }

    private void OnDisable()
    {
        OnManualDataReceived -= HandleManualData;
    }

    private void HandleManualData(NetworkPlayerRef sender, System.ArraySegment<byte> data)
    {
        Debug.Log($"[PlayerController] Manual data from {sender}: {data.Count} bytes");
    }
}
