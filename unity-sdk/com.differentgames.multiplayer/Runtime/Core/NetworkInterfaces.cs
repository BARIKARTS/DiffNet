using System.Runtime.CompilerServices;
using UnityEngine;

namespace DifferentGames.Multiplayer.Core
{
    /// <summary>
    /// NetworkRunner'ın her frame tetiklediği tick-based simülasyon arayüzü.
    /// Unity'nin Update()'inden bağımsız olarak, deterministik bir döngü sağlar.
    /// Geliştirici bu arayüzü implemente ederek ağ mantığını buraya yazar.
    /// <para>Kullanım: NetworkBehaviour içinde override edilir.</para>
    /// </summary>
    public interface INetworkSimulation
    {
        /// <summary>
        /// Sunucudan gelen Tick numarasına göre çağrılır.
        /// Tüm input okuma, state değiştirme ve ağ mantığı burada yazılmalıdır.
        /// Unity'nin FixedUpdate'i gibi fakat deterministik ve ağ senkronize.
        /// </summary>
        void FixedUpdateNetwork();

        /// <summary>
        /// Render frame'de çağrılır. Interpolasyon ve görsel güncelleme buraya yazılır.
        /// State değiştirme yapılmamalıdır.
        /// </summary>
        void Render();
    }

    /// <summary>
    /// Bir ağ nesnesinin sahiplenme ve durum bilgisini taşıyan arayüz.
    /// </summary>
    public interface INetworkState
    {
        /// <summary>Bu nesnenin ağdaki benzersiz kimliği.</summary>
        NetworkObjectId ObjectId { get; }

        /// <summary>Bu nesneyi kimin sahiplendiği (Owner).</summary>
        NetworkPlayerRef InputAuthority { get; }

        /// <summary>Bu nesne yapay zeka/sunucu tarafından mı kontrol ediliyor?</summary>
        bool HasStateAuthority { get; }

        /// <summary>Bu nesne yerel istemciye mi ait?</summary>
        bool HasInputAuthority { get; }

        /// <summary>Nesnenin bağlı olduğu tick. Henüz bağlanmadıysa 0.</summary>
        NetworkTick CurrentTick { get; }
    }

    /// <summary>
    /// Ağ olayları için callback arayüzü.
    /// NetworkRunner tarafından çağrılır.
    /// </summary>
    public interface INetworkCallbacks
    {
        void OnPlayerJoined(NetworkPlayerRef player) { }
        void OnPlayerLeft(NetworkPlayerRef player) { }
        void OnConnectedToServer(NetworkPlayerRef localPlayer) { }
        void OnDisconnectedFromServer() { }
        void OnShutdown() { }
    }

    /// <summary>
    /// RPC gönderim bilgisi. Gönderen tarafı ve güvenilirlik bilgisi taşır.
    /// </summary>
    public readonly struct RpcInfo
    {
        public readonly NetworkPlayerRef Source;
        public readonly NetworkTick SentTick;
        public readonly bool IsInvokedLocally;

        public RpcInfo(NetworkPlayerRef source, NetworkTick tick, bool local)
        {
            Source = source;
            SentTick = tick;
            IsInvokedLocally = local;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RpcInfo FromLocal(NetworkPlayerRef player, NetworkTick tick) =>
            new RpcInfo(player, tick, true);
    }
}
