# DifferentGames Multiplayer SDK
[English](#english) | [Türkçe](#türkçe)

<hr>

<a name="english"></a>
# Documentation (English)

> **Version:** 0.1.0 · **Platform:** .NET 8 / Unity 2022.3+ · **Package:** `com.differentgames.multiplayer`

---

## Table of Contents

1. [Project Overview and Philosophy](#1-project-overview-and-philosophy)
2. [Server Side and Dashboard](#2-server-side-and-dashboard)
3. [Unity SDK Setup and Connection](#3-unity-sdk-setup-and-connection)
4. [Basic Usage Examples](#4-basic-usage-examples)
5. [Best Practices](#5-best-practices)

---

## 1. Project Overview and Philosophy

### What is this Infrastructure?

DifferentGames Multiplayer infrastructure is a **full-stack** networking solution built from scratch for competitive multiplayer games requiring low-latency, high-frequency updates, and scalability.

It consists of two main layers:

| Layer | Technology | Description |
|--------|-----------|----------|
| **Server** | .NET 8 + ASP.NET Core (Co-Hosted) | UDP game loop and web dashboard in the same process |
| **Client SDK** | Unity C# (unsafe/pointer) | `[Networked]` / `[Rpc]` attribute API for Unity developers |

### Why Dedicated RUDP instead of TCP or HTTP?

| Feature | TCP | HTTP | RUDP (Our Approach) |
|---------|-----|------|--------------------------|
| Latency | High (handshake + HOL) | Very high | **Low** |
| Packet Guarantee | Required | Required | **Selectable** |
| Bandwidth Efficiency | Medium | Low | **High (Redundant ACK)** |
| Congestion Control | Kernel level | Kernel level | **Game-specific, dynamic RTO** |

TCP forces all packets to arrive strictly in order. In games, order guarantee is unnecessary for data like **position updates** — old information is replaced by new information anyway. RUDP leaves this choice to the developer:

- `Unreliable` → Position, rotation (speed critical, loss insignificant)
- `ReliableUnordered` → Inventory updates (must arrive, order not required)
- `ReliableOrdered` → RPCs, critical events (death, purchase)

### Core Design Philosophies

#### Zero-Allocation
Using the `new` keyword in the game loop triggers the Garbage Collector (GC) later and leads to frame drops (GC Spikes). In this infrastructure:

- `ArrayPool<byte>` → Packet buffers are reused
- `ref struct` + `stackalloc` → `NetworkWriter` / `NetworkReader` live on the stack
- `unsafe` pointers → Structs are copied via pointers without being moved to the heap
- `Span<T>` / `ReadOnlySpan<T>` → Slicing without array copying

#### Pointer / Unsafe Usage
`unsafe` blocks disable operations where C# normally adds safety layers (array bounds check, boxing, etc.). It is used only in these critical places:

- **RUDP Header deserialization:** Zero-copy with `RudpHeader*` pointer cast
- **NetworkWriter / NetworkReader:** No alignment overhead with `Unsafe.WriteUnaligned`
- **RPC dispatch:** `byte*` coming from the socket callback is read directly

#### Tick-Based Synchronization
Unity's `Update()` loop depends on frame rate (variable). Network simulation must be deterministic — the same input should always produce the same result. Therefore:

```
Server: 60 Ticks/sec (fixed)
    │
    ├── Each Tick: FixedUpdateNetwork() is called
    ├── Each Tick: State snapshot is packed and published
    └── Each Tick: RTO timer is checked (RUDP)

Client: Every Render frame:
    ├── FixedUpdateNetwork() → Runs based on tick count
    └── Render() → Smooth visuals with Interpolation
```

### Data Flow and Authority Model

```
[Client A]                    [Server]                    [Client B]
    │                              │                              │
    │── RpcSendInput(dir) ────────►│                              │
    │                              │ SimulateTick()               │
    │                              │ Physics, validation          │
    │                              │── Snapshot ────────────────►│
    │◄─────────────────── Snapshot ─┤                              │
```

**State Authority (Server Authority):** Game logic (damage calculation, physics, rules) always runs on the server. The client only sends **input**.

**Input Authority:** Determines who controls an object's input. The owner of the character is the client with `HasInputAuthority = true`.

> **Host Migration** (Future Version): When the server drops, the client with the lowest ping is selected as the new server and takes over the snapshot history.

---

## 2. Server Side and Dashboard

### Setting Up the Server

#### Requirements
- .NET 8 SDK
- Port 7777 (UDP - Game) and Port 5000 (HTTP - Dashboard) must be open

#### Build and Run

```bash
# Go to project directory
cd src/GameServer.App

# Build
dotnet build

# Run
dotnet run
```

Successful output:
```
info: GameServer.App.Services.ServerLifecycleManager[0]
      Game Server starting on port 7777...
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
```

Two services run simultaneously in a single process:
- **UDP Game Server** → Port 7777
- **ASP.NET Core Web Server (Kestrel)** → Port 5000

#### `appsettings.json` Configuration

```json
{
  "GameServer": {
    "UdpPort": 7777,
    "TickRate": 60,
    "MaxPlayers": 100
  },
  "AdminPanel": {
    "ApiKey": "WRITE_SECRET_KEY_HERE"
  }
}
```

### Admin Dashboard Access

While the server is running, open the following address in your browser:

```
http://localhost:5000
```

The Dashboard shows the following metrics in real-time via SignalR WebSocket:

| Metric | Description | Update Frequency |
|--------|----------|-------------------|
| **CCU** | Instant connected player count | 1 sec |
| **RAM** | Memory used by application (MB) | 1 sec |
| **CPU** | Estimated processor usage (%) | 1 sec |
| **GC Gen0/1/2** | Garbage collection counters | 1 sec |
| **Packets In/Out** | UDP packets per second | 1 sec |
| **Server Status** | Running / Stopped | Instant |

### Start / Stop Server from Dashboard

The **"Start Server"** and **"Stop Server"** buttons on the dashboard call the following REST endpoints:

```http
POST http://localhost:5000/api/admin/server/start
X-Api-Key: WRITE_SECRET_KEY_HERE

POST http://localhost:5000/api/admin/server/stop
X-Api-Key: WRITE_SECRET_KEY_HERE
```

Manual test with Curl:
```bash
curl -X POST http://localhost:5000/api/admin/server/start \
     -H "X-Api-Key: secret123"
```

### Player Kick / Admin Intervention

```http
POST http://localhost:5000/api/admin/players/{playerId}/kick
X-Api-Key: your-key
Content-Type: application/json

{ "reason": "Cheat detected" }
```

Successful response:
```json
{ "success": true, "player": "Player[42]", "reason": "Cheat detected" }
```

---

## 3. Unity SDK Setup and Connection

### Installation

#### Via Package Manager (Recommended)

1. Open `Window → Package Manager` in Unity
2. Click the **`+`** button at the top left
3. Select **"Add package from disk"**
4. Select the `com.differentgames.multiplayer/package.json` file

#### Alternative: `manifest.json`

Add to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.differentgames.multiplayer": "file:../../unity-sdk/com.differentgames.multiplayer"
  }
}
```

> **Note:** `Allow Unsafe Code` is already enabled in the Assembly definition. Don't forget to check the `Player Settings → Allow Unsafe Code` option in Unity project settings as well.

### Scene Setup

#### 1. Adding NetworkRunner

1. Create an empty `GameObject` in the scene, name it `[NetworkRunner]`
2. Add `Add Component → NetworkRunner`
3. Configure settings from the Inspector:

| Field | Value | Description |
|------|-------|----------|
| Tick Rate | 60 | Simulation step per second |
| Port | 7777 | Same port as the server |
| Server Address | 127.0.0.1 | Localhost for testing |

#### 2. Start Connection

```csharp
public class GameManager : MonoBehaviour, INetworkCallbacks
{
    [SerializeField] private NetworkRunner _runner;

    void Start()
    {
        // Start as server
        _runner.StartServer();

        // OR: Connect to server as client
        _runner.StartClient("192.168.1.100", 7777);
    }

    // Callbacks
    public void OnConnectedToServer(NetworkPlayerRef localPlayer)
        => Debug.Log($"Connected: {localPlayer}");

    public void OnPlayerJoined(NetworkPlayerRef player)
        => Debug.Log($"New player: {player}");

    public void OnPlayerLeft(NetworkPlayerRef player)
        => Debug.Log($"Player left: {player}");

    public void OnShutdown()
        => Debug.Log("Connection closed.");
}
```

### RUDP Delivery Modes — When to Use What?

```csharp
// 1. Unreliable — Position, rotation, animation blend weights
//    No problem if lost, a new value will arrive in the next Tick anyway.
transport.SendTo(player, data, DeliveryMode.Unreliable);

// 2. ReliableUnordered — Inventory updates, room list
//    Must definitely arrive, but order is not required.
transport.SendTo(player, data, DeliveryMode.ReliableUnordered);

// 3. ReliableOrdered — Critical events: death, purchase, RPCs
//    Both guaranteed and ordered. This has the highest bandwidth cost.
transport.SendTo(player, data, DeliveryMode.ReliableOrdered);
```

**Summary Rule:**

> If the answer to "does losing this data break the game?" is **Yes**, use `ReliableOrdered`; if the answer to "is order important?" is **No**, use `ReliableUnordered`; in all other cases, use `Unreliable`.

---

## 4. Basic Usage Examples

### 4.1 `[Networked]` — State Synchronization

Fields or properties marked with the `[Networked]` attribute are automatically synchronized with the server at each Tick.

```csharp
using DifferentGames.Multiplayer.Attributes;
using DifferentGames.Multiplayer.Components;

public class PlayerHealth : NetworkBehaviour
{
    // Basic usage: Synchronized in both directions
    [Networked]
    public float Health { get; set; } = 100f;

    // Server to client only (Server Authoritative)
    // Client cannot change this value
    [Networked(serverOnly: true)]
    public bool IsAlive { get; set; } = true;

    // Interpolation support for constantly changing data like position
    [Networked(interpolate: true)]
    public Vector3 RespawnPoint { get; set; }

    public override void FixedUpdateNetwork()
    {
        // Only the server can decrease Health
        if (!HasStateAuthority) return;

        if (Health <= 0 && IsAlive)
        {
            IsAlive = false;
            // This change will be propagated to all clients in the next Snapshot
        }
    }
}
```

> **Caution:** Change `[Networked]` variables **only** inside `FixedUpdateNetwork()`. Changes inside `Update()` or `LateUpdate()` will not be deterministic and will lead to synchronization errors.

---

### 4.2 `[Rpc]` — Remote Procedure Call

RPCs allow you to run a method on specific targets over the network.

```csharp
public class PlayerCombat : NetworkBehaviour
{
    [SerializeField] private ParticleSystem _muzzleFlash;
    [SerializeField] private float _damage = 25f;

    public override void FixedUpdateNetwork()
    {
        // Only the owner of this character reads input
        if (!HasInputAuthority) return;

        if (Input.GetButtonDown("Fire1"))
        {
            // Send "fire" command to server
            RpcFire(transform.position, transform.forward);
        }
    }

    // ── Client → Server ──────────────────────────────────────────────────
    // Unreliable: If fire input is lost, the user will press it again anyway
    [Rpc(RpcTargets.Server, Reliable = false, Channel = 1)]
    public void RpcFire(Vector3 origin, Vector3 direction)
    {
        // Server performs raycast, hit check is here (Cheat-proof!)
        if (Physics.Raycast(origin, direction, out RaycastHit hit, 100f))
        {
            if (hit.collider.TryGetComponent<PlayerHealth>(out var health))
            {
                // Notify all clients of the hit event
                RpcOnHit(hit.point, health.ObjectId, _damage);
            }
        }
    }

    // ── Server → All Clients ──────────────────────────────────────────
    // Reliable: Hit feedback must be seen by everyone
    [Rpc(RpcTargets.All, Reliable = true)]
    public void RpcOnHit(Vector3 hitPoint, NetworkObjectId targetId, float damage)
    {
        // This code runs on both the server and all clients
        SpawnHitEffect(hitPoint);

        // Only the server applies damage
        if (HasStateAuthority)
        {
            // Access object via targetId and apply damage...
        }
    }

    // ── Server → Owner Only ─────────────────────────────────────────
    [Rpc(RpcTargets.Owner)]
    public void RpcGrantKillReward(int scoreBonus)
    {
        // Runs only on the screen of the player who called this RPC
        HUD.ShowKillFeed($"+{scoreBonus} points!");
    }

    // ── [RpcCaller] — Automatically Get Sender ───────────────────────────
    [Rpc(RpcTargets.Server)]
    public void RpcRequestRespawn([RpcCaller] NetworkPlayerRef caller)
    {
        // You don't pass 'caller' parameter, SDK fills it automatically
        Debug.Log($"{caller} wants to respawn.");
        // Spawn logic here...
    }

    private void SpawnHitEffect(Vector3 point)
    {
        // Visual effect
    }
}
```

#### `RpcTargets` Summary Table

| Value | Description | Typical Usage |
|-------|----------|----------------|
| `Server` | Runs only on the server | Sending input |
| `All` | Server + all clients | Explosion effect, important events |
| `Owner` | Only on object owner | UI updates, special notifications |
| `Proxy` | All clients except owner | Animations of others |

---

### 4.3 `NetworkTransform` — Movement Synchronization and Interpolation

`NetworkTransform` is a built-in component that automatically synchronizes position and rotation.

#### Setup

1. Add `NetworkObject` to the object to be synchronized
2. Add `NetworkTransform` to the same object
3. Configure Inspector settings:

```
Send Rate Tick Interval : 1    → Send every Tick (60/s)
Interpolate             : ✓    → Client-side smooth movement
Sync Scale              : ✗    → Sync scale (turn off if unnecessary)
```

#### How it Works?

```
Server Tick N:   Position = (10, 0, 5)  → sent
Server Tick N+1: Position = (10.08, 0, 5) → sent

Client (Render frame):
  Alpha = 0.0  → Lerp(Tick N, Tick N+1, 0.0) = (10, 0, 5)
  Alpha = 0.5  → Lerp(Tick N, Tick N+1, 0.5) = (10.04, 0, 5)  ← Smooth!
  Alpha = 1.0  → Lerp(Tick N, Tick N+1, 1.0) = (10.08, 0, 5)
```

#### Custom Movement Logic

If you want to write your own movement logic instead of `NetworkTransform`:

```csharp
public class CustomMovement : NetworkBehaviour
{
    // [Networked] + interpolate: true combination does what NetworkTransform does
    [Networked(interpolate: true)]
    public Vector3 NetworkPosition { get; set; }

    private Vector3 _prevRenderPosition;
    private Vector3 _renderPosition;

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        // Physics calculation (on server)
        NetworkPosition += Vector3.forward * 5f * Runner.DeltaTime;
    }

    public override void Render()
    {
        // Interpolation (client visual)
        float alpha = Runner.InterpolationAlpha;
        transform.position = Vector3.Lerp(_prevRenderPosition, NetworkPosition, alpha);
    }
}
```

**Dead Reckoning (Extrapolation):** Calculate estimated position based on the velocity vector when the packet arrives late:

```csharp
public override void Render()
{
    float alpha = Runner.InterpolationAlpha;

    if (alpha > 1f) // Packet delayed, extrapolate
    {
        float excess = alpha - 1f;
        Vector3 extrapolated = NetworkPosition + _lastVelocity * excess * Runner.DeltaTime;
        transform.position = extrapolated;
    }
    else
    {
        transform.position = Vector3.Lerp(_prevRenderPosition, NetworkPosition, alpha);
    }
}
```

---

### 4.4 Manual Data Transmission

For advanced scenarios (custom protocol, binary format), you can send raw byte data.

#### Create Packet with NetworkWriter

```csharp
public class InventorySync : NetworkBehaviour
{
    public void SendInventoryUpdate(int slotIndex, int itemId, int quantity)
    {
        // Create 64 byte buffer on Stack (NO GC!)
        Span<byte> buffer = stackalloc byte[64];
        var writer = new NetworkWriter(buffer);

        // Specify packet type (custom protocol)
        writer.WriteByte(0xAA);          // Packet ID: InventoryUpdate
        writer.WriteInt(slotIndex);      // Slot index
        writer.WriteInt(itemId);         // Item ID
        writer.WriteInt(quantity);       // Amount
        writer.WriteFloat(Time.time);    // Timestamp

        // Send reliable to server
        SendManual(writer.ToSpan(), DeliveryMode.ReliableOrdered);
    }
}
```

#### Decoding Packet with NetworkReader

```csharp
private void OnEnable()
{
    OnManualDataReceived += HandlePacket;
}

private void OnDisable()
{
    OnManualDataReceived -= HandlePacket;
}

private unsafe void HandlePacket(NetworkPlayerRef sender, ArraySegment<byte> data)
{
    fixed (byte* ptr = data.Array)
    {
        var reader = new NetworkReader(ptr + data.Offset, data.Count);

        byte packetId = reader.ReadByte();

        switch (packetId)
        {
            case 0xAA: // InventoryUpdate
                int slot     = reader.ReadInt();
                int itemId   = reader.ReadInt();
                int quantity = reader.ReadInt();
                float time   = reader.ReadFloat();

                Debug.Log($"[{sender}] Inventory updated: Slot {slot} → Item {itemId} x{quantity}");
                ApplyInventoryUpdate(slot, itemId, quantity);
                break;

            default:
                Debug.LogWarning($"Unknown packet ID: {packetId:X2}");
                break;
        }
    }
}
```

#### Compressed Data Transmission

Use `WriteCompressedFloat` to save bandwidth:

```csharp
// Position delta between -10f and +10f, with 0.01f precision
// Normal float: 4 bytes × 3 = 12 bytes
// Compressed:   2 bytes × 3 = 6 bytes → 50% savings!
writer.WriteCompressedFloat(delta.x, -10f, 10f, 0.01f);
writer.WriteCompressedFloat(delta.y, -10f, 10f, 0.01f);
writer.WriteCompressedFloat(delta.z, -10f, 10f, 0.01f);

// On receiver side:
float x = reader.ReadCompressedFloat(-10f, 10f, 0.01f);
float y = reader.ReadCompressedFloat(-10f, 10f, 0.01f);
float z = reader.ReadCompressedFloat(-10f, 10f, 0.01f);
```

---

## 5. Best Practices

### ✅ Do's

#### Write all network logic inside `FixedUpdateNetwork()`

```csharp
// ✅ CORRECT: FixedUpdateNetwork is deterministic
public override void FixedUpdateNetwork()
{
    if (!HasInputAuthority) return;
    var dir = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
    RpcSendInput(dir);
}

// ❌ WRONG: Update() depends on frame rate, non-deterministic
void Update()
{
    RpcSendInput(/* ??? */); // Called at different frequencies on each device!
}
```

#### Move visual operations into `Render()`

```csharp
// ✅ CORRECT
public override void Render()
{
    _healthBar.fillAmount = Health / MaxHealth; // Visual only
}

// ❌ WRONG
public override void FixedUpdateNetwork()
{
    _healthBar.fillAmount = Health / MaxHealth; // Visuals shouldn't be in FixedUpdate
}
```

#### Avoid GC pressure with `stackalloc`

```csharp
// ✅ CORRECT: Stack memory, no GC
Span<byte> buffer = stackalloc byte[256];
var writer = new NetworkWriter(buffer);

// ❌ WRONG: Heap allocation on every call
byte[] buffer = new byte[256]; // GC pressure!
```

#### Perform `HasStateAuthority` check

```csharp
// ✅ CORRECT
public override void FixedUpdateNetwork()
{
    if (!HasStateAuthority) return; // Only the server runs this logic
    Health -= Time.deltaTime * poisonDamage;
}
```

---

### ❌ Don'ts

#### Call RPCs inside `Update()` or `LateUpdate()`

```csharp
// ❌ Sends RPC every frame → Bandwidth collapses
void Update()
{
    RpcSendPosition(transform.position); // 60+ RPC/s → CATASTROPHIC
}

// ✅ Send only when changed
public override void FixedUpdateNetwork()
{
    if (Vector3.Distance(NetworkPosition, transform.position) > 0.01f)
        RpcSendPosition(transform.position);
}
```

#### Change `[Networked]` variable inside `Update()`

```csharp
// ❌ Breaks determinism
void Update()
{
    Health -= damage; // Server and client run at different frequencies → Desync
}

// ✅ Only inside FixedUpdateNetwork
public override void FixedUpdateNetwork()
{
    if (HasStateAuthority) Health -= damage;
}
```

#### Code containing LINQ, Dictionary.Values iteration, or boxing

```csharp
// ❌ LINQ creates allocation on every call
var alivePlayers = _players.Where(p => p.IsAlive).ToList(); // 2x allocation!

// ✅ Use pre-allocated array
private readonly PlayerHealth[] _aliveBuffer = new PlayerHealth[100];
// ... fill with manual loop
```

#### Passing large structs by value

```csharp
// ❌ Copying on every call
void Process(BigStruct data) { }

// ✅ Pass by ref (no copying)
void Process(ref BigStruct data) { }
void Process(in BigStruct data) { } // readonly ref
```

---

### GC Reference Table

| Operation | GC Pressure | Alternative |
|-------|-----------|------------|
| `new byte[N]` | ✗ High | `ArrayPool<byte>.Shared.Rent(N)` |
| `new List<T>()` | ✗ High | `T[]` fixed array or `Span<T>` |
| `string.Format()` | ✗ Medium | `stackalloc char[]` or `StringBuilder` |
| `(object)myStruct` | ✗ Boxing | `ref struct` or generic method |
| `Span<byte> s = stackalloc byte[N]` | ✅ Zero | Use this |
| `ArrayPool<byte>.Shared.Rent(N)` | ✅ Zero | Use this |
| `NetworkWriter(stackalloc byte[256])` | ✅ Zero | SDK already does this |

---

## Support and Community

- 📧 **E-mail:** sdk@differentgames.com  
- 🐛 **Bug Report:** GitHub Issues  
- 📖 **Source Code:** `GameServer/src/` and `GameServer/unity-sdk/`

---

*© 2025 DifferentGames Studio. All rights reserved.*  
*This documentation is written for `com.differentgames.multiplayer` v0.1.0.*

<hr>

<a name="türkçe"></a>
# Dokümantasyon (Türkçe)

> **Sürüm:** 0.1.0 · **Platform:** .NET 8 / Unity 2022.3+ · **Paket:** `com.differentgames.multiplayer`

---

## İçindekiler

1. [Proje Özeti ve Felsefesi](#1-proje-özeti-ve-felsefesi)
2. [Sunucu Tarafı ve Dashboard](#2-sunucu-tarafı-ve-dashboard)
3. [Unity SDK Kurulumu ve Bağlantı](#3-unity-sdk-kurulumu-ve-bağlantı)
4. [Temel Kullanım Örnekleri](#4-temel-kullanım-örnekleri)
5. [Best Practices](#5-best-practices)

---

## 1. Proje Özeti ve Felsefesi

### Bu Altyapı Nedir?

DifferentGames Multiplayer altyapısı; düşük gecikme (low-latency), yüksek frekans güncelleme ve ölçeklenebilirlik gerektiren rekabetçi çok oyunculu oyunlar için sıfırdan inşa edilmiş **tam yığın (full-stack)** bir ağ çözümüdür.

İki ana katmandan oluşur:

| Katman | Teknoloji | Açıklama |
|--------|-----------|----------|
| **Sunucu** | .NET 8 + ASP.NET Core (Co-Hosted) | UDP oyun döngüsü ve web kontrol paneli aynı process içinde |
| **İstemci SDK** | Unity C# (unsafe/pointer) | Unity geliştiricileri için `[Networked]` / `[Rpc]` attribute API |

### Neden TCP veya HTTP Değil, Özel RUDP?

| Özellik | TCP | HTTP | RUDP (Bizim Yaklaşımımız) |
|---------|-----|------|--------------------------|
| Gecikme | Yüksek (bağlantı kurulum + HOL) | Çok yüksek | **Düşük** |
| Paket garantisi | Var (zorunlu) | Var (zorunlu) | **Seçilebilir** |
| Bant genişliği verimliliği | Orta | Düşük | **Yüksek (Redundant ACK)** |
| Congestion Control | Kernel seviyesinde | Kernel seviyesinde | **Oyuna özel, dinamik RTO** |

TCP, tüm paketlerin kesinlikle sırayla ulaşmasını zorlar. Oyunlarda ise **pozisyon güncellemeleri** gibi veriler için sıra garantisi gereksizdir — eski bilgi gelirse yeni bilgiyle değiştirilir zaten. RUDP, bu seçimi geliştirici eline bırakır:

- `Unreliable` → Pozisyon, rotasyon (hız kritik, kayıp önemsiz)
- `ReliableUnordered` → Envanter güncellemeleri (ulaşmalı, sıra şart değil)
- `ReliableOrdered` → RPC'ler, kritik olaylar (ölüm, satın alma)

### Temel Tasarım Felsefeleri

#### Zero-Allocation (Sıfır Çöp)
Oyun döngüsünde `new` anahtar kelimesini yazmak, Garbage Collector'ı (GC) ileride tetikler ve frame düşüşlerine (GC Spike) yol açar. Bu altyapıda:

- `ArrayPool<byte>` → Paket buffer'ları yeniden kullanılır
- `ref struct` + `stackalloc` → `NetworkWriter` / `NetworkReader` stack'te yaşar
- `unsafe` pointer → Struct'lar heap'e taşınmadan pointer üzerinden kopyalanır
- `Span<T>` / `ReadOnlySpan<T>` → Array kopyalamadan dilim okuma

#### Pointer / Unsafe Kullanımı
`unsafe` bloklar, C#'ın normalde güvenlik katmanı eklediği işlemleri (array bounds check, boxing vb.) devre dışı bırakır. Bu altyapıda yalnızca şu kritik yerlerde kullanılır:

- **RUDP Header deserializasyonu:** `RudpHeader*` pointer cast ile sıfır kopya
- **NetworkWriter / NetworkReader:** `Unsafe.WriteUnaligned` ile hizalama overhead yok
- **RPC dispatch:** Socket callback'inden gelen `byte*` direkt okunur

#### Tick-Based Synchronization
Unity'nin `Update()` döngüsü frame hızına bağlıdır (değişken). Ağ simülasyonu ise deterministik olmalıdır — aynı input, her zaman aynı sonucu üretmeli. Bu yüzden:

```
Sunucu: 60 Tick/sn (sabit)
    │
    ├── Her Tick: FixedUpdateNetwork() çağrılır
    ├── Her Tick: State snapshot paketlenir ve yayınlanır
    └── Her Tick: RTO zamanlayıcısı kontrol edilir (RUDP)

İstemci: Her Render frame'de:
    ├── FixedUpdateNetwork() → Tick sayısına göre çalışır
    └── Render() → Interpolation ile smooth görüntü
```

### Veri Akışı ve Otorite Modeli

```
[İstemci A]                    [Sunucu]                    [İstemci B]
    │                              │                              │
    │── RpcSendInput(dir) ────────►│                              │
    │                              │ SimulateTick()               │
    │                              │ Physics, validation          │
    │                              │── Snapshot ────────────────►│
    │◄─────────────────── Snapshot ─┤                              │
```

**State Authority (Sunucu Otoritesi):** Oyun mantığı (hasar hesabı, fizik, kurallar) her zaman sunucuda çalışır. İstemci sadece **input** gönderir.

**Input Authority:** Bir nesnenin inputunu kimin kontrol ettiğini belirler. Karakterin sahibi, `HasInputAuthority = true` olan istemcidir.

> **Host Migration** (İleri Sürüm): Sunucu düştüğünde, en düşük ping'e sahip istemci yeni sunucu olarak seçilir ve snapshot geçmişini devralır.

---

## 2. Sunucu Tarafı ve Dashboard

### Sunucuyu Ayağa Kaldırma

#### Gereksinimler
- .NET 8 SDK
- Port 7777 (UDP - Oyun) ve Port 5000 (HTTP - Dashboard) açık olmalı

#### Derleme ve Çalıştırma

```bash
# Proje dizinine git
cd src/GameServer.App

# Derle
dotnet build

# Çalıştır
dotnet run
```

Başarılı çıktı:
```
info: GameServer.App.Services.ServerLifecycleManager[0]
      Game Server starting on port 7777...
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
```

Tek bir process içinde iki servis aynı anda çalışır:
- **UDP Oyun Sunucusu** → Port 7777
- **ASP.NET Core Web Sunucusu (Kestrel)** → Port 5000

#### `appsettings.json` Yapılandırması

```json
{
  "GameServer": {
    "UdpPort": 7777,
    "TickRate": 60,
    "MaxPlayers": 100
  },
  "AdminPanel": {
    "ApiKey": "BURAYA_GIZLI_ANAHTAR_YAZ"
  }
}
```

### Admin Dashboard Erişimi

Sunucu çalışırken tarayıcıda şu adresi aç:

```
http://localhost:5000
```

Dashboard gerçek zamanlı şu metrikleri SignalR WebSocket üzerinden gösterir:

| Metrik | Açıklama | Güncelleme Sıklığı |
|--------|----------|-------------------|
| **CCU** | Anlık bağlı oyuncu sayısı | 1 sn |
| **RAM** | Uygulamanın kullandığı bellek (MB) | 1 sn |
| **CPU** | Tahmini işlemci kullanımı (%) | 1 sn |
| **GC Gen0/1/2** | Çöp toplama sayaçları | 1 sn |
| **Packets In/Out** | Saniyedeki UDP paket sayısı | 1 sn |
| **Server Status** | Running / Stopped | Anlık |

### Sunucuyu Dashboard'dan Başlat / Durdur

Dashboard üzerindeki **"Start Server"** ve **"Stop Server"** butonları şu REST endpoint'lerini çağırır:

```http
POST http://localhost:5000/api/admin/server/start
X-Api-Key: BURAYA_GIZLI_ANAHTAR_YAZ

POST http://localhost:5000/api/admin/server/stop
X-Api-Key: BURAYA_GIZLI_ANAHTAR_YAZ
```

Curl ile manuel test:
```bash
curl -X POST http://localhost:5000/api/admin/server/start \
     -H "X-Api-Key: secret123"
```

### Oyuncu Kick / Admin Müdahalesi

```http
POST http://localhost:5000/api/admin/players/{playerId}/kick
X-Api-Key: your-key
Content-Type: application/json

{ "reason": "Hile tespiti" }
```

Başarılı yanıt:
```json
{ "success": true, "player": "Player[42]", "reason": "Hile tespiti" }
```

---

## 3. Unity SDK Kurulumu ve Bağlantı

### Kurulum

#### Package Manager ile (Önerilen)

1. Unity'de `Window → Package Manager` aç
2. Sol üstteki **`+`** butonuna tıkla
3. **"Add package from disk"** seç
4. `com.differentgames.multiplayer/package.json` dosyasını seç

#### Alternatif: `manifest.json`

`Packages/manifest.json` dosyasına ekle:

```json
{
  "dependencies": {
    "com.differentgames.multiplayer": "file:../../unity-sdk/com.differentgames.multiplayer"
  }
}
```

> **Not:** Assembly definition'da `Allow Unsafe Code` zaten aktif bırakılmıştır. Unity proje ayarlarında `Player Settings → Allow Unsafe Code` seçeneğini de işaretlemeyi unutma.

### Sahne Kurulumu

#### 1. NetworkRunner Ekleme

1. Sahneye boş bir `GameObject` oluştur, adını `[NetworkRunner]` koy
2. `Add Component → NetworkRunner` ekle
3. Inspector'dan ayarları yapılandır:

| Alan | Değer | Açıklama |
|------|-------|----------|
| Tick Rate | 60 | Saniyedeki simülasyon adımı |
| Port | 7777 | Sunucu ile aynı port |
| Server Address | 127.0.0.1 | Test için localhost |

#### 2. Bağlantıyı Başlat

```csharp
public class GameManager : MonoBehaviour, INetworkCallbacks
{
    [SerializeField] private NetworkRunner _runner;

    void Start()
    {
        // Sunucu olarak başlat
        _runner.StartServer();

        // VEYA: İstemci olarak sunucuya bağlan
        _runner.StartClient("192.168.1.100", 7777);
    }

    // Callback'ler
    public void OnConnectedToServer(NetworkPlayerRef localPlayer)
        => Debug.Log($"Bağlandı: {localPlayer}");

    public void OnPlayerJoined(NetworkPlayerRef player)
        => Debug.Log($"Yeni oyuncu: {player}");

    public void OnPlayerLeft(NetworkPlayerRef player)
        => Debug.Log($"Oyuncu ayrıldı: {player}");

    public void OnShutdown()
        => Debug.Log("Bağlantı kesildi.");
}
```

### RUDP Delivery Modları — Ne Zaman Ne Kullanılır?

```csharp
// 1. Unreliable — Pozisyon, rotasyon, animasyon blend ağırlıkları
//    Kaybolursa sorun olmaz, bir sonraki Tick'te zaten yeni değer gelecek.
transport.SendTo(player, data, DeliveryMode.Unreliable);

// 2. ReliableUnordered — Envanter güncellemeleri, oda listesi
//    Kesinlikle ulaşmalı ama sıra şart değil.
transport.SendTo(player, data, DeliveryMode.ReliableUnordered);

// 3. ReliableOrdered — Kritik olaylar: ölüm, satın alma, RPC'ler
//    Hem garantili, hem sıralı. Bant genişliği maliyeti en yüksek bu.
transport.SendTo(player, data, DeliveryMode.ReliableOrdered);
```

**Özet Kural:**

> Eğer "bu verinin kaybolması oyunu bozar mı?" sorusunun cevabı **Evet** ise `ReliableOrdered`; "sıra önemli mi?" sorusunun cevabı **Hayır** ise `ReliableUnordered`; diğer tüm durumlarda `Unreliable` kullan.

---

## 4. Temel Kullanım Örnekleri

### 4.1 `[Networked]` — State Senkronizasyonu

`[Networked]` attribute'u ile işaretlenen field veya property'ler, her Tick'te otomatik olarak sunucu ile senkronize edilir.

```csharp
using DifferentGames.Multiplayer.Attributes;
using DifferentGames.Multiplayer.Components;

public class PlayerHealth : NetworkBehaviour
{
    // Temel kullanım: Her iki yönde senkronize
    [Networked]
    public float Health { get; set; } = 100f;

    // Sadece sunucudan istemciye (Server Authoritative)
    // İstemci bu değeri değiştiremez
    [Networked(serverOnly: true)]
    public bool IsAlive { get; set; } = true;

    // Pozisyon gibi sürekli değişen veriler için interpolasyon desteği
    [Networked(interpolate: true)]
    public Vector3 RespawnPoint { get; set; }

    public override void FixedUpdateNetwork()
    {
        // Sadece sunucu Health'i azaltabilir
        if (!HasStateAuthority) return;

        if (Health <= 0 && IsAlive)
        {
            IsAlive = false;
            // Bu değişiklik bir sonraki Snapshot'ta tüm istemcilere yayılır
        }
    }
}
```

> **Dikkat:** `[Networked]` değişkenlerini **sadece** `FixedUpdateNetwork()` içinde değiştir. `Update()` veya `LateUpdate()` içindeki değişiklikler deterministik olmaz ve senkronizasyon hatalarına yol açar.

---

### 4.2 `[Rpc]` — Uzaktan Prosedür Çağrısı

RPC'ler, ağ üzerinden belirli hedeflerde bir metodu çalıştırmanı sağlar.

```csharp
public class PlayerCombat : NetworkBehaviour
{
    [SerializeField] private ParticleSystem _muzzleFlash;
    [SerializeField] private float _damage = 25f;

    public override void FixedUpdateNetwork()
    {
        // Sadece bu karakterin sahibi input okur
        if (!HasInputAuthority) return;

        if (Input.GetButtonDown("Fire1"))
        {
            // Sunucuya "ateş et" komutu gönder
            RpcFire(transform.position, transform.forward);
        }
    }

    // ── İstemci → Sunucu ────────────────────────────────────────────────────
    // Unreliable: Ateş inputu kaybolursa kullanıcı zaten tekrar basacak
    [Rpc(RpcTargets.Server, Reliable = false, Channel = 1)]
    public void RpcFire(Vector3 origin, Vector3 direction)
    {
        // Sunucu raycast yapar, hit kontrolü burada (Cheat-proof!)
        if (Physics.Raycast(origin, direction, out RaycastHit hit, 100f))
        {
            if (hit.collider.TryGetComponent<PlayerHealth>(out var health))
            {
                // Tüm istemcilere hasar olayını bildir
                RpcOnHit(hit.point, health.ObjectId, _damage);
            }
        }
    }

    // ── Sunucu → Tüm İstemciler ─────────────────────────────────────────────
    // Reliable: Hit feedback herkesin görmesi şart
    [Rpc(RpcTargets.All, Reliable = true)]
    public void RpcOnHit(Vector3 hitPoint, NetworkObjectId targetId, float damage)
    {
        // Bu kod hem sunucuda hem tüm istemcilerde çalışır
        SpawnHitEffect(hitPoint);

        // Sadece sunucu hasarı uygular
        if (HasStateAuthority)
        {
            // targetId üzerinden nesneye eriş ve hasar uygula...
        }
    }

    // ── Sunucu → Sadece Sahip ────────────────────────────────────────────────
    [Rpc(RpcTargets.Owner)]
    public void RpcGrantKillReward(int scoreBonus)
    {
        // Sadece bu RPC'yi çağıran oyuncunun ekranında çalışır
        HUD.ShowKillFeed($"+{scoreBonus} puan!");
    }

    // ── [RpcCaller] — Göndereni Otomatik Al ─────────────────────────────────
    [Rpc(RpcTargets.Server)]
    public void RpcRequestRespawn([RpcCaller] NetworkPlayerRef caller)
    {
        // 'caller' parametresini sen geçmezsin, SDK otomatik doldurur
        Debug.Log($"{caller} yeniden doğmak istiyor.");
        // Spawn mantığı burada...
    }

    private void SpawnHitEffect(Vector3 point)
    {
        // Görsel efekt
    }
}
```

#### `RpcTargets` Özet Tablosu

| Değer | Açıklama | Tipik Kullanım |
|-------|----------|----------------|
| `Server` | Sadece sunucuda çalışır | Input göndermek |
| `All` | Sunucu + tüm istemciler | Patlama efekti, önemli olaylar |
| `Owner` | Sadece nesne sahibinde | UI guncellemesi, özel bildirimler |
| `Proxy` | Sahip hariç tüm istemciler | Diğerlerinin animasyonu |

---

### 4.3 `NetworkTransform` — Hareket Senkronizasyonu ve Interpolasyon

`NetworkTransform`, pozisyon ve rotasyonu otomatik senkronize eden hazır bileşendir.

#### Kurulum

1. Senkronize edilecek nesneye `NetworkObject` ekle
2. Aynı nesneye `NetworkTransform` ekle
3. Inspector ayarlarını yap:

```
Send Rate Tick Interval : 1    → Her Tick'te gönder (60/sn)
Interpolate             : ✓    → Client-side smooth hareket
Sync Scale              : ✗    → Ölçek gönderme (gereksizse kapat)
```

#### Nasıl Çalışır?

```
Sunucu Tick N:   Pozisyon = (10, 0, 5)  → gönderildi
Sunucu Tick N+1: Pozisyon = (10.08, 0, 5) → gönderildi

İstemci (Render frame):
  Alpha = 0.0  → Lerp(Tick N, Tick N+1, 0.0) = (10, 0, 5)
  Alpha = 0.5  → Lerp(Tick N, Tick N+1, 0.5) = (10.04, 0, 5)  ← Smooth!
  Alpha = 1.0  → Lerp(Tick N, Tick N+1, 1.0) = (10.08, 0, 5)
```

#### Özel Hareket Mantığı

Eğer `NetworkTransform` yerine kendi hareket mantığını yazmak istersen:

```csharp
public class CustomMovement : NetworkBehaviour
{
    // [Networked] + interpolate: true kombinasyonu NetworkTransform'un yaptığını yapar
    [Networked(interpolate: true)]
    public Vector3 NetworkPosition { get; set; }

    private Vector3 _prevRenderPosition;
    private Vector3 _renderPosition;

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        // Fizik hesabı (sunucuda)
        NetworkPosition += Vector3.forward * 5f * Runner.DeltaTime;
    }

    public override void Render()
    {
        // Interpolation (istemci görsel)
        float alpha = Runner.InterpolationAlpha;
        transform.position = Vector3.Lerp(_prevRenderPosition, NetworkPosition, alpha);
    }
}
```

**Dead Reckoning (Extrapolation):** Paket geç geldiğinde nesnenin hız vektörüne göre tahmini konumu hesapla:

```csharp
public override void Render()
{
    float alpha = Runner.InterpolationAlpha;

    if (alpha > 1f) // Paket gecikti, extrapolate et
    {
        float excess = alpha - 1f;
        Vector3 extrapolated = NetworkPosition + _lastVelocity * excess * Runner.DeltaTime;
        transform.position = extrapolated;
    }
    else
    {
        transform.position = Vector3.Lerp(_prevRenderPosition, NetworkPosition, alpha);
    }
}
```

---

### 4.4 Manuel Veri Gönderimi

İleri seviye senaryolar için (özel protokol, binary format), ham byte verisi gönderebilirsin.

#### NetworkWriter ile Paket Oluştur

```csharp
public class InventorySync : NetworkBehaviour
{
    public void SendInventoryUpdate(int slotIndex, int itemId, int quantity)
    {
        // Stack'te 64 byte buffer yarat (GC YOK!)
        Span<byte> buffer = stackalloc byte[64];
        var writer = new NetworkWriter(buffer);

        // Paketin tipini belirt (özel protokol)
        writer.WriteByte(0xAA);          // Packet ID: InventoryUpdate
        writer.WriteInt(slotIndex);      // Slot indeksi
        writer.WriteInt(itemId);         // Item ID
        writer.WriteInt(quantity);       // Miktar
        writer.WriteFloat(Time.time);    // Zaman damgası

        // Sunucuya reliable gönder
        SendManual(writer.ToSpan(), DeliveryMode.ReliableOrdered);
    }
}
```

#### NetworkReader ile Paketin Çözümlenmesi

```csharp
private void OnEnable()
{
    OnManualDataReceived += HandlePacket;
}

private void OnDisable()
{
    OnManualDataReceived -= HandlePacket;
}

private unsafe void HandlePacket(NetworkPlayerRef sender, ArraySegment<byte> data)
{
    fixed (byte* ptr = data.Array)
    {
        var reader = new NetworkReader(ptr + data.Offset, data.Count);

        byte packetId = reader.ReadByte();

        switch (packetId)
        {
            case 0xAA: // InventoryUpdate
                int slot     = reader.ReadInt();
                int itemId   = reader.ReadInt();
                int quantity = reader.ReadInt();
                float time   = reader.ReadFloat();

                Debug.Log($"[{sender}] Envanter güncellendi: Slot {slot} → Item {itemId} x{quantity}");
                ApplyInventoryUpdate(slot, itemId, quantity);
                break;

            default:
                Debug.LogWarning($"Bilinmeyen paket ID: {packetId:X2}");
                break;
        }
    }
}
```

#### Sıkıştırılmış Veri Gönderimi

Bant genişliğini tasarruf etmek için `WriteCompressedFloat` kullan:

```csharp
// Pozisyon delta'sı -10f ile +10f arasında, 0.01f hassasiyetle
// Normal float: 4 byte × 3 = 12 byte
// Compressed:   2 byte × 3 = 6 byte → %50 tasarruf!
writer.WriteCompressedFloat(delta.x, -10f, 10f, 0.01f);
writer.WriteCompressedFloat(delta.y, -10f, 10f, 0.01f);
writer.WriteCompressedFloat(delta.z, -10f, 10f, 0.01f);

// Alıcı tarafta:
float x = reader.ReadCompressedFloat(-10f, 10f, 0.01f);
float y = reader.ReadCompressedFloat(-10f, 10f, 0.01f);
float z = reader.ReadCompressedFloat(-10f, 10f, 0.01f);
```

---

## 5. Best Practices

### ✅ Yapılması Gerekenler

#### Tüm ağ mantığını `FixedUpdateNetwork()` içine yaz

```csharp
// ✅ DOĞRU: FixedUpdateNetwork deterministik
public override void FixedUpdateNetwork()
{
    if (!HasInputAuthority) return;
    var dir = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
    RpcSendInput(dir);
}

// ❌ YANLIŞ: Update() frame hızına bağlıdır, non-deterministik
void Update()
{
    RpcSendInput(/* ??? */); // Her cihazda farklı frekansta çağrılır!
}
```

#### Görsel işlemleri `Render()` içinde yap

```csharp
// ✅ DOĞRU
public override void Render()
{
    _healthBar.fillAmount = Health / MaxHealth; // Sadece görsel
}

// ❌ YANLIŞ
public override void FixedUpdateNetwork()
{
    _healthBar.fillAmount = Health / MaxHealth; // Görsel FixedUpdate'e girmesin
}
```

#### `stackalloc` ile GC baskısından kaç

```csharp
// ✅ DOĞRU: Stack bellek, GC yok
Span<byte> buffer = stackalloc byte[256];
var writer = new NetworkWriter(buffer);

// ❌ YANLIŞ: Her çağrıda heap allocation
byte[] buffer = new byte[256]; // GC baskısı!
```

#### `HasStateAuthority` kontrolü yap

```csharp
// ✅ DOĞRU
public override void FixedUpdateNetwork()
{
    if (!HasStateAuthority) return; // Sadece sunucu bu mantığı çalıştırır
    Health -= Time.deltaTime * poisonDamage;
}
```

---

### ❌ Yapılmaması Gerekenler

#### `Update()` veya `LateUpdate()` içinde RPC çağırma

```csharp
// ❌ Her frame RPC gönderir → Bant genişliği çöker
void Update()
{
    RpcSendPosition(transform.position); // 60+ RPC/sn → FELAKETİK
}

// ✅ Sadece değiştiğinde gönder
public override void FixedUpdateNetwork()
{
    if (Vector3.Distance(NetworkPosition, transform.position) > 0.01f)
        RpcSendPosition(transform.position);
}
```

#### `[Networked]` değişkeni `Update()` içinde değiştirme

```csharp
// ❌ Determinizmi bozar
void Update()
{
    Health -= damage; // Sunucu ve istemci farklı frekansta çalışır → Desync
}

// ✅ Sadece FixedUpdateNetwork içinde
public override void FixedUpdateNetwork()
{
    if (HasStateAuthority) Health -= damage;
}
```

#### LINQ, Dictionary.Values iterasyonu veya boxing içeren kod

```csharp
// ❌ LINQ her çağrıda allocation yaratır
var alivePlayers = _players.Where(p => p.IsAlive).ToList(); // 2x allocation!

// ✅ Önceden tahsis edilmiş array kullan
private readonly PlayerHealth[] _aliveBuffer = new PlayerHealth[100];
// ... manuel döngü ile doldur
```

#### Büyük struct'ları değer olarak geçirme

```csharp
// ❌ Her çağrıda kopyalama
void Process(BigStruct data) { }

// ✅ ref ile geç (kopyalama yok)
void Process(ref BigStruct data) { }
void Process(in BigStruct data) { } // readonly ref
```

---

### GC Referans Tablosu

| İşlem | GC Baskısı | Alternatif |
|-------|-----------|------------|
| `new byte[N]` | ✗ Yüksek | `ArrayPool<byte>.Shared.Rent(N)` |
| `new List<T>()` | ✗ Yüksek | `T[]` sabit dizi veya `Span<T>` |
| `string.Format()` | ✗ Orta | `stackalloc char[]` veya `StringBuilder` |
| `(object)myStruct` | ✗ Boxing | `ref struct` veya generic metot |
| `Span<byte> s = stackalloc byte[N]` | ✅ Sıfır | Bunu kullan |
| `ArrayPool<byte>.Shared.Rent(N)` | ✅ Sıfır | Bunu kullan |
| `NetworkWriter(stackalloc byte[256])` | ✅ Sıfır | SDK zaten bu şekilde |

---

## Destek ve Topluluk

- 📧 **E-posta:** sdk@differentgames.com  
- 🐛 **Hata Bildirimi:** GitHub Issues  
- 📖 **Kaynak Kod:** `GameServer/src/` ve `GameServer/unity-sdk/`

---

*© 2025 DifferentGames Studio. Tüm hakları saklıdır.*  
*Bu dokümantasyon, `com.differentgames.multiplayer` v0.1.0 için yazılmıştır.*
