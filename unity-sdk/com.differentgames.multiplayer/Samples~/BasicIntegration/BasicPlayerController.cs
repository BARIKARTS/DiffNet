using UnityEngine;
using DifferentGames.Multiplayer.Components;

namespace DifferentGames.Multiplayer.Samples
{
    /// <summary>
    /// Sample predictive player controller utilizing DiffNet's zero-allocation rollback system.
    /// Requires NetworkTransform for smooth visual lerping.
    /// </summary>
    [RequireComponent(typeof(NetworkTransform))]
    public class BasicPlayerController : NetworkBehaviour
    {
        public float Speed = 5f;

        public override void FixedUpdateNetwork()
        {
            // Fully predictive! Server executes this same code to find the authoritative state!
            if (GetInput<BasicPlayerInput>(out var input))
            {
                // Network TickRate is typically 60. Fixed step time:
                float deltaTime = 1f / 60f; 
                
                Vector3 moveDelta = new Vector3(input.Movement.x, 0, input.Movement.y) * Speed * deltaTime;
                transform.position += moveDelta;

                if (input.IsJumping)
                {
                    // Basic jump simulation logic
                    transform.position += Vector3.up * (Speed * deltaTime);
                }
            }
        }
    }
}
