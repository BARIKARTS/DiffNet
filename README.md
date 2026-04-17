<p align="center">
  <img src="C:\Users\berka\.gemini\antigravity\brain\7da884eb-c740-4b3d-997a-d3ddba836401\diffnet_banner_1776339869239.png" alt="DiffNet Banner" width="100%">
</p>

# 🌐 DiffNet Multiplayer SDK

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-blue?style=for-the-badge&logo=.net" alt=".NET 8">
  <img src="https://img.shields.io/badge/Unity-2022.3+-black?style=for-the-badge&logo=unity" alt="Unity">
  <img src="https://img.shields.io/badge/License-Commercial_Friendly-success?style=for-the-badge" alt="License">
  <img src="https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20Mobile-blueviolet?style=for-the-badge" alt="Platforms">
</p>

<p align="center">
  <b>High-performance, Zero-Allocation RUDP Networking for Real-time Multi-platform Games.</b>
</p>

---

<p align="center">
  <a href="#english">English Documentation</a> • <a href="#türkçe">Türkçe Dokümantasyon</a>
</p>

---

<a name="english"></a>
# 📂 Documentation (English)

> **Version:** 0.1.0 · **Platform:** .NET 8 / Unity 2022.3+ · **Package:** `com.differentgames.multiplayer`

---

## 📑 Table of Contents

