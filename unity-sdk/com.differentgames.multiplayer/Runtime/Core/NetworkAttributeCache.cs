using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DifferentGames.Multiplayer.Attributes;
using UnityEngine;

namespace DifferentGames.Multiplayer.Core
{
    /// <summary>
    /// Fills the attribute cache during initialization using Reflection once.
    /// Ensures O(1) lookup in subsequent frames. Does not create GC pressure.
    /// </summary>
    public static class NetworkAttributeCache
    {
        // Per-Type cache: Stores RPC methods for each MonoBehaviour type
        private static readonly Dictionary<Type, RpcMethodInfo[]> _rpcCache
            = new Dictionary<Type, RpcMethodInfo[]>();

        // Per-Type cache: Stores Networked Fields/Properties for each MonoBehaviour type
        private static readonly Dictionary<Type, NetworkedMemberInfo[]> _networkedCache
            = new Dictionary<Type, NetworkedMemberInfo[]>();

        /// <summary>
        /// Runs the attribute scanning process for the given type for the first time and writes to the cache.
        /// Subsequent calls read only from the cache.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void WarmupAll()
        {
            // Pre-scan all types in the Unity domain (Optional: only NetworkBehaviour subclasses)
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
        /// Returns all [Rpc] methods of the type (reads from cache or configures).
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
                        // RPC ID: Generate a deterministic hash from the Method name
                        RpcId = (ushort)(method.Name.GetHashCode() & 0xFFFF)
                    });
                }
            }

            var arr = result.ToArray();
            _rpcCache[type] = arr;
            return arr;
        }

        /// <summary>
        /// Returns all [Networked] fields and properties of the type.
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

    /// <summary>Cached RPC method information.</summary>
    public struct RpcMethodInfo
    {
        public MethodInfo Method;
        public RpcAttribute Attribute;
        public ushort RpcId;  // Compact identity carried over the network
    }

    /// <summary>Cached Networked member information.</summary>
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
