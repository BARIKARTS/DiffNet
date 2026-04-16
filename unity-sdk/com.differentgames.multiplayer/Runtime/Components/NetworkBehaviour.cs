using System;
using System.Runtime.CompilerServices;
using DifferentGames.Multiplayer.Serialization;
using UnityEngine;

namespace DifferentGames.Multiplayer.Components
{
    /// <summary>
    /// Base class for all components to be synchronized over the network.
    /// Derives from MonoBehaviour; handles [Networked] and [Rpc] attributes,
    /// provides the deterministic FixedUpdateNetwork loop, and supports Manual data transmission.
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

        /// <summary>Reference to the NetworkRunner connected to this object.</summary>
        public NetworkRunner Runner { get; internal set; }

        // ── Internal References ───────────────────────────────────────────────────
        private NetworkObject _networkObject;
        private Core.RpcMethodInfo[] _cachedRpcMethods;
        private Core.NetworkedMemberInfo[] _cachedNetworkedMembers;

        // Delta Compression State 
        private Core.StateHistoryBuffer _stateHistory;
        public NetworkTick LastAckedTick { get; set; } = new NetworkTick(0);

        internal void InitializeStateHistory(Core.NetworkConfig config)
        {
            int[] sizes = new int[_cachedNetworkedMembers.Length];
            for (int i = 0; i < sizes.Length; i++)
            {
                sizes[i] = GetNetworkedValueSize(_cachedNetworkedMembers[i].MemberType);
            }
            _stateHistory = new Core.StateHistoryBuffer(config.StateHistorySize, sizes);
        }

        // Event for manual data reception
        public event Action<NetworkPlayerRef, ArraySegment<byte>> OnManualDataReceived;

        // ── Unity Lifecycle ───────────────────────────────────────────────────

        protected virtual void Awake()
        {
            _networkObject = GetComponent<NetworkObject>();
            // Prepare attribute cache (O(1) if Reflection WarmUp was already done)
            _cachedRpcMethods = Core.NetworkAttributeCache.GetRpcMethods(GetType());
            _cachedNetworkedMembers = Core.NetworkAttributeCache.GetNetworkedMembers(GetType());
        }

        // ── INetworkSimulation ────────────────────────────────────────────────

        /// <summary>
        /// Retrieves the requested input struct from the Runner's InputBuffer for the CurrentTick.
        /// Returns false if no input is available for this tick.
        /// </summary>
        public bool GetInput<T>(out T input) where T : unmanaged, Core.INetworkInput
        {
            if (Runner != null && Runner.InputBuffer != null)
            {
                return Runner.InputBuffer.TryGetInput(CurrentTick, out input);
            }
            input = default;
            return false;
        }

        /// <summary>
        /// Deterministic update loop synchronized with the Server Tick.
        /// Write all network logic here (Reading Input, changing state).
        /// DO NOT CONFUSE with Unity Update(). Called by NetworkRunner.
        /// </summary>
        public virtual void FixedUpdateNetwork() { }

        /// <summary>
        /// Called for visual updates (Interpolation is done here).
        /// Network state should not be modified.
        /// </summary>
        public virtual void Render() { }

        // ── RPC Dispatch ──────────────────────────────────────────────────────

