using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DifferentGames.Multiplayer.Attributes;
using UnityEngine;

namespace DifferentGames.Multiplayer.Core
{
    /// <summary>
    /// Attribute cache'ini başlatma (initialization) sırasında, Reflection ile bir kez doldurur.
    /// Sonraki frame'lerde O(1) lookup yapılmasını sağlar. GC baskısı yaratmaz.
    /// </summary>
    public static class NetworkAttributeCache
    {
        // Per-Type cache: Her MonoBehaviour tipi için RPC metodlarını saklar
        private static readonly Dictionary<Type, RpcMethodInfo[]> _rpcCache
            = new Dictionary<Type, RpcMethodInfo[]>();

        // Per-Type cache: Her MonoBehaviour tipi için Networked Field/Property'leri saklar
        private static readonly Dictionary<Type, NetworkedMemberInfo[]> _networkedCache
            = new Dictionary<Type, NetworkedMemberInfo[]>();

        /// <summary>
        /// Verilen tip için attribute tarama işlemini ilk kez çalıştırır ve cache'e yazar.
        /// Sonraki çağrılarda yalnızca cache okur.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void WarmupAll()
        {
            // Unity domain'inde tüm tipleri önceden tara (Optional: yalnızca NetworkBehaviour subclass'ları)
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(type) && !type.IsAbstract)
                        {
                            _ = GetRpcMethods(type);
                            _ = GetNetworkedMembers(type);
                        }
                    }
                }
                catch (ReflectionTypeLoadException) { /* Ignore system assemblies */ }
            }
        }

        /// <summary>
        /// Tipin tüm [Rpc] metodlarını döndürür (cache'den okur veya yapılandırır).
        /// </summary>
        public static RpcMethodInfo[] GetRpcMethods(Type type)
        {
            if (_rpcCache.TryGetValue(type, out var cached))
                return cached;

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var result = new List<RpcMethodInfo>();

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<RpcAttribute>();
                if (attr != null)
                {
                    result.Add(new RpcMethodInfo
                    {
                        Method = method,
                        Attribute = attr,
                        // RPC ID: Method adından deterministik bir hash üret
                        RpcId = (ushort)(method.Name.GetHashCode() & 0xFFFF)
                    });
                }
            }

            var arr = result.ToArray();
            _rpcCache[type] = arr;
            return arr;
        }

        /// <summary>
        /// Tipin tüm [Networked] field ve property'lerini döndürür.
        /// </summary>
        public static NetworkedMemberInfo[] GetNetworkedMembers(Type type)
        {
            if (_networkedCache.TryGetValue(type, out var cached))
                return cached;

            var result = new List<NetworkedMemberInfo>();

            // Fields
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                var attr = field.GetCustomAttribute<NetworkedAttribute>();
                if (attr != null)
                {
                    result.Add(new NetworkedMemberInfo
                    {
                        MemberInfo = field,
                        Attribute = attr,
                        MemberType = field.FieldType,
                        IsField = true
                    });
                }
            }

            // Properties
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var prop in props)
            {
                var attr = prop.GetCustomAttribute<NetworkedAttribute>();
                if (attr != null && prop.CanRead && prop.CanWrite)
                {
                    result.Add(new NetworkedMemberInfo
                    {
                        MemberInfo = prop,
                        Attribute = attr,
                        MemberType = prop.PropertyType,
                        IsField = false
                    });
                }
            }

            var arr = result.ToArray();
            _networkedCache[type] = arr;
            return arr;
        }
    }

    /// <summary>Cache'lenmiş RPC metot bilgisi.</summary>
    public struct RpcMethodInfo
    {
        public MethodInfo Method;
        public RpcAttribute Attribute;
        public ushort RpcId;  // Ağ üzerinden taşınan compact kimlik
    }

    /// <summary>Cache'lenmiş Networked member bilgisi.</summary>
    public struct NetworkedMemberInfo
    {
        public MemberInfo MemberInfo;
        public NetworkedAttribute Attribute;
        public Type MemberType;
        public bool IsField;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object GetValue(object target) =>
            IsField
                ? ((FieldInfo)MemberInfo).GetValue(target)
                : ((PropertyInfo)MemberInfo).GetValue(target);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetValue(object target, object value)
        {
            if (IsField) ((FieldInfo)MemberInfo).SetValue(target, value);
            else ((PropertyInfo)MemberInfo).SetValue(target, value);
        }
    }
}
