using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GameServer.Core.Types;

namespace GameServer.Core.Transport.UdpTransport
{
    /// <summary>
    /// Runs the Sliding Window logic for RUDP on a Ring Buffer (Circular Buffer) algorithm.
    /// Adheres to zero-allocation goals using ArrayPool.
    /// Used to store packets that have been sent but not yet acknowledged (ACK).
    /// </summary>
    public unsafe class RudpRingBuffer
    {
        private readonly int _capacity;
        private readonly PendingPacket[] _buffer;

        // Sequence numbers
        public ushort LocalSequence { get; private set; } // The next sequence number we send
        public ushort RemoteSequence { get; private set; } // The most recent number we expect from the remote side
        
        // Bitmask for the last 32 packets
        public uint AckBitfield { get; private set; }

        public RudpRingBuffer(int capacity = 1024)
        {
            // A capacity that is a power of 2 provides performance for masking operations.
            _capacity = capacity;
            _buffer = new PendingPacket[capacity];
            LocalSequence = 0;
            RemoteSequence = 0;
            AckBitfield = 0;
        }

        /// <summary>
        /// Copies new data to be sent to the remote side into the Ring Buffer and stores packet information.
        /// </summary>
        public ushort AddPendingPacket(ReadOnlySpan<byte> payload, DeliveryMode mode, long currentTime)
        {
            ushort seq = LocalSequence++;
            int index = seq % _capacity;

            ref PendingPacket packet = ref _buffer[index];

            // If the old packet is still in the Buffer (ACK not received but being overwritten), it indicates a capacity issue.
            if (packet.IsActive && packet.Buffer != null)
            {
                ArrayPool<byte>.Shared.Return(packet.Buffer);
            }

            // For zero-allocation goals, request from pool and copy with pointer
            byte[] rentedArray = ArrayPool<byte>.Shared.Rent(payload.Length);
            payload.CopyTo(rentedArray.AsSpan(0, payload.Length));

            packet.IsActive = true;
            packet.Sequence = seq;
            packet.Length = payload.Length;
            packet.Mode = mode;
            packet.Buffer = rentedArray;
            packet.LastSendTime = currentTime;

            return seq;
        }

        /// <summary>
        /// Analyzes the Header received from the remote side and performs ACK acknowledgment for packets we have sent.
        /// </summary>
        public void ProcessAcks(ushort ackNumber, uint ackBitfield)
        {
            // Acknowledgment of incoming ackNumber:
            AcknowledgePacket(ackNumber);

            // Redundant ACK logic: We return the relevant packets from the pool based on the acknowledgment of the past 32 bits.
            for (int i = 0; i < 32; i++)
            {
                // Check if that bit is 1 with a bit mask
                if ((ackBitfield & (1U << i)) != 0)
                {
                    ushort seqToAck = (ushort)(ackNumber - (i + 1));
                    AcknowledgePacket(seqToAck);
                }
            }
        }

        /// <summary>
        /// When a packet is acknowledged (ACK), it returns the byte array taken from the pool back to the system (does not create GC pressure).
        /// </summary>
        private void AcknowledgePacket(ushort sequence)
        {
            int index = sequence % _capacity;
            ref PendingPacket packet = ref _buffer[index];

            // For security purposes: Both the packet must be active and the sequence must match (to avoid deleting old packets)
            if (packet.IsActive && packet.Sequence == sequence)
            {
                packet.IsActive = false;
                if (packet.Buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(packet.Buffer);
                    packet.Buffer = null;
                }
            }
        }

        /// <summary>
        /// Updates the sequence we received (and Redundant ACK logic) when a new RUDP packet arrives from the remote side.
        /// </summary>
        public void UpdateRemoteSequence(ushort incomingSeq)
        {
            int diff = SequenceDiff(incomingSeq, RemoteSequence);

            if (diff > 0)
            {
                // If the incoming packet is greater than the current RemoteSequence, i.e., it's a new leading packet
                // We need to shift the Bitfield to the left by the difference
                AckBitfield <<= diff;

                // Mark the previous RemoteSequence as received (1) in the history relative to the new sequence
                if (diff <= 32)
                {
                    AckBitfield |= (1U << (diff - 1));
                }

                RemoteSequence = incomingSeq;
            }
            else if (diff < 0)
            {
                // If a past packet arrives (Out-of-order), set the corresponding bit to 1 (Received)
                int bitIndex = -diff - 1;
                if (bitIndex < 32)
                {
                    AckBitfield |= (1U << bitIndex);
                }
            }
        }

        public Span<PendingPacket> GetBuffer() => _buffer;

        // Sequence numbers wrap around at 65535, so standard difference doesn't work.
        public static int SequenceDiff(ushort a, ushort b)
        {
            int diff = a - b;
            if (diff < -32768) return diff + 65536;
            if (diff > 32768) return diff - 65536;
            return diff;
        }
    }

    /// <summary>
    /// PendingPacket is the fundamental unit of the Ring buffer that can live on the stack and avoids allocations.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PendingPacket
    {
        public bool IsActive;
        public DeliveryMode Mode;
        public ushort Sequence;
        public int Length;
        public long LastSendTime;
        public byte[]? Buffer; // Temporarily rented array from the pool
    }

}
