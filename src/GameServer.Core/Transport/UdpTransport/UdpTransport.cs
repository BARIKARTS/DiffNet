using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using GameServer.Core.Interfaces;
using GameServer.Core.Types;

namespace GameServer.Core.Transport.UdpTransport
{
    /// <summary>
    /// Zero-allocation, high performance UDP transport using generic Socket.
    /// </summary>
    public class UdpTransport : INetworkTransport
    {
        private Socket? _socket;
        private byte[] _recvBuffer;
        
        // EndPoint ref object to avoid allocation on ReceiveFrom
        private EndPoint _remoteEndPoint;
        
        private Dictionary<EndPoint, PlayerRef> _endpointToPlayer = new();
        private Dictionary<PlayerRef, SocketAddress> _playerToAddress = new();
        private Dictionary<PlayerRef, RudpConnection> _playerConnections = new();

        private byte[] _sendBuffer; // Zero-allocation cache for send copies

        public event INetworkTransport.PlayerConnectedHandler? OnPlayerConnected;
        public event INetworkTransport.PlayerDisconnectedHandler? OnPlayerDisconnected;
        public event INetworkTransport.DataReceivedHandler? OnDataReceived;

        public UdpTransport(int bufferSize = 4096)
        {
            _recvBuffer = new byte[bufferSize];
            _sendBuffer = new byte[bufferSize];
            _remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        }

        public void StartServer(int port)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Blocking = false;
            
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            
            // Constrain connection reset exception from ICMP (Windows specific, highly recommended for UDP)
            const int SIO_UDP_CONNRESET = -1744830452;
            try {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    _socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
                }
            } catch { }

            _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        }

        public void Shutdown()
        {
            _socket?.Close();
            _socket = null;
        }

        public unsafe void Tick()
        {
            if (_socket == null) return;
            long currentTime = Environment.TickCount64;

            // 1. Dynamic RTO: If pending packets are not acknowledged and RTO is exceeded, resend (Retransmission)
            foreach (var conn in _playerConnections.Values)
            {
                conn.CheckTimeouts(currentTime, InternalSendRaw);
            }

            // 2. Process Incoming Packets
            while (_socket.Available > 0)
            {
                try
                {
                    int bytesRead = _socket.ReceiveFrom(_recvBuffer, ref _remoteEndPoint);
                    if (bytesRead >= sizeof(RudpHeader))
                    {
                        if (!_endpointToPlayer.TryGetValue(_remoteEndPoint, out PlayerRef playerRef))
                        {
                            playerRef = new PlayerRef(_endpointToPlayer.Count + 1);
                            var clonedEp = CreateClone(_remoteEndPoint);
                            _endpointToPlayer[clonedEp] = playerRef;
                            _playerToAddress[playerRef] = clonedEp.Serialize();
                            
                            var newConn = new RudpConnection(playerRef);
                            _playerConnections[playerRef] = newConn;

                            OnPlayerConnected?.Invoke(playerRef);
                        }

                        var connection = _playerConnections[playerRef];

                        fixed (byte* pBuffer = _recvBuffer)
                        {
                            // A) Zero-allocation deserialize Header using pointer
                            RudpHeader* header = (RudpHeader*)pBuffer;
                            
                            // B) Process ACK information (AckTime comparison can also be added for Ping / RTT calculation)
                            connection.SendWindow.ProcessAcks(header->AckNumber, header->AckBitfield);
                            
                            // C) Update our RemoteSequence (the SequenceNumber of the incoming packet)
                            connection.SendWindow.UpdateRemoteSequence(header->SequenceNumber);

                            // D) Pass only the payload to the game engine (skipping the Header length)
                            byte* payloadData = pBuffer + sizeof(RudpHeader);
                            int payloadLength = bytesRead - sizeof(RudpHeader);
                            
                            if (payloadLength > 0)
                            {
                                OnDataReceived?.Invoke(playerRef, payloadData, payloadLength);
                            }
                        }
                    }
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode != SocketError.WouldBlock) { }
                    break;
                }
            }
        }

        // Internal Retransmission / Raw Send function
        private unsafe void InternalSendRaw(PlayerRef player, ReadOnlySpan<byte> payload, RudpHeader header)
        {
            if (_playerToAddress.TryGetValue(player, out SocketAddress? address))
            {
                int totalLen = sizeof(RudpHeader) + payload.Length;
                fixed (byte* pSend = _sendBuffer)
                {
                    // Struct copy via pointer
                    *(RudpHeader*)pSend = header;
                }
                payload.CopyTo(_sendBuffer.AsSpan(sizeof(RudpHeader)));
                
                _socket?.SendTo(_sendBuffer.AsSpan(0, totalLen), SocketFlags.None, address);
            }
        }

        public unsafe void SendTo(PlayerRef player, ReadOnlySpan<byte> data, DeliveryMode mode = DeliveryMode.Unreliable)
        {
            if (_socket == null || !_playerConnections.TryGetValue(player, out RudpConnection? conn)) return;

            long currentTime = Environment.TickCount64;

            // If Reliable, add to the window and lease the Sequence number from there
            ushort seq = mode != DeliveryMode.Unreliable 
                ? conn.SendWindow.AddPendingPacket(data, mode, currentTime)
                : (ushort)0; // Sequence is not important for Unreliable (or an optional counter can be used)

            // Create the packet header
            var header = new RudpHeader
            {
                Mode = mode,
                SequenceNumber = seq,
                AckNumber = conn.SendWindow.RemoteSequence,
                AckBitfield = conn.SendWindow.AckBitfield
            };

            InternalSendRaw(player, data, header);
        }

        public void Broadcast(ReadOnlySpan<byte> data, DeliveryMode mode = DeliveryMode.Unreliable)
        {
            if (_socket == null) return;
            
            foreach (var kvp in _playerConnections)
            {
                SendTo(kvp.Key, data, mode); // SendTo must be called one by one because each user will have unique sequence and ACK information written
            }
        }


        private EndPoint CreateClone(EndPoint ep)
        {
            if (ep is IPEndPoint ip) return new IPEndPoint(ip.Address, ip.Port);
            throw new NotSupportedException("Only IPEndPoint supported");
        }

        public void Dispose()
        {
            Shutdown();
        }
    }
}
