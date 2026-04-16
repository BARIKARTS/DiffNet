using System;
using UnityEngine;

namespace DifferentGames.Multiplayer.Core
{
    /// <summary>
    /// Zero-allocation Ring Buffer for storing and querying unmanaged network inputs.
    /// Client uses this to predict inputs, and the Server uses this to execute received inputs.
    /// Max capacity per input state is fixed at 64 bytes.
    /// </summary>
    public unsafe class NetworkInputBuffer
    {
        public const int MaxInputSizeBytes = 64;
        
        private readonly byte[] _buffer;
        private readonly int[] _tickMapping;
        private readonly int _capacity;

        public NetworkInputBuffer(int capacity = 128)
        {
            _capacity = capacity;
            _buffer = new byte[_capacity * MaxInputSizeBytes];
            _tickMapping = new int[_capacity];
            
            // Initialize with invalid ticks
            for (int i = 0; i < _capacity; i++)
                _tickMapping[i] = -1;
        }

        /// <summary>
        /// Stores the provided input at the specified Tick index.
        /// </summary>
        public void SetInput<T>(NetworkTick tick, in T input) where T : unmanaged, INetworkInput
        {
            if (!tick.IsValid) return;

            int size = sizeof(T);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (size > MaxInputSizeBytes)
            {
                Debug.LogError($"[InputBuffer] Input struct '{typeof(T).Name}' exceeds {MaxInputSizeBytes} bytes! Prediction failed.");
                return;
            }
#endif

            int index = tick.Value % _capacity;
            int offset = index * MaxInputSizeBytes;

            fixed (T* srcPtr = &input)
            fixed (byte* dstPtr = &_buffer[offset])
            {
                Buffer.MemoryCopy(srcPtr, dstPtr, MaxInputSizeBytes, size);
            }

            _tickMapping[index] = tick.Value;
        }

        /// <summary>
        /// Tries to fetch the input for a specific Tick. Returns false if not found.
        /// </summary>
        public bool TryGetInput<T>(NetworkTick tick, out T input) where T : unmanaged, INetworkInput
        {
            input = default;
            if (!tick.IsValid) return false;

            int index = tick.Value % _capacity;

            // Reject if the tick mapped to this index doesn't match the requested tick
            // (e.g. RingBuffer wrapped around or input was never received)
            if (_tickMapping[index] != tick.Value)
            {
                return false;
            }

            int size = sizeof(T);
            int offset = index * MaxInputSizeBytes;

            fixed (byte* srcPtr = &_buffer[offset])
            fixed (T* dstPtr = &input)
            {
                Buffer.MemoryCopy(srcPtr, dstPtr, size, size);
            }

            return true;
        }
    }
}
