using System.Collections.Generic;
using UnityEngine;
using DifferentGames.Multiplayer.Core;
using DifferentGames.Multiplayer.Components;
using DifferentGames.Multiplayer.Integration;

namespace DifferentGames.Multiplayer.Samples
{
    /// <summary>
    /// Concrete DiffNet implementation ready to be placed inside a Unity Scene.
    /// Demonstrates: AOI-driven Spawn/Despawn callbacks, nametag pooling, and input injection.
    /// </summary>
    public class DiffNetStarter : DiffNetManagerBase
    {
        // ── Nametag (Label) Pooling ───────────────────────────────────────────
        // Associates each spawned NetworkObject with a simple world-space label.
        // This is a typical use-case: show a player's name above their head when
        // they enter your AOI, hide it when they leave — zero GC overhead.

        private readonly Dictionary<NetworkObjectId, GameObject> _nametags = new();

        [Header("Nametag Settings")]
        [Tooltip("Optional prefab with a TextMesh/TMP component to show above remote players.")]
        public GameObject NametagPrefab;

        // ── OnGUI ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (Runner == null || Runner.IsRunning) return;

            GUILayout.BeginArea(new Rect(10, 10, 220, 300));
            GUI.Box(new Rect(0, 0, 220, 110), "DiffNet Starter");

            if (GUILayout.Button("▶  Start Server", GUILayout.Height(40))) StartServer();
            if (GUILayout.Button("⬡  Start Client", GUILayout.Height(40))) StartClient();

            GUILayout.EndArea();
        }

        // ── Input Injection ───────────────────────────────────────────────────

        public override void OnProvideInput(NetworkRunner runner, NetworkInputProvider input)
        {
            // Gather Unity inputs and forward them to DiffNet's prediction engine.
            var myInput = new BasicPlayerInput
            {
                Movement  = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical")),
                IsJumping = Input.GetKeyDown(KeyCode.Space)
            };

            input.Set(myInput);
        }

        // ── AOI Lifecycle Callbacks ───────────────────────────────────────────

        /// <summary>
        /// Called on the CLIENT when a new NetworkObject enters its Area of Interest
        /// (or at initial connect for Global/OwnerOnly objects).
        ///
        /// Use-cases: Show nametag, play spawn VFX, register minimap icon, etc.
        /// </summary>
        public override void OnObjectSpawned(NetworkObject netObj)
        {
            // Do not create nametags for objects we own (self)
            if (netObj.InputAuthority == Runner.LocalPlayer) return;
            if (NametagPrefab == null) return;

            // Position the nametag 2 units above the object's pivot
            Vector3 tagPos = netObj.transform.position + Vector3.up * 2f;
            var tag = Instantiate(NametagPrefab, tagPos, Quaternion.identity, netObj.transform);

            // Set the label text if a TextMesh is present
            var textMesh = tag.GetComponentInChildren<TextMesh>();
            if (textMesh != null)
                textMesh.text = $"Player {netObj.InputAuthority.Id}";

            _nametags[netObj.ObjectId] = tag;

            Debug.Log($"[DiffNetStarter] Object {netObj.ObjectId} entered AOI — nametag created.");
        }

        /// <summary>
        /// Called on the CLIENT just before a NetworkObject is destroyed because it
        /// exited the local player's Area of Interest.
        ///
        /// Use-cases: Remove minimap icon, stop audio, clean up pooled UI elements.
        /// </summary>
        public override void OnObjectDespawned(NetworkObject netObj)
        {
            if (_nametags.TryGetValue(netObj.ObjectId, out var tag))
            {
                Destroy(tag);
                _nametags.Remove(netObj.ObjectId);
            }

            Debug.Log($"[DiffNetStarter] Object {netObj.ObjectId} left AOI — nametag removed.");
        }
    }
}
