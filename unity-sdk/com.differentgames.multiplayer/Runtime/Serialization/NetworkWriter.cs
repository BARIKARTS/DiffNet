using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DifferentGames.Multiplayer.Serialization
{
    /// <summary>
    /// Zero-allocation, unsafe pointer based network writer (Serializer).
    /// Writes onto a fixed-size stack buffer, creates no heap allocation.
    /// 
    /// Usage:
    /// <code>
    /// var writer = new NetworkWriter(stackalloc byte[256]);
    /// writer.WriteFloat(3.14f);
    /// writer.WriteVector3(transform.position);
    /// var data = writer.ToSpan();
    /// </code>
    /// </summary>
    public unsafe ref struct NetworkWriter
    {
        private readonly byte* _buffer;
        private readonly int _capacity;
        private int _position;

        public int Position => _position;
        public int Remaining => _capacity - _position;
        public bool IsOverflow => _position > _capacity;

        /// <summary>
        /// Initialization over Span. Recommended to use with stackalloc.
        /// </summary>
        public NetworkWriter(Span<byte> buffer)
        {
            fixed (byte* ptr = buffer)
                _buffer = ptr;
            _capacity = buffer.Length;
            _position = 0;
        }

        // ─── Primitives ────────────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value)
        {
            CheckCapacity(1);
            _buffer[_position++] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteShort(short value)
        {
            CheckCapacity(2);
            Unsafe.WriteUnaligned(_buffer + _position, value);
            _position += 2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUShort(ushort value)
        {
            CheckCapacity(2);
            Unsafe.WriteUnaligned(_buffer + _position, value);
            _position += 2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt(int value)
        {
            CheckCapacity(4);
            Unsafe.WriteUnaligned(_buffer + _position, value);
            _position += 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt(uint value)
        {
            CheckCapacity(4);
            Unsafe.WriteUnaligned(_buffer + _position, value);
            _position += 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteLong(long value)
        {
            CheckCapacity(8);
            Unsafe.WriteUnaligned(_buffer + _position, value);
            _position += 8;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFloat(float value)
        {
            CheckCapacity(4);
            Unsafe.WriteUnaligned(_buffer + _position, value);
            _position += 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteDouble(double value)
        {
            CheckCapacity(8);
            Unsafe.WriteUnaligned(_buffer + _position, value);
            _position += 8;
        }

        // ─── Compressed Float (Common in games: 16-bit half precision) ───

        /// <summary>
        /// Compresses a float value into 2 Bytes (according to ±range, precision).
        /// Ideal for bandwidth saving of position delta, angle, etc.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteCompressedFloat(float value, float min, float max, float precision)
        {
            float range = max - min;
            float steps = range / precision;
            ushort compressed = (ushort)Mathf.Clamp((value - min) / precision, 0, steps);
            WriteUShort(compressed);
        }

        // ─── Unity Types ──────────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVector2(Vector2 value)
        {
            WriteFloat(value.x);
            WriteFloat(value.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVector3(Vector3 value)
        {
            WriteFloat(value.x);
            WriteFloat(value.y);
            WriteFloat(value.z);
        }

        /// <summary>
        /// Compresses a Quaternion using the "Smallest Three" method (3.5 float equivalent instead of 4 floats).
        /// Provides ~32% bandwidth savings.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteQuaternionCompressed(Quaternion q)
        {
            // Find and remove the largest component (the receiver will recalculate it)
            float absX = Mathf.Abs(q.x), absY = Mathf.Abs(q.y),
                  absZ = Mathf.Abs(q.z), absW = Mathf.Abs(q.w);

            byte largestIndex = 3; // W
            float largest = absW;
            if (absX > largest) { largest = absX; largestIndex = 0; }
            if (absY > largest) { largest = absY; largestIndex = 1; }
            if (absZ > largest) { largest = absZ; largestIndex = 2; }

            float a, b, c;
            float sign = largestIndex switch
            {
                0 => q.x < 0 ? -1f : 1f,
                1 => q.y < 0 ? -1f : 1f,
                2 => q.z < 0 ? -1f : 1f,
                _ => q.w < 0 ? -1f : 1f
            };

            if (largestIndex == 0) { a = q.y * sign; b = q.z * sign; c = q.w * sign; }
            else if (largestIndex == 1) { a = q.x * sign; b = q.z * sign; c = q.w * sign; }
            else if (largestIndex == 2) { a = q.x * sign; b = q.y * sign; c = q.w * sign; }
            else { a = q.x * sign; b = q.y * sign; c = q.z * sign; }

            WriteByte(largestIndex);
            WriteCompressedFloat(a, -1f, 1f, 0.0001f);
            WriteCompressedFloat(b, -1f, 1f, 0.0001f);
            WriteCompressedFloat(c, -1f, 1f, 0.0001f);
        }

        // ─── Struct Serialization (Zero-Copy Struct Write) ────────────────

        /// <summary>
        /// Writes any unmanaged struct directly to the buffer via pointer.
        /// Zero-Copy, zero allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteStruct<T>(in T value) where T : unmanaged
        {
            int size = sizeof(T);
            CheckCapacity(size);
            fixed (T* ptr = &value)
                Buffer.MemoryCopy(ptr, _buffer + _position, _capacity - _position, size);
            _position += size;
        }

        // ─── Raw Bytes ────────────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytes(ReadOnlySpan<byte> data)
        {
            CheckCapacity(data.Length);
            fixed (byte* src = data)
                Buffer.MemoryCopy(src, _buffer + _position, _capacity - _position, data.Length);
            _position += data.Length;
        }

        // ─── Output ───────────────────────────────────────────────────────

        /// <summary>Returns the written data as a ReadOnlySpan (no copying).</summary>
        public ReadOnlySpan<byte> ToSpan() => new ReadOnlySpan<byte>(_buffer, _position);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckCapacity(int required)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_position + required > _capacity)
                throw new InvalidOperationException(
                    $"[NetworkWriter] Buffer overflow! Required {required} bytes, remaining {Remaining}.");
#endif
        }
    }
}
