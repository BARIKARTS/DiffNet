using UnityEngine;
using DifferentGames.Multiplayer.Core;

namespace DifferentGames.Multiplayer.Samples
{
    /// <summary>
    /// Sample input structure limiting data to essentials.
    /// Zero-Allocation guarantee natively copies this via memory streams.
    /// </summary>
    public struct BasicPlayerInput : INetworkInput
    {
        public Vector2 Movement;
        public bool IsJumping;
    }
}
