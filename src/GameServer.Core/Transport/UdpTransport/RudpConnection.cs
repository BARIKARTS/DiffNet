using System;
using System.Diagnostics;
using GameServer.Core.Types;

namespace GameServer.Core.Transport.UdpTransport
{
    /// <summary>
    /// Tracks RUDP logic, RTT (Ping) duration, variable RTO, and RingBuffer windows for a player's connection.
    /// </summary>
    public class RudpConnection
    {
        public PlayerRef Player { get; }
        public RudpRingBuffer SendWindow { get; }

        // RTT and RTO logic details
        public double SmoothedRTT { get; private set; } = 100.0; // Default ping 100ms
        public double RTTVar { get; private set; } = 50.0;
        public double RTO { get; private set; } = 250.0; // Initial timeout 250ms

        public RudpConnection(PlayerRef player)
        {
            Player = player;
            SendWindow = new RudpRingBuffer(1024);
        }

        public void UpdateRTT(double sampleRTT)
        {
            // RFC 6298: Jacobson's algorithm (Dynamic TCP RTT/RTO adjustment)
            RTTVar = (0.75 * RTTVar) + (0.25 * Math.Abs(SmoothedRTT - sampleRTT));
            SmoothedRTT = (0.875 * SmoothedRTT) + (0.125 * sampleRTT);
            
            RTO = SmoothedRTT + Math.Max(50.0, 4 * RTTVar);
            
            // If there is delay or loss, RTO will be large. Let's add a cap for safety.
            if (RTO > 2000.0) RTO = 2000.0; // 2 Sec maximum timeout
            if (RTO < 50.0) RTO = 50.0;     // 50 Ms minimum timeout
        }

        public delegate void ResendPacketHandler(PlayerRef player, ReadOnlySpan<byte> payload, RudpHeader header);

        /// <summary>
        /// Detects packets that have timed out during a Tick but have not received an ACK.
        /// Processes the sequence requiring Retransmission.
        /// </summary>
        public void CheckTimeouts(long currentTimeMs, ResendPacketHandler resendHandler)
        {
            var buffer = SendWindow.GetBuffer();
            for (int i = 0; i < buffer.Length; i++)
            {
                ref var packet = ref buffer[i];
                if (packet.IsActive && packet.Buffer != null)
                {
                    // If the expected time has passed according to the RTO calculation (Probability of Packet Loss/Drop)
                    if (currentTimeMs - packet.LastSendTime > RTO)
                    {
                        packet.LastSendTime = currentTimeMs;
                        
                        // Congestion control - Double the RTO in case of consecutive timeouts
                        RTO = Math.Min(2000.0, RTO * 2);

                        // Retransmission trigger
                        var header = new RudpHeader
                        {
                            Mode = packet.Mode,
                            SequenceNumber = packet.Sequence,
                            AckNumber = SendWindow.RemoteSequence,
                            AckBitfield = SendWindow.AckBitfield
                        };

                        resendHandler(Player, packet.Buffer.AsSpan(0, packet.Length), header);
                    }
                }
            }
        }
    }

}