        /// <summary>
        /// Decodes incoming raw byte data as an RPC and calls the corresponding method.
        /// Called by NetworkRunner; not to be called directly by the user.
        /// </summary>
        internal unsafe void DispatchRpc(byte* data, int length, NetworkPlayerRef sender)
        {
            if (length < 2) return;

            var reader = new NetworkReader(data, length);
            ushort rpcId = reader.ReadUShort(); // First 2 bytes = RPC identity

            foreach (ref var rpcInfo in _cachedRpcMethods.AsSpan())
            {
                if (rpcInfo.RpcId != rpcId) continue;

                // Build parameter list
                var parameters = rpcInfo.Method.GetParameters();
                var args = new object[parameters.Length];

                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;

                    // Automatically fill [RpcCaller] parameter
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
        /// Sends an RPC over the network. Serializes based on the developer's method signature.
        /// Methods with [Rpc] attribute normally call this method periodically via generated code or direct logic.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected unsafe void SendRpc(ushort rpcId, Attributes.RpcTargets targets,
            bool reliable, Action<NetworkWriter> writePayload)
        {
            if (Runner == null) return;

            // Create buffer on stack (no GC!)
            Span<byte> buffer = stackalloc byte[512];
            var writer = new NetworkWriter(buffer);
            writer.WriteUShort(rpcId);     // Header: RPC ID
            writer.WriteInt(ObjectId.Value == 0 ? 0 : (int)ObjectId.Value); // Header: Which object?
            writePayload(writer);           // Payload: User data

            var data = writer.ToSpan();
            var mode = reliable ? Core.DeliveryMode.ReliableOrdered : Core.DeliveryMode.Unreliable;

            Runner.SendRawToTargets(InputAuthority, targets, data, mode);
        }

        // ── Manual Data Transmission ──────────────────────────────────────────────

        /// <summary>
        /// Sends raw byte data. For advanced users.
        /// <para>Example: <c>SendManual(data, DeliveryMode.ReliableOrdered)</c></para>
        /// </summary>
        public void SendManual(ReadOnlySpan<byte> data, Core.DeliveryMode mode = Core.DeliveryMode.Unreliable)
        {
            Runner?.SendRaw(InputAuthority, data, mode);
        }

        /// <summary>
        /// Triggers incoming manual data (via DispatchManual).
        /// Called by the system.
        /// </summary>
        internal unsafe void DispatchManual(byte* data, int length, NetworkPlayerRef sender)
        {
            if (OnManualDataReceived == null) return;
            // byte* → managed ArraySegment (one mandatory copy - for managed event)
            var managed = new byte[length];
            fixed (byte* dst = managed)
                Buffer.MemoryCopy(data, dst, length, length);
            OnManualDataReceived?.Invoke(sender, new ArraySegment<byte>(managed));
        }

        /// <summary>
        /// Saves current networked values into the Ring Buffer for the CurrentTick. 
        /// Performed exactly ONCE per Tick. Server and Client (Predictive) both record.
        /// </summary>
        internal unsafe void RecordCurrentState()
        {
            for (int i = 0; i < _cachedNetworkedMembers.Length; i++)
            {
                var member = _cachedNetworkedMembers[i];
                if (member.Attribute.ServerOnly && !HasStateAuthority) continue;

                object value = member.GetValue(this);
                Span<byte> historyData = _stateHistory?.GetVariableData(CurrentTick, i) ?? Span<byte>.Empty;
                
                if (!historyData.IsEmpty)
                {
                    var tempWriter = new NetworkWriter(historyData);
                    WriteNetworkedValue(ref tempWriter, value, member.MemberType);
                }
            }
        }

        /// <summary>
        /// Calculates the bitmask difference between CurrentTick and the target baselineTick, 
        /// and writes the compressed delta to the network.
        /// </summary>
        internal unsafe void SerializeDeltaState(ref NetworkWriter writer, NetworkTick baselineTick)
        {
            int maxVars = Runner.Config.MaxNetworkedVariables;
            BitMask mask = new BitMask();

            // First Pass: Determine changes and build BitMask
            for (int i = 0; i < _cachedNetworkedMembers.Length; i++)
            {
                var member = _cachedNetworkedMembers[i];
                if (member.Attribute.ServerOnly && !HasStateAuthority) continue;

                Span<byte> currentVarData = _stateHistory?.GetVariableData(CurrentTick, i) ?? Span<byte>.Empty;
                Span<byte> baselineData = _stateHistory?.GetVariableData(baselineTick, i) ?? Span<byte>.Empty;

                if (baselineData.IsEmpty || !baselineData.SequenceEqual(currentVarData))
                {
                    mask.SetBit(i);
                }
            }

            // Second Pass: Write Mask
            mask.WriteTo(ref writer, maxVars);

            // Third Pass: Write Only Changed Variables
            for (int i = 0; i < _cachedNetworkedMembers.Length; i++)
            {
                var member = _cachedNetworkedMembers[i];
                if (member.Attribute.ServerOnly && !HasStateAuthority) continue;

                if (mask.GetBit(i))
                {
                    Span<byte> currentVarData = _stateHistory?.GetVariableData(CurrentTick, i) ?? Span<byte>.Empty;
                    writer.WriteBytes(currentVarData);
                }
            }
        }

        /// <summary>
        /// Applies the received authoritative bitmask and data onto the targetTick in the history buffer.
        /// Compares the generated byte sequence with the predicted history to detect prediction failures.
        /// Returns true if prediction failed and resimulation is required.
        /// </summary>
        internal unsafe bool DeserializeDeltaState(ref NetworkReader reader, NetworkTick baselineTick, NetworkTick targetTick)
        {
            int maxVars = Runner.Config.MaxNetworkedVariables;
            BitMask mask = new BitMask();
            mask.ReadFrom(ref reader, maxVars);
            bool predictionFailed = false;

            for (int i = 0; i < _cachedNetworkedMembers.Length; i++)
            {
                var member = _cachedNetworkedMembers[i];
                if (member.Attribute.ServerOnly && HasStateAuthority) continue;

                var size = GetNetworkedValueSize(member.MemberType);
                Span<byte> targetHistoryData = _stateHistory?.GetVariableData(targetTick, i) ?? Span<byte>.Empty;

                if (mask.GetBit(i))
                {
                    // Delta says changed compared to baselineTick. Read authoritative value!
                    object value = ReadNetworkedValue(ref reader, member.MemberType);

                    if (!targetHistoryData.IsEmpty)
                    {
                        var tempWriter = new NetworkWriter(stackalloc byte[size]);
                        WriteNetworkedValue(ref tempWriter, value, member.MemberType);
                        var serializedBytes = tempWriter.ToSpan();

                        if (!targetHistoryData.SequenceEqual(serializedBytes))
                        {
                            predictionFailed = true;
                            // Overwrite our incorrect predicted history with the authoritative one over the network
                            serializedBytes.CopyTo(targetHistoryData);
                        }
                    }
                    else
                    {
                        // No history initialized somehow, force snap
                        predictionFailed = true;
                        member.SetValue(this, value);
                    }
                }
                else
                {
                    // Delta says EXACTLY unchanged compared to the baselineTick.
                    Span<byte> prevData = _stateHistory?.GetVariableData(baselineTick, i) ?? Span<byte>.Empty;
                    if (!prevData.IsEmpty && !targetHistoryData.IsEmpty)
                    {
                        if (!targetHistoryData.SequenceEqual(prevData))
                        {
                            predictionFailed = true; // We predicted it would change, but authoritative said it didn't!
                            prevData.CopyTo(targetHistoryData); // Rewind
                        }
                    }
                }
            }

            return predictionFailed;
        }

        /// <summary>
        /// Snaps the object's networked C# properties (the visible state) exactly to a past Tick's history state.
        /// Used by the NetworkRunner strictly before starting Resimulation (Rollback).
        /// </summary>
        internal unsafe void SnapToTick(NetworkTick targetTick)
        {
            for (int i = 0; i < _cachedNetworkedMembers.Length; i++)
            {
                var member = _cachedNetworkedMembers[i];
                if (member.Attribute.ServerOnly && !HasStateAuthority) continue;

                Span<byte> historyData = _stateHistory?.GetVariableData(targetTick, i) ?? Span<byte>.Empty;
                if (!historyData.IsEmpty)
                {
                    var reader = new Serialization.NetworkReader(historyData);
                    object value = ReadNetworkedValue(ref reader, member.MemberType);
                    member.SetValue(this, value);
                }
            }
        }

        // ── Helper: Read Parameters ──────────────────────────────────────

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
            if (type == typeof(NetworkObjectId))  return new NetworkObjectId((uint)reader.ReadInt());
            return null;
        }

        private static int GetNetworkedValueSize(Type type)
        {
            if (type == typeof(int) || type == typeof(float) || type == typeof(uint) || type == typeof(NetworkPlayerRef) || type == typeof(NetworkObjectId)) return 4;
            if (type == typeof(bool) || type == typeof(byte)) return 1;
            if (type == typeof(short) || type == typeof(ushort)) return 2;
            if (type == typeof(long) || type == typeof(double) || type == typeof(Vector2)) return 8;
            if (type == typeof(Vector3)) return 12;
            if (type == typeof(Quaternion)) return 4; // Because compressed in NetworkWriter
            return 4; // default
        }
    }
}
