using System;
using System.Runtime.CompilerServices;

namespace DifferentGames.Multiplayer.Serialization
{
    /// <summary>
    /// Zero-allocation, Unmanaged 256-bit BitMask used for Delta Compression.
    /// Can mark up to 256 [Networked] variables.
    /// Size is fixed (32 bytes), but network serialization is dynamic depending on NetworkConfig.MaxNetworkedVariables.
    /// </summary>
    public unsafe struct BitMask
    {
        public fixed ulong Data[4]; // 4 * 64 = 256 bits

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBit(int index)
        {
            if (index >= 256) return;
            int wordIndex = index >> 6; // index / 64
            int bitIndex = index & 63;  // index % 64
            fixed (ulong* ptr = Data)
            {
                ptr[wordIndex] |= (1ul << bitIndex);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetBit(int index)
        {
            if (index >= 256) return false;
            int wordIndex = index >> 6;
            int bitIndex = index & 63;
            fixed (ulong* ptr = Data)
            {
                return (ptr[wordIndex] & (1ul << bitIndex)) != 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            fixed (ulong* ptr = Data)
            {
                ptr[0] = 0;
                ptr[1] = 0;
                ptr[2] = 0;
                ptr[3] = 0;
            }
        }

        /// <summary>
        /// Serializes the BitMask onto the network buffer dynamically up to the Contract limit.
        /// </summary>
        public void WriteTo(ref NetworkWriter writer, int maxVariables)
        {
            int ulongCount = maxVariables >> 6;
            if (ulongCount == 0) ulongCount = 1; // Minimal 64 vars

            fixed (ulong* ptr = Data)
            {
                for (int i = 0; i < ulongCount; i++)
                {
                    writer.WriteLong((long)ptr[i]);
                }
            }
        }

        /// <summary>
        /// Deserializes the BitMask from the network buffer.
        /// </summary>
        public void ReadFrom(ref NetworkReader reader, int maxVariables)
        {
            int ulongCount = maxVariables >> 6;
            if (ulongCount == 0) ulongCount = 1; // Minimal 64 vars

            fixed (ulong* ptr = Data)
            {
                for (int i = 0; i < ulongCount; i++)
                {
                    ptr[i] = (ulong)reader.ReadLong();
                }
            }
        }

        /// <summary>
        /// Quick equality check for unchanged state. 
        /// Returns true if all relevant configuration bits are empty.
        /// </summary>
        public bool IsEmpty(int maxVariables)
        {
            int ulongCount = maxVariables >> 6;
            if (ulongCount == 0) ulongCount = 1;

            fixed (ulong* ptr = Data)
            {
                for (int i = 0; i < ulongCount; i++)
                {
                    if (ptr[i] != 0) return false;
                }
            }
            return true;
        }
    }
}
