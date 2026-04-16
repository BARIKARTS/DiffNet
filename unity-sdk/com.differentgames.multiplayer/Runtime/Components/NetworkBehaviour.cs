using System;
using System.Runtime.CompilerServices;
using DifferentGames.Multiplayer.Serialization;
using UnityEngine;

namespace DifferentGames.Multiplayer.Components
{
    /// <summary>
    /// Ağ üzerinden senkronize edilecek tüm bileşenlerin temel sınıfı.
    /// MonoBehaviour'dan türer; [Networked] ve [Rpc] attribute'larını çalıştırır,
    /// deterministik FixedUpdateNetwork döngüsünü sağlar ve Manuel veri gönderimini destekler.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public abstract class NetworkBehaviour : MonoBehaviour, INetworkSimulation, INetworkState
    {
        // ── INetworkState ────────────────────────────────────────────────────

        public NetworkObjectId ObjectId => _networkObject != null ? _networkObject.ObjectId : NetworkObjectId.Invalid;
        public NetworkPlayerRef InputAuthority => _networkObject != null ? _networkObject.InputAuthority : NetworkPlayerRef.None;
        public bool HasStateAuthority => Runner != null && Runner.IsServer;
        public bool HasInputAuthority => Runner != null && Runner.LocalPlayer == InputAuthority;
        public NetworkTick CurrentTick => Runner != null ? Runner.CurrentTick : NetworkTick.Invalid;

        /// <summary>Bu nesneye bağlı NetworkRunner referansı.</summary>
        public NetworkRunner Runner { get; internal set; }

        // ── İç Referanslar ───────────────────────────────────────────────────
        private NetworkObject _networkObject;
        private Core.RpcMethodInfo[] _cachedRpcMethods;
        private Core.NetworkedMemberInfo[] _cachedNetworkedMembers;

        // Manuel veri alımı için event
        public event Action<NetworkPlayerRef, ArraySegment<byte>> OnManualDataReceived;

        // ── Unity Lifecycle ───────────────────────────────────────────────────

        protected virtual void Awake()
        {
            _networkObject = GetComponent<NetworkObject>();
            // Attribute cache'ini hazırla (Reflection WarmUp zaten yapıldıysa O(1))
            _cachedRpcMethods = Core.NetworkAttributeCache.GetRpcMethods(GetType());
            _cachedNetworkedMembers = Core.NetworkAttributeCache.GetNetworkedMembers(GetType());
        }

        // ── INetworkSimulation ────────────────────────────────────────────────

        /// <summary>
        /// Sunucu Tick'ine bağlı deterministik güncelleme döngüsü.
        /// Tüm ağ mantığını buraya yaz (Input okuma, state değiştirme).
        /// Unity Update() ile KARIŞTIRMA. NetworkRunner tarafından çağrılır.
        /// </summary>
        public virtual void FixedUpdateNetwork() { }

        /// <summary>
        /// Görsel güncelleme için çağrılır (Interpolasyon burada yapılır).
        /// Ağ state'i değiştirme yapılmaz.
        /// </summary>
        public virtual void Render() { }

        // ── RPC Dispatch ──────────────────────────────────────────────────────

        /// <summary>
        /// Gelen ham byte verisini RPC olarak çözer ve ilgili metodu çağırır.
        /// NetworkRunner tarafından çağrılır; kullanıcı direkt çağırmaz.
        /// </summary>
        internal unsafe void DispatchRpc(byte* data, int length, NetworkPlayerRef sender)
        {
            if (length < 2) return;

            var reader = new NetworkReader(data, length);
            ushort rpcId = reader.ReadUShort(); // İlk 2 byte = RPC kimliği

            foreach (ref var rpcInfo in _cachedRpcMethods.AsSpan())
            {
                if (rpcInfo.RpcId != rpcId) continue;

                // Parametre listesini oluştur
                var parameters = rpcInfo.Method.GetParameters();
                var args = new object[parameters.Length];

                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;

                    // [RpcCaller] parametresini otomatik doldur
                    if (parameters[i].GetCustomAttributes(typeof(Attributes.RpcCallerAttribute), false).Length > 0)
                    {
                        args[i] = sender;
                        continue;
                    }

                    args[i] = ReadParameter(ref reader, paramType);
                }

                rpcInfo.Method.Invoke(this, args);
                return;
            }

#if UNITY_EDITOR
            Debug.LogWarning($"[NetworkBehaviour] Unknown RPC ID: {rpcId} on {name}");
#endif
        }

