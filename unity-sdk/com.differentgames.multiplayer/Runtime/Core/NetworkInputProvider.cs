using System;

namespace DifferentGames.Multiplayer.Core
{
    /// <summary>
    /// Passed to the player during the OnProvideInput callback to register their input.
    /// </summary>
    public readonly struct NetworkInputProvider
    {
        private readonly NetworkInputBuffer _buffer;
        private readonly NetworkTick _tick;

        public NetworkInputProvider(NetworkInputBuffer buffer, NetworkTick tick)
        {
            _buffer = buffer;
            _tick = tick;
        }

        public void Set<T>(in T input) where T : unmanaged, INetworkInput
        {
            _buffer.SetInput(_tick, input);
        }
    }
}
