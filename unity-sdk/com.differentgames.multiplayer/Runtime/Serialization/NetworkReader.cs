using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace DifferentGames.Multiplayer.Serialization
{
    /// <summary>
    /// Zero-allocation, unsafe pointer tabanlı ağ okuyucusu (Deserializer).
    /// Gelen ham byte verisi üzerinde kopyalama yapmadan okuma yapar.
    ///
    /// Kullanım:
    /// <code>
    /// var reader = new NetworkReader(rawData);
    /// float speed = reader.ReadFloat();
    /// Vector3 pos = reader.ReadVector3();
    /// </code>
    /// </summary>
    public unsafe ref struct NetworkReader
    {
        private readonly byte* _buffer;
        private readonly int _length;
        private int _position;

        public int Position => _position;
        public int Remaining => _length - _position;
        public bool EndOfData => _position >= _length;

        /// <summary>ReadOnlySpan üzerinden başlatma (Socket verisini kopyalamadan sarmalar).</summary>
        public NetworkReader(ReadOnlySpan<byte> data)
        {
            fixed (byte* ptr = data)
                _buffer = ptr;
            _length = data.Length;
            _position = 0;
        }

        /// <summary>Raw pointer ile başlatma (Socket callback'lerindeki byte* uyumluluğu).</summary>
        public NetworkReader(byte* buffer, int length)
        {
            _buffer = buffer;
            _length = length;
            _position = 0;
        }

        // ─── Primitives ───────────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            CheckBounds(1);
            return _buffer[_position++];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBool() => ReadByte() != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public short ReadShort()
        {
            CheckBounds(2);
            var v = Unsafe.ReadUnaligned<short>(_buffer + _position);
            _position += 2;
            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadUShort()
        {
            CheckBounds(2);
            var v = Unsafe.ReadUnaligned<ushort>(_buffer + _position);
            _position += 2;
            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt()
        {
            CheckBounds(4);
            var v = Unsafe.ReadUnaligned<int>(_buffer + _position);
            _position += 4;
            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt()
        {
            CheckBounds(4);
            var v = Unsafe.ReadUnaligned<uint>(_buffer + _position);
            _position += 4;
            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadLong()
        {
            CheckBounds(8);
            var v = Unsafe.ReadUnaligned<long>(_buffer + _position);
            _position += 8;
            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ReadFloat()
        {
            CheckBounds(4);
            var v = Unsafe.ReadUnaligned<float>(_buffer + _position);
            _position += 4;
            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ReadDouble()
        {
            CheckBounds(8);
            var v = Unsafe.ReadUnaligned<double>(_buffer + _position);
            _position += 8;
            return v;
        }

        // ─── Compressed Float ─────────────────────────────────────────────

        /// <summary>NetworkWriter.WriteCompressedFloat ile yazılmış değeri geri çözer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ReadCompressedFloat(float min, float max, float precision)
        {
            ushort compressed = ReadUShort();
            return min + compressed * precision;
        }

        // ─── Unity Types ──────────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2 ReadVector2() => new Vector2(ReadFloat(), ReadFloat());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 ReadVector3() => new Vector3(ReadFloat(), ReadFloat(), ReadFloat());

        /// <summary>NetworkWriter.WriteQuaternionCompressed ile yazılmış Quaternion'ı geri çözer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion ReadQuaternionCompressed()
        {
            byte largestIndex = ReadByte();
            float a = ReadCompressedFloat(-1f, 1f, 0.0001f);
            float b = ReadCompressedFloat(-1f, 1f, 0.0001f);
            float c = ReadCompressedFloat(-1f, 1f, 0.0001f);

            // En büyük bileşeni q.x^2 + q.y^2 + q.z^2 + q.w^2 = 1 formülüyle yeniden hesapla
            float largest = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a - b * b - c * c));

            return largestIndex switch
            {
                0 => new Quaternion(largest, a, b, c),
                1 => new Quaternion(a, largest, b, c),
                2 => new Quaternion(a, b, largest, c),
                _ => new Quaternion(a, b, c, largest)
            };
        }

        // ─── Struct Deserialization (Zero-Copy) ───────────────────────────

        /// <summary>
        /// Herhangi bir unmanaged struct'ı doğrudan pointer üzerinden okur.
        /// Kopyalama yok, allocation yok.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T ReadStruct<T>() where T : unmanaged
        {
            int size = sizeof(T);
            CheckBounds(size);
            T value = Unsafe.ReadUnaligned<T>(_buffer + _position);
            _position += size;
            return value;
        }

        // ─── Raw Bytes ────────────────────────────────────────────────────

        /// <summary>Belirtilen uzunluktaki veriyi kopyalamadan Span olarak döndürür.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            CheckBounds(count);
            var span = new ReadOnlySpan<byte>(_buffer + _position, count);
            _position += count;
            return span;
        }

        // ─── Seek ─────────────────────────────────────────────────────────

        /// <summary>Okuma pozisyonunu sıfırlar.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset() => _position = 0;

        /// <summary>Okuma pozisyonunu atlar (header'i skip etmek için).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Skip(int bytes)
        {
            CheckBounds(bytes);
            _position += bytes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckBounds(int required)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_position + required > _length)
                throw new InvalidOperationException(
                    $"[NetworkReader] Buffer underflow! Trying to read {required} bytes, only {Remaining} remaining.");
#endif
        }
    }
}