        /// <summary>
        /// Bir RPC'yi ağ üzerinden gönderir. Geliştirici metodun imzasına göre serialize eder.
        /// Normalde [Rpc] attribute'u taşıyan metodlar kod tarafından bu metodu çağırır.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected unsafe void SendRpc(ushort rpcId, Attributes.RpcTargets targets,
            bool reliable, Action<NetworkWriter> writePayload)
        {
            if (Runner == null) return;

            // Stack üzerinde buffer yarat (GC yok!)
            Span<byte> buffer = stackalloc byte[512];
            var writer = new NetworkWriter(buffer);
            writer.WriteUShort(rpcId);     // Header: RPC ID
            writer.WriteInt(ObjectId.Value == 0 ? 0 : (int)ObjectId.Value); // Header: Hangi nesne?
            writePayload(writer);           // Payload: Kullanıcı verileri

            var data = writer.ToSpan();
            var mode = reliable ? Core.DeliveryMode.ReliableOrdered : Core.DeliveryMode.Unreliable;

            Runner.SendRawToTargets(InputAuthority, targets, data, mode);
        }

        // ── Manuel Veri Gönderim ──────────────────────────────────────────────

        /// <summary>
        /// Ham byte verisi gönderir. İleri seviye kullanıcılar için.
        /// <para>Örnek: <c>SendManual(data, DeliveryMode.ReliableOrdered)</c></para>
        /// </summary>
        public void SendManual(ReadOnlySpan<byte> data, Core.DeliveryMode mode = Core.DeliveryMode.Unreliable)
        {
            Runner?.SendRaw(InputAuthority, data, mode);
        }

        /// <summary>
        /// Gelen manuel veriyi (DispatchManual üzerinden) tetikler.
        /// Sistem tarafından çağrılır.
        /// </summary>
        internal unsafe void DispatchManual(byte* data, int length, NetworkPlayerRef sender)
        {
            if (OnManualDataReceived == null) return;
            // byte* → managed ArraySegment (tek bir kopyalama zorunlu - managed event için)
            var managed = new byte[length];
            fixed (byte* dst = managed)
                Buffer.MemoryCopy(data, dst, length, length);
            OnManualDataReceived?.Invoke(sender, new ArraySegment<byte>(managed));
        }

        // ── Snapshot / State Senkronizasyonu ─────────────────────────────────

        /// <summary>
        /// [Networked] işaretli tüm değerleri bir byte buffer'a paketler (Tick snapshot için).
        /// Her Tick'te NetworkRunner bu metodu çağırır.
        /// </summary>
        internal unsafe void SerializeState(ref NetworkWriter writer)
        {
            foreach (var member in _cachedNetworkedMembers)
            {
                // ServerOnly değişkenleri: sadece sunucu serialize eder
                if (member.Attribute.ServerOnly && !HasStateAuthority) continue;

                object value = member.GetValue(this);
                WriteNetworkedValue(ref writer, value, member.MemberType);
            }
        }

        /// <summary>
        /// Gelen snapshot verisini [Networked] değişkenlerine yazar.
        /// </summary>
        internal unsafe void DeserializeState(ref NetworkReader reader)
        {
            foreach (var member in _cachedNetworkedMembers)
            {
                if (member.Attribute.ServerOnly && HasStateAuthority) continue;

                object value = ReadNetworkedValue(ref reader, member.MemberType);
                member.SetValue(this, value);
            }
        }

        // ── Yardımcı: Parametreleri Oku ──────────────────────────────────────

        private static unsafe object ReadParameter(ref NetworkReader reader, Type type)
        {
            if (type == typeof(int))           return reader.ReadInt();
            if (type == typeof(float))         return reader.ReadFloat();
            if (type == typeof(bool))          return reader.ReadBool();
            if (type == typeof(byte))          return reader.ReadByte();
            if (type == typeof(short))         return reader.ReadShort();
            if (type == typeof(ushort))        return reader.ReadUShort();
            if (type == typeof(uint))          return reader.ReadUInt();
            if (type == typeof(long))          return reader.ReadLong();
            if (type == typeof(double))        return reader.ReadDouble();
            if (type == typeof(Vector2))       return reader.ReadVector2();
            if (type == typeof(Vector3))       return reader.ReadVector3();
            if (type == typeof(Quaternion))    return reader.ReadQuaternionCompressed();
            if (type == typeof(NetworkPlayerRef)) return new NetworkPlayerRef(reader.ReadInt());
            if (type == typeof(NetworkObjectId))  return new NetworkObjectId((uint)reader.ReadInt());

            Debug.LogWarning($"[NetworkBehaviour] Unsupported RPC param type: {type.Name}");
            return null;
        }

        private static void WriteNetworkedValue(ref NetworkWriter writer, object value, Type type)
        {
            if (type == typeof(int))           { writer.WriteInt((int)value); return; }
            if (type == typeof(float))         { writer.WriteFloat((float)value); return; }
            if (type == typeof(bool))          { writer.WriteBool((bool)value); return; }
            if (type == typeof(byte))          { writer.WriteByte((byte)value); return; }
            if (type == typeof(short))         { writer.WriteShort((short)value); return; }
            if (type == typeof(ushort))        { writer.WriteUShort((ushort)value); return; }
            if (type == typeof(uint))          { writer.WriteUInt((uint)value); return; }
            if (type == typeof(long))          { writer.WriteLong((long)value); return; }
            if (type == typeof(double))        { writer.WriteDouble((double)value); return; }
            if (type == typeof(Vector2))       { writer.WriteVector2((Vector2)value); return; }
            if (type == typeof(Vector3))       { writer.WriteVector3((Vector3)value); return; }
            if (type == typeof(Quaternion))    { writer.WriteQuaternionCompressed((Quaternion)value); return; }
            if (type == typeof(NetworkPlayerRef)) { writer.WriteInt(((NetworkPlayerRef)value).Id); return; }
        }

        private static object ReadNetworkedValue(ref NetworkReader reader, Type type)
        {
            if (type == typeof(int))           return reader.ReadInt();
            if (type == typeof(float))         return reader.ReadFloat();
            if (type == typeof(bool))          return reader.ReadBool();
            if (type == typeof(byte))          return reader.ReadByte();
            if (type == typeof(short))         return reader.ReadShort();
            if (type == typeof(ushort))        return reader.ReadUShort();
            if (type == typeof(uint))          return reader.ReadUInt();
            if (type == typeof(long))          return reader.ReadLong();
            if (type == typeof(double))        return reader.ReadDouble();
            if (type == typeof(Vector2))       return reader.ReadVector2();
            if (type == typeof(Vector3))       return reader.ReadVector3();
            if (type == typeof(Quaternion))    return reader.ReadQuaternionCompressed();
            if (type == typeof(NetworkPlayerRef)) return new NetworkPlayerRef(reader.ReadInt());
            return null;
        }
    }
}
