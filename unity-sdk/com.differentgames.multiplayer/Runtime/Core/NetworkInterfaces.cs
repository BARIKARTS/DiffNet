using System.Runtime.CompilerServices;
using UnityEngine;

namespace DifferentGames.Multiplayer.Core
{
    /// <summary>
    /// Tick-based simulation interface triggered by the NetworkRunner every frame.
    /// Provides a deterministic loop independent of Unity's Update().
    /// Developers write network logic here by implementing this interface.
    /// <para>Usage: Overridden within NetworkBehaviour.</para>
    /// </summary>
    public interface INetworkSimulation
    {
        /// <summary>
        /// Called according to the Tick number from the server.
        /// All input reading, state changes, and network logic should be written here.
        /// Similar to Unity's FixedUpdate but deterministic and network-synchronized.
        /// </summary>
        void FixedUpdateNetwork();

        /// <summary>
        /// Called in the render frame. Interpolation and visual updates are written here.
        /// State changes should not be performed.
        /// </summary>
        void Render();
    }

    /// <summary>
    /// Interface carrying ownership and state information of a network object.
    /// </summary>
    public interface INetworkState
    {
        /// <summary>Unique identity of this object on the network.</summary>
        NetworkObjectId ObjectId { get; }

        /// <summary>Who owns this object (Owner).</summary>
        NetworkPlayerRef InputAuthority { get; }

        /// <summary>Is this object controlled by AI/server?</summary>
        bool HasStateAuthority { get; }

        /// <summary>Does this object belong to the local client?</summary>
        bool HasInputAuthority { get; }

        /// <summary>The tick the object is connected to. 0 if not yet connected.</summary>
        NetworkTick CurrentTick { get; }
    }

    /// <summary>
    /// Callback interface for network events.
    /// Called by the NetworkRunner.
    /// </summary>
    public interface INetworkCallbacks
    {
        void OnPlayerJoined(NetworkPlayerRef player) { }
        void OnPlayerLeft(NetworkPlayerRef player) { }
        void OnConnectedToServer(NetworkPlayerRef localPlayer) { }
        void OnDisconnectedFromServer() { }
        void OnProvideInput(NetworkRunner runner, NetworkInputProvider input) { }
        void OnShutdown() { }

        /// <summary>
        /// Called on the client when a new NetworkObject is instantiated
        /// due to entering the local player's Area of Interest (or initial connect).
        /// </summary>
        void OnObjectSpawned(Components.NetworkObject netObj) { }

        /// <summary>
        /// Called on the client just before a NetworkObject is destroyed
        /// due to leaving the local player's Area of Interest.
        /// </summary>
        void OnObjectDespawned(Components.NetworkObject netObj) { }
    }

    /// <summary>
    /// RPC transmission info. Carries the sender and reliability information.
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
