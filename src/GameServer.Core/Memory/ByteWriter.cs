using System;
using System.Runtime.CompilerServices;
using System.Text;
using GameServer.Core.Interfaces;

namespace GameServer.Core.Memory
{
    public unsafe class ByteWriter : INetworkSerializer
    {
        private byte* _buffer;
        private int _capacity;
        private int _position;

        public bool IsWriting => true;
        public bool IsReading => false;
        public int Position => _position;
        public int Capacity => _capacity;

        public ByteWriter() { }

        public void SetBuffer(byte* buffer, int capacity)
        {
            _buffer = buffer;
            _capacity = capacity;
            _position = 0;
        }

        public void Serialize<T>(ref T value) where T : unmanaged
        {
            int size = sizeof(T);
            if (_position + size > _capacity)
                throw new IndexOutOfRangeException("Writer buffer overflow");

            Unsafe.WriteUnaligned(_buffer + _position, value);
            _position += size;
        }

        public void SerializeString(ref string value, int maxLength = 64)
        {
            if (string.IsNullOrEmpty(value))
            {
                int len = 0;
                Serialize(ref len);
                return;
            }

            int length = Encoding.UTF8.GetByteCount(value);
            if (length > maxLength) length = maxLength;
            
            Serialize(ref length);
            
            if (_position + length > _capacity)
                throw new IndexOutOfRangeException("Writer buffer overflow");

            fixed (char* strPtr = value)
            {
                Encoding.UTF8.GetBytes(strPtr, value.Length, _buffer + _position, length);
            }
            _position += length;
        }

        public void SerializeBytes(byte* destination, int length)
        {
            if (_position + length > _capacity)
                throw new IndexOutOfRangeException("Writer buffer overflow");

            Unsafe.CopyBlock(_buffer + _position, destination, (uint)length);
            _position += length;
        }
        
        public ReadOnlySpan<byte> GetSpan()
        {
            return new ReadOnlySpan<byte>(_buffer, _position);
        }
    }
}
