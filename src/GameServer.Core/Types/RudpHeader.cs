using System.Runtime.InteropServices;

namespace GameServer.Core.Types
{
    /// <summary>
    /// Packet header that forms the basis of the RUDP (Reliable UDP) protocol over the network.
    /// Designed with a bit-based and Unsafe approach, it can be written directly to the buffer with zero-allocation.
    /// Thanks to [StructLayout(..., Pack = 1)], no padding spaces are created in memory.
    /// Total Header Size: 9 Bytes
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct RudpHeader
    {
        // 1 byte - Communication channel used to send the packet
        public DeliveryMode Mode;

        // 2 bytes - Unique sequence number of this packet (0-65535, wraps around)
        public ushort SequenceNumber;

        // 2 bytes - The sequence number of the last packet successfully received by the sender
        public ushort AckNumber;

        // 4 bytes - Redundant ACK logic. Bit-mask map of the last 32 packets prior to AckNumber.
        // If the 1st bit (rightmost) is 1, it means packet AckNumber - 1 was also received.
        // This allows carrying ACK information for the last 32 packets in a single UDP packet (costing only 4 bytes).
        // If an ACK packet is dropped, data loss is compensated for since redundant information is carried in subsequent packets.
        public uint AckBitfield;
    }
}

