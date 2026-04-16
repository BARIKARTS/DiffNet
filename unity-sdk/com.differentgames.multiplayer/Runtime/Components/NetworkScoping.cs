using UnityEngine;

namespace DifferentGames.Multiplayer.Components
{
    public enum ScopingMode
    {
        Global,     // Synced to everyone, always
        Spatial,    // Synced only to players within Grid-based ViewDistance
        OwnerOnly,  // Synced only to the player who owns it (InputAuthority)
        Manual      // Ignored by default systems, handled by custom logic
    }

    /// <summary>
    /// Optional component attached to a NetworkObject to override the Global interest rules.
    /// Without this component, the object operates in 'Global' mode (Zero overhead).
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkScoping : MonoBehaviour
    {
        [Tooltip("How this object is distributed across the network.")]
        public ScopingMode Mode = ScopingMode.Global;
    }
}
