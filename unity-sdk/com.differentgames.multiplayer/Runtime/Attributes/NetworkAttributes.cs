using System;

namespace DifferentGames.Multiplayer.Attributes
{
    /// <summary>
    /// Determines the targets an RPC will be sent to.
    /// </summary>
    public enum RpcTargets
    {
        /// <summary>Runs only on the client that is the owner of this object.</summary>
        Owner,

        /// <summary>Runs only on the server side.</summary>
        Server,

        /// <summary>Runs on all connected clients (including the server).</summary>
        All,

        /// <summary>Runs on all clients except the owner.</summary>
        Proxy
    }

    /// <summary>
    /// This attribute makes a field or property synchronized over the network.
    /// Marked values are sent to the server as Snapshot Deltas at each Tick.
    /// <para>Usage: <c>[Networked] public float Health { get; set; }</c></para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class NetworkedAttribute : Attribute
    {
        /// <summary>
        /// Should the onInterpolate callback be triggered before this variable is updated?
        /// Used for data that needs to be smoothed, such as position.
        /// </summary>
        public bool Interpolate { get; set; } = false;

        /// <summary>
        /// Should this variable only synchronize from server to client (Server Authoritative)?
        /// If true, client changes are ignored.
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
    /// This attribute turns a method into a Remote Procedure Call (RPC).
    /// The marked method can be called over the network to specific targets.
    /// <para>Usage: <c>[Rpc(RpcTargets.All)] public void OnPlayerDied() { }</c></para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class RpcAttribute : Attribute
    {
        /// <summary>The target(s) this RPC will be sent to.</summary>
        public RpcTargets Targets { get; }

        /// <summary>
        /// Send the RPC packet as Reliable or Unreliable?
        /// Reliable is suitable for critical events (death, damage), Unreliable for position snaps.
        /// </summary>
        public bool Reliable { get; set; } = true;

        /// <summary>
        /// Channel Id: For RPCs of the same type to follow each other.
        /// Different channels do not block each other (prevents Head-of-Line Blocking).
        /// </summary>
        public byte Channel { get; set; } = 0;

        public RpcAttribute(RpcTargets targets = RpcTargets.Server)
        {
            Targets = targets;
        }
    }

    /// <summary>
    /// This attribute allows a parameter to be automatically filled with the information
    /// of the caller (Caller) in an RPC call. The user does not pass this parameter explicitly.
    /// <para>Usage: <c>[Rpc(RpcTargets.Server)] public void Shoot([RpcCaller] PlayerRef caller) { }</c></para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public sealed class RpcCallerAttribute : Attribute { }
}
