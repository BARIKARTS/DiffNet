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

## 5. Best Practices

*   ✅ **Do:** Use `FixedUpdateNetwork()` for all network logic.
*   ✅ **Do:** Move visual updates to `Render()`.
*   ❌ **Don't:** Change `[Networked]` variables inside `Update()`.
*   ❌ **Don't:** Call RPCs every frame inside `Update()`.

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
5. [En İyi Uygulamalar](#5-en-iyi-uygulamalar)

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

## 5. En İyi Uygulamalar

*   ✅ **Do:** Tüm ağ mantığını `FixedUpdateNetwork()` içine yazın.
*   ✅ **Do:** Görsel güncellemeleri `Render()` içinde yapın.
*   ❌ **Don't:** `[Networked]` değişkenlerini `Update()` içinde değiştirmeyin.
*   ❌ **Don't:** Her frame `Update()` içinde RPC çağırmayın.

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
