using System;

namespace GameServer.Core.Interfaces
{
    /// <summary>
    /// Zero-allocation pointer based serializer.
    /// Used for state synchronization payload building.
    /// </summary>
    public unsafe interface INetworkSerializer
    {
        bool IsWriting { get; }
        bool IsReading { get; }
        int Position { get; }
        int Capacity { get; }
        
        // Unmanaged struct write (int, float, Vector3 etc.)
        void Serialize<T>(ref T value) where T : unmanaged;
        
        // Null terminated or length-prefixed stack string serialization
        void SerializeString(ref string value, int maxLength = 64);
        
        // Raw byte manipulation
        void SerializeBytes(byte* destination, int length);
    }
}
