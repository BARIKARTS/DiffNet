using System;
using UnityEngine;
using DifferentGames.Multiplayer.Core;

namespace DifferentGames.Multiplayer.Integration
{
    /// <summary>
    /// Abstract base class to help developers instantly launch a DiffNet server or client.
    /// Handles NetworkRunner lifecycle, callbacks, and automatic player spawning.
    /// </summary>
    [RequireComponent(typeof(NetworkRunner))]
    public abstract class DiffNetManagerBase : MonoBehaviour, INetworkCallbacks
    {
        public NetworkRunner Runner { get; private set; }

        [Header("Prefabs")]
        [Tooltip("The player prefab to spawn when a client connects. Must contain a NetworkObject.")]
        public GameObject PlayerPrefab;

        protected virtual void Awake()
        {
            Runner = GetComponent<NetworkRunner>();
        }

        public void StartServer() => Runner.StartServer();
        public void StartClient(string ip = "127.0.0.1", int port = 0) => Runner.StartClient(ip, port);

        // Core Callbacks
        public virtual void OnPlayerJoined(NetworkPlayerRef player)
        {
            if (Runner.IsServer && PlayerPrefab != null)
            {
                var spawnPos = GetSpawnPosition(player);
                var netObj = Runner.Spawn(PlayerPrefab, spawnPos, Quaternion.identity, player);
                
                // Set the anchor for Area of Interest calculations
                Runner.SetPlayerAnchor(player, netObj);
            }
        }

        public virtual void OnPlayerLeft(NetworkPlayerRef player) { }
        public virtual void OnConnectedToServer(NetworkPlayerRef localPlayer) { }
        public virtual void OnDisconnectedFromServer() { }
        public virtual void OnProvideInput(NetworkRunner runner, NetworkInputProvider input) { }
        public virtual void OnShutdown() { }

        /// <summary>
        /// Defines where the player should spawn. Can be overridden for custom logic.
        /// </summary>
        protected virtual Vector3 GetSpawnPosition(NetworkPlayerRef player)
        {
            // Simple offset to prevent overlapping
            return Vector3.zero + (Vector3.right * player.Id * 2f);
        }
    }
}
