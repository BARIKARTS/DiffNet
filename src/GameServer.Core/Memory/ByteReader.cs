using System;
using System.Runtime.CompilerServices;
using System.Text;
using GameServer.Core.Interfaces;

namespace GameServer.Core.Memory
{
    public unsafe class ByteReader : INetworkSerializer
    {
        private byte* _buffer;
        private int _capacity;
        private int _position;

        public bool IsWriting => false;
        public bool IsReading => true;
        public int Position => _position;
        public int Capacity => _capacity;

        public ByteReader() { }

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
                throw new IndexOutOfRangeException("Reader buffer overflow");

            value = Unsafe.ReadUnaligned<T>(_buffer + _position);
            _position += size;
        }

        public void SerializeString(ref string value, int maxLength = 64)
        {
            int length = 0;
            Serialize(ref length);

            if (length == 0)
            {
                value = string.Empty;
                return;
            }

            if (_position + length > _capacity)
                throw new IndexOutOfRangeException("Reader buffer overflow");

            value = Encoding.UTF8.GetString(_buffer + _position, length);
            _position += length;
        }

        public void SerializeBytes(byte* destination, int length)
        {
            if (_position + length > _capacity)
                throw new IndexOutOfRangeException("Reader buffer overflow");

            Unsafe.CopyBlock(destination, _buffer + _position, (uint)length);
            _position += length;
        }
    }
}
