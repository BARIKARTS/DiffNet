using DifferentGames.Multiplayer;
using DifferentGames.Multiplayer.Attributes;
using DifferentGames.Multiplayer.Components;
using UnityEngine;

/// <summary>
/// SDK Kullanım Örneği.
/// Bu scripti bir NetworkRunner bileşenine sahip GameObject üzerine ekle.
///
/// Sahne Kurulumu:
///   1. Boş GameObject → Add Component → NetworkRunner
///   2. Boş GameObject → Add Component → NetworkBootstrap (bu script)
///   3. Inspector'dan playerPrefab ve callbacksTarget'ı ata
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

        // Sunucu tarafında yeni oyuncuya kendi karakter nesnesini spawn et
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
/// Örnek Player bileşeni. SDK API'sini nasıl kullanacağını gösterir.
/// </summary>
public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float _speed = 5f;

    // ─── [Networked] Değişkenler: Otomatik senkronize edilir ─────────────────

    [Networked] public float Health { get; set; } = 100f;
    [Networked] public int Score { get; set; } = 0;
    [Networked(serverOnly: true)] public bool IsAlive { get; set; } = true;

    // ─── Deterministik Döngü ─────────────────────────────────────────────────

    public override void FixedUpdateNetwork()
    {
        // Sadece bu nesnenin InputAuthority'si olan istemci input okur
        if (!HasInputAuthority) return;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        var dir = new Vector3(h, 0, v).normalized;

        if (dir.sqrMagnitude > 0.01f)
        {
            // Sunucuya input gönder
            RpcSendInput(dir, _speed);
        }
    }

    public override void Render()
    {
        // Görsel update - interpolasyon burada yapılır (NetworkTransform otomatik halleder)
    }

    // ─── [Rpc] Metodlar ───────────────────────────────────────────────────────

    /// <summary>İstemci → Sunucu: Hareket inputu gönder</summary>
    [Rpc(RpcTargets.Server, Reliable = false, Channel = 0)]
    public void RpcSendInput(Vector3 direction, float speed)
    {
        // Sunucu bu metodu alır ve fizik hesabı yapar
        if (!HasStateAuthority) return;
        transform.position += direction * speed * (1f / 60f); // TickRate ile normalize
    }

    /// <summary>Sunucu → Tümü: Hasar al</summary>
    [Rpc(RpcTargets.All, Reliable = true)]
    public void RpcTakeDamage(float amount, NetworkPlayerRef attacker)
    {
        if (HasStateAuthority)
        {
            Health -= amount;
            if (Health <= 0) IsAlive = false;
        }
        // Tüm istemcilerde efekt çal:
        Debug.Log($"{name} took {amount} damage from {attacker}! HP: {Health}");
    }

    /// <summary>Sunucu → Sahip: Skor güncelle (sadece owner görsün)</summary>
    [Rpc(RpcTargets.Owner)]
    public void RpcUpdateScore(int newScore)
    {
        Score = newScore;
        Debug.Log($"Your score: {Score}");
    }

    // ─── Manuel Veri Gönderimi ────────────────────────────────────────────────

    public void SendCustomData(byte[] customPacket)
    {
        // İleri seviye: Ham byte gönderimi (DeliveryMode belirt)
        SendManual(customPacket, DeliveryMode.ReliableOrdered);
    }

    private void OnEnable()
    {
        // Gelen Manuel veriyi dinle
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