1. [Project Overview and Philosophy](#1-project-overview-and-philosophy)
2. [Server Side and Dashboard](#2-server-side-and-dashboard)
3. [Unity SDK Setup and Connection](#3-unity-sdk-setup-and-connection)
4. [Basic Usage Examples](#4-basic-usage-examples)
5. [Interest Management (AOI)](#5-interest-management-aoi)
6. [Best Practices](#6-best-practices)

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

TCP forces all packets to arrive strictly in order. In games, order guarantee is unnecessary for data like **position updates** — old information is replaced by new information anyway. RUDP leaves this choice to the developer:

- `Unreliable` → Position, rotation (speed critical, loss insignificant)
- `ReliableUnordered` → Inventory updates (must arrive, order not required)
- `ReliableOrdered` → RPCs, critical events (death, purchase)

#### ⚡ Core Design Philosophies
*   **Zero-Allocation:** Using `ArrayPool`, `ref struct` + `stackalloc`, and `unsafe` pointers to avoid GC spikes.
*   **Pointer / Unsafe Usage:** Direct memory manipulation for zero-copy header deserialization and fast RPC dispatch.
*   **Tick-Based Synchronization:** 60Hz fixed-rate simulation ensuring deterministic gameplay across all clients.

---

## 2. Server Side and Dashboard

### 🖥️ Setting Up the Server

#### Requirements
- .NET 8 SDK
- Port 7777 (UDP - Game) and Port 5000 (HTTP - Dashboard) open

#### Build and Run
```bash
cd src/GameServer.App
dotnet build
dotnet run
```

### 📊 Admin Dashboard
Access the command center at `http://localhost:5000`. Monitoring features:
*   **CCU:** Instant connected player count.
*   **Performance:** CPU/RAM and GC Gen0/1/2 tracking.
*   **Traffic:** Real-time bandwidth and packet frequency.

---

## 3. Unity SDK Setup and Connection

### 📦 Installation
Add via **Package Manager** using the `com.differentgames.multiplayer/package.json` file.

### 🔌 Scene Setup
1. Create `[NetworkRunner]` GameObject.
2. Add `NetworkRunner` component.
3. Call `_runner.StartClient("differentgames.online", 7777);`

---

## 4. Basic Usage Examples

### 🛰️ `[Networked]` — State Synchronization
```csharp
public class PlayerHealth : NetworkBehaviour {
    [Networked] public float Health { get; set; } = 100f;
    [Networked(serverOnly: true)] public bool IsAlive { get; set; } = true;
    [Networked(interpolate: true)] public Vector3 RespawnPoint { get; set; }
}
```

### 📣 `[Rpc]` — Remote Procedure Call
```csharp
[Rpc(RpcTargets.Server, Reliable = false)]
public void RpcFire(Vector3 origin, Vector3 direction) {
    // Processed on server
}

[Rpc(RpcTargets.All, Reliable = true)]
public void RpcOnHit(Vector3 point) {
    // Visuals for everyone
}
```

---

## 5. Interest Management (AOI)

DiffNet's **Area of Interest** system ensures that each player only receives network updates for objects within their spatial grid radius — delivering massive bandwidth savings for large worlds.

### ⚙️ Inspector Setup — Prefab Registry

Every prefab that can be spawned over the network must be registered in the **NetworkRunner** Inspector.

1. Select your `[NetworkRunner]` GameObject in the scene.
2. In the **Prefab Registry** section, expand the `Network Prefabs` list.
3. Add each network-enabled prefab to the list — the **slot index + 1 becomes its `prefabId`**.

> ⚠️ **The order must be identical on Server and all Clients.** A mismatch will cause the wrong prefab to be instantiated on the client.

```
Network Prefabs List (0-indexed)
├─ [0] PlayerPrefab   → prefabId = 1
├─ [1] NPCPrefab      → prefabId = 2
└─ [2] ItemPrefab     → prefabId = 3
```

### 🔲 Configuring Object Scoping

By default, every `NetworkObject` is **Global** (synced to all players). To change this, add a `NetworkScoping` component:

| Mode | Behavior |
|---|---|
| `Global` | Synced to everyone, always (default — zero overhead) |
| `Spatial` | Synced only to players within the Grid AOI radius |
| `OwnerOnly` | Synced only to the player who owns the object |
| `Manual` | Ignored by the system — controlled by your own code |

```csharp
// Add NetworkScoping to a prefab to opt into Spatial filtering:
GetComponent<NetworkScoping>().Mode = ScopingMode.Spatial;
```

### 📡 AOI Callbacks in Game Code

Override these methods in your `DiffNetManagerBase` subclass to react to objects entering/leaving the AOI:

```csharp
public class MyGameManager : DiffNetManagerBase
{
    // Called CLIENT-SIDE when an object enters local player's AOI (or on initial connect).
    // Perfect for: nametag creation, VFX, minimap icons, audio sources.
    public override void OnObjectSpawned(NetworkObject netObj)
    {
        Debug.Log($"Object {netObj.ObjectId} entered AOI → show nametag");
    }

    // Called CLIENT-SIDE just before an object is destroyed for leaving AOI.
    // Perfect for: nametag cleanup, stopping audio, deregistering minimap icons.
    public override void OnObjectDespawned(NetworkObject netObj)
    {
        Debug.Log($"Object {netObj.ObjectId} left AOI → remove nametag");
    }
}
```

### 🔧 NetworkConfig AOI Settings

| Property | Default | Description |
|---|---|---|
| `EnableAOI` | `true` | Toggle AOI system on/off |
| `AOIGridCellSize` | `10` | World-units per grid cell |
| `AOIGridRadius` | `1` | Cells around player to include (1 = 3×3 = 9 cells) |

---

## 6. Best Practices

*   ✅ **Do:** Use `FixedUpdateNetwork()` for all network logic.
*   ✅ **Do:** Move visual updates to `Render()`.
*   ✅ **Do:** Register ALL spawnable prefabs in the Prefab Registry list.
*   ✅ **Do:** Add `NetworkScoping` (Spatial) to NPC/item prefabs in large worlds.
*   ❌ **Don't:** Change `[Networked]` variables inside `Update()`.
*   ❌ **Don't:** Call RPCs every frame inside `Update()`.
*   ❌ **Don't:** Change the Prefab Registry order after shipping — it will break `prefabId` lookup.

---

## 🤝 Support and Community

*   🌐 **Website:** [differentgames.online](https://differentgames.online)
*   📩 **Contact:** [differentgames.online/iletisim/](https://differentgames.online/iletisim/)
*   📧 **E-mail:** [sdk@differentgames.online](mailto:sdk@differentgames.online)

---

<p align="center">
  <i>This project is open-source friendly. It can be freely modified and used for commercial purposes.</i><br>
  <b>Open for everyone, built by DifferentGames Studio.</b>
</p>

---

<hr>

<a name="türkçe"></a>
# 📂 Dokümantasyon (Türkçe)

> **Sürüm:** 0.1.0 · **Platform:** .NET 8 / Unity 2022.3+ · **Paket:** `com.differentgames.multiplayer`

---

## 📑 İçindekiler

1. [Proje Özeti ve Felsefesi](#1-proje-%C3%B6zeti-ve-felsefesi-1)
2. [Sunucu Tarafı ve Dashboard](#2-sunucu-taraf%C4%B1-ve-dashboard-1)
3. [Unity SDK Kurulumu ve Bağlantı](#3-unity-sdk-kurulumu-ve-ba%C4%9Flant%C4%B1-1)
4. [Temel Kullanım Örnekleri](#4-temel-kullan%C4%B1m-%C3%B6rnekleri-1)
5. [İlgi Alanı Yönetimi (AOI)](#5-ilgi-alan%C4%B1-y%C3%B6netimi-aoi)
6. [En İyi Uygulamalar](#6-en-iyi-uygulamalar)

---

## 1. Proje Özeti ve Felsefesi

### Bu Altyapı Nedir?

DifferentGames Multiplayer altyapısı; düşük gecikme (low-latency), yüksek frekans güncelleme ve ölçeklenebilirlik gerektiren rekabetçi oyunlar için tasarlanmış **tam yığın (full-stack)** bir çözümdür.

### Neden Özel RUDP?

TCP tüm paketlerin sırayla gelmesini zorunlu kılar, bu da oyunlarda gereksiz gecikmeye yol açar. RUDP ile seçimi size bırakıyoruz:

- `Unreliable` → Pozisyon, rotasyon.
- `ReliableUnordered` → Envanter güncellemeleri.
- `ReliableOrdered` → Kritik RPC'ler.

#### ⚡ Temel Tasarım Felsefeleri
*   **Sıfır Tahsisat:** GC Spike'larını önlemek için `ArrayPool` ve `stackalloc` kullanımı.
*   **Performans:** Ham memory erişimi (pointers) ile ultra hızlı deserializasyon.
*   **Tick Yapısı:** 60Hz sabit simülasyon hızı ile tüm cihazlarda tutarlı sonuçlar.

---

## 2. Sunucu Tarafı ve Dashboard

### 🖥️ Sunucuyu Ayağa Kaldırma

#### Gereksinimler
- .NET 8 SDK
- Port 7777 (UDP) ve Port 5000 (HTTP) açık olmalı

#### Çalıştırma
```bash
cd src/GameServer.App
dotnet run
```

### 📊 Admin Dashboard
`http://localhost:5000` adresinden erişilebilen Dashboard:
*   **CCU:** Anlık oyuncu takibi.
*   **Performans:** Detaylı CPU/RAM ve GC metrikleri.
*   **Trafik:** Saniyedeki paket ve bant genişliği kullanımı.

---

## 3. Unity SDK Kurulumu ve Bağlantı

### 📦 Kurulum
**Package Manager** üzerinden `com.differentgames.multiplayer/package.json` dosyasını seçerek ekleyin.

### 🔌 Bağlantı
`_runner.StartClient("differentgames.online", 7777);` komutu ile sunucuya bağlanabilirsiniz.

---

## 4. Temel Kullanım Örnekleri

### 🛰️ `[Networked]` — State Senkronizasyonu
```csharp
public class PlayerHealth : NetworkBehaviour {
    [Networked] public float Health { get; set; } = 100f;
    [Networked(serverOnly: true)] public bool IsAlive { get; set; } = true;
}
```

### 📣 `[Rpc]` — Uzaktan Prosedür Çağrısı
```csharp
[Rpc(RpcTargets.Server)]
public void RpcFire(Vector3 origin, Vector3 direction) {
    // Sunucu tarafında doğrulanır
}
```

---

## 5. İlgi Alanı Yönetimi (AOI)

DiffNet'in **Alan İlgisi (AOI)** sistemi, her oyuncunun yalnızca kendi grid yarıçapındaki objeler için güncelleme almasını sağlar — büyük dünyalarda bant genişliğini dramatik ölçüde düşürür.

### ⚙️ Inspector Kurulumu — Prefab Registry

Ağ üzerinden spawn edilebilecek her prefab'ın **NetworkRunner** Inspector'ına kayıtlı olması gerekir.

1. Sahnedeki `[NetworkRunner]` GameObject'i seçin.
2. Inspector'daki **Prefab Registry** bölümünde `Network Prefabs` listesini açın.
3. Spawn edilebilen her prefab'ı listeye ekleyin — **slot indeksi + 1 = `prefabId`** olarak kullanılır.

> ⚠️ **Sıralama Server ve tüm Client'larda aynı olmalıdır.** Farklı sıra, yanlış prefab instantiate edilmesine ve oyun bozukluğuna yol açar.

```
Network Prefabs Listesi (0-indeksli)
├─ [0] PlayerPrefab   → prefabId = 1
├─ [1] NPCPrefab      → prefabId = 2
└─ [2] ItemPrefab     → prefabId = 3
```

### 🔲 Obje Kapsam Ayarı (NetworkScoping)

Varsayılan olarak her `NetworkObject` **Global** moddadır (herkese gönderilir). Bunu değiştirmek için `NetworkScoping` bileşeni ekleyin:

| Mod | Davranış |
|---|---|
| `Global` | Herkese her zaman senkronize (varsayılan — sıfır overhead) |
| `Spatial` | Yalnızca Grid AOI yarıçapı içindeki oyunculara gönderilir |
| `OwnerOnly` | Yalnızca objenin sahibi olan oyuncuya gönderilir |
| `Manual` | Sistem tarafından görmezden gelinir — kendi kodunuzla kontrol edin |

### 📡 AOI Callback'leri

`DiffNetManagerBase` alt sınıfınızda bu metodları override ederek AOI geçişlerine tepki verebilirsiniz:

```csharp
public class OyunYoneticim : DiffNetManagerBase
{
    // CLIENT tarafında — bir obje yerel oyuncunun AOI'sına girdiğinde tetiklenir.
    // Kullanım: isim etiketi oluşturma, VFX, minimap ikonu ekleme.
    public override void OnObjectSpawned(NetworkObject netObj)
    {
        Debug.Log($"Obje {netObj.ObjectId} AOI'ya girdi → isim etiketi oluştur");
    }

    // CLIENT tarafında — bir obje AOI'dan çıkmadan hemen önce tetiklenir.
    // Kullanım: isim etiketi temizleme, sesi durdurma, minimap'ten kaldırma.
    public override void OnObjectDespawned(NetworkObject netObj)
    {
        Debug.Log($"Obje {netObj.ObjectId} AOI'dan çıktı → isim etiketi sil");
    }
}
```

### 🔧 NetworkConfig AOI Ayarları

| Özellik | Varsayılan | Açıklama |
|---|---|---|
| `EnableAOI` | `true` | AOI sistemini açar/kapatır |
| `AOIGridCellSize` | `10` | Grid hücresi boyutu (Unity birimi) |
| `AOIGridRadius` | `1` | Oyuncunun etrafındaki hücre sayısı (1 = 3×3 = 9 hücre) |

---

## 6. En İyi Uygulamalar

*   ✅ **Yap:** Tüm ağ mantığını `FixedUpdateNetwork()` içine yazın.
*   ✅ **Yap:** Görsel güncellemeleri `Render()` içinde yapın.
*   ✅ **Yap:** Tüm spawn edilebilir prefabları Prefab Registry listesine ekleyin.
*   ✅ **Yap:** Büyük dünyalardaki NPC/item prefablarına `NetworkScoping (Spatial)` ekleyin.
*   ❌ **Yapma:** `[Networked]` değişkenlerini `Update()` içinde değiştirmeyin.
*   ❌ **Yapma:** Her frame `Update()` içinde RPC çağırmayın.
*   ❌ **Yapma:** Yayına çıktıktan sonra Prefab Registry sırasını değiştirmeyin — `prefabId` lookup bozulur.

---

## 🤝 Destek ve Topluluk

*   🌐 **Web Sitesi:** [differentgames.online](https://differentgames.online)
*   📩 **İletişim:** [differentgames.online/iletisim/](https://differentgames.online/iletisim/)
*   📧 **E-posta:** [sdk@differentgames.online](mailto:sdk@differentgames.online)

---

<p align="center">
  <i>Bu proje açık kaynak dostudur. Serbestçe değiştirilebilir ve ticari amaçlarla kullanılabilir.</i><br>
  <b>Herkes için açık, DifferentGames Studio tarafından geliştirildi.</b>
</p>
