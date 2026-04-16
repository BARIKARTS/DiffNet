using UnityEngine;

namespace DifferentGames.Multiplayer.Components
{
    /// <summary>
    /// Identity component that must be present on every network object (Prefab).
    /// Holds the ObjectId and ownership info that makes the object unique on the network.
    /// NetworkBehaviour components require this object.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkObject : MonoBehaviour
    {
        [SerializeField, HideInInspector]
        private uint _prefabId; // Immutable identity assigned to prefab in Editor

        // ── Grid Interest Management (Zero-Allocation Linked List) ──
        public Vector2Int CurrentGridCell { get; internal set; }
        public NetworkObject PrevInGrid { get; internal set; }
        public NetworkObject NextInGrid { get; internal set; }
        public NetworkScoping Scoping { get; private set; }
        // ────────────────────────────────────────────────────────────

        public NetworkObjectId ObjectId { get; internal set; } = NetworkObjectId.Invalid;
        public NetworkPlayerRef InputAuthority { get; internal set; } = NetworkPlayerRef.None;
        public NetworkRunner Runner { get; internal set; }

        /// <summary>
        /// The tick in which this object was most recently updated by the server.
        /// Used strictly by the Client to apply Interest Management Object Pooling (Disable/Enable).
        /// </summary>
        public NetworkTick LastReceivedSnapshotTick { get; internal set; } = NetworkTick.Invalid;

        /// <summary>The prefab reference identity of this object (used for spawning).</summary>
        public uint PrefabId => _prefabId;

        /// <summary>Caches all NetworkBehaviour components.</summary>
        internal NetworkBehaviour[] Behaviours { get; private set; }

        private void Awake()
        {
            Behaviours = GetComponents<NetworkBehaviour>();
            Scoping = GetComponent<NetworkScoping>();
        }

        /// <summary>Called when the Runner spawns this object for the first time.</summary>
        internal void NetworkInitialize(NetworkRunner runner, NetworkObjectId id, NetworkPlayerRef owner)
        {
            Runner = runner;
            ObjectId = id;
            InputAuthority = owner;

            foreach (var nb in Behaviours)
            {
                nb.Runner = runner;
                nb.InitializeStateHistory(runner.Config);
            }
        }
    }
}
