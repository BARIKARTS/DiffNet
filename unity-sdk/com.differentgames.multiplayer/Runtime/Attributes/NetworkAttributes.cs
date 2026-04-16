using System;

namespace DifferentGames.Multiplayer.Attributes
{
    /// <summary>
    /// Hangi RPC hedeflerine gönderileceğini belirler.
    /// </summary>
    public enum RpcTargets
    {
        /// <summary>Sadece bu nesnenin sahibi (Owner) olan istemciye çalışır.</summary>
        Owner,

        /// <summary>Sadece sunucu (Server) tarafında çalışır.</summary>
        Server,

        /// <summary>Tüm bağlı istemcilerde çalışır (sunucu dahil).</summary>
        All,

        /// <summary>Sahibi dışındaki tüm istemcilerde çalışır.</summary>
        Proxy
    }

    /// <summary>
    /// Bu attribute, bir field veya property'yi ağ üzerinden senkronize hale getirir.
    /// İşaretlenmiş değerler, her Tick'te sunucuya Snapshot Delta olarak gönderilir.
    /// <para>Kullanım: <c>[Networked] public float Health { get; set; }</c></para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class NetworkedAttribute : Attribute
    {
        /// <summary>
        /// Bu değişken güncellenmeden önce onInterpolate callback'i tetiklensin mi?
        /// Pozisyon gibi smooth edilmesi gereken veriler için kullanılır.
        /// </summary>
        public bool Interpolate { get; set; } = false;

        /// <summary>
        /// Bu değişken sadece sunucudan istemciye mi senkronize olsun (Server Authoritative)?
        /// true ise, istemci değişikliği ihmal edilir.
        /// </summary>
        public bool ServerOnly { get; set; } = false;

        public NetworkedAttribute() { }
        public NetworkedAttribute(bool interpolate = false, bool serverOnly = false)
        {
            Interpolate = interpolate;
            ServerOnly = serverOnly;
        }
    }

    /// <summary>
    /// Bu attribute, bir metodu Remote Procedure Call (RPC) haline getirir.
    /// İşaretlenen metot, ağ üzerinden belirli hedeflere çağrılabilir.
    /// <para>Kullanım: <c>[Rpc(RpcTargets.All)] public void OnPlayerDied() { }</c></para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class RpcAttribute : Attribute
    {
        /// <summary>Bu RPC'nin gönderileceği hedef(ler).</summary>
        public RpcTargets Targets { get; }

        /// <summary>
        /// RPC paketini Reliable mi Unreliable mı gönder?
        /// Kritik olaylar (ölüm, hasar) için Reliable, pozisyon snap için Unreliable uygundur.
        /// </summary>
        public bool Reliable { get; set; } = true;

        /// <summary>
        /// Channel Id: Aynı tipte RPC'lerin birbirini takip etmesi için.
        /// Farklı channel'lar birbirini bloklamaz (Head-of-Line Blocking'i engeller).
        /// </summary>
        public byte Channel { get; set; } = 0;

        public RpcAttribute(RpcTargets targets = RpcTargets.Server)
        {
            Targets = targets;
        }
    }

    /// <summary>
    /// Bu attribute, bir parametrenin RPC çağrısında kimin çağırdığına (Caller) ait bilgiyle
    /// otomatik doldurulmasını sağlar. Kullanıcı bu parametreyi açıkça geçmez.
    /// <para>Kullanım: <c>[Rpc(RpcTargets.Server)] public void Shoot([RpcCaller] PlayerRef caller) { }</c></para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public sealed class RpcCallerAttribute : Attribute { }
}
