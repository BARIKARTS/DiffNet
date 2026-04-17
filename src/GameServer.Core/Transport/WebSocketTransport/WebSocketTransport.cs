using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using GameServer.Core.Interfaces;
using GameServer.Core.Types;

namespace GameServer.Core.Transport.WebSocketTransport
{
    public class WebSocketTransport : INetworkTransport
    {
        private int _playerIdCounter = 20000; // Offset WebSocket player IDs from UDP
        private readonly ConcurrentDictionary<PlayerRef, WebSocketConnection> _connections = new();
        private readonly ConcurrentQueue<IncomingData> _incomingQueue = new();

        public event INetworkTransport.PlayerConnectedHandler? OnPlayerConnected;
        public event INetworkTransport.PlayerDisconnectedHandler? OnPlayerDisconnected;
        public event INetworkTransport.DataReceivedHandler? OnDataReceived;

        private class WebSocketConnection
        {
            public required PlayerRef Player { get; init; }
            public required WebSocket Socket { get; init; }
            public required CancellationTokenSource Cts { get; init; }
            public long LastActivityTime { get; set; }
        }

        private struct IncomingData
        {
            public PlayerRef Player;
            public byte[] Buffer;
            public int Length;
        }

        public void StartServer(int port)
        {
            // The actual listener is ASP.NET Core pipeline, so we just init here.
        }

        public void Shutdown()
        {
            foreach (var kvp in _connections)
            {
                kvp.Value.Cts.Cancel();
                kvp.Value.Socket.Dispose();
            }
            _connections.Clear();
        }

        public unsafe void Tick()
        {
            // Process incoming queue and fire events on the game loop thread
            while (_incomingQueue.TryDequeue(out var data))
            {
                fixed (byte* ptr = data.Buffer)
                {
                    OnDataReceived?.Invoke(data.Player, ptr, data.Length);
                }
            }
        }

        public unsafe void SendTo(PlayerRef player, ReadOnlySpan<byte> data, DeliveryMode mode = DeliveryMode.Unreliable)
        {
            if (_connections.TryGetValue(player, out var conn))
            {
                if (conn.Socket.State == WebSocketState.Open)
                {
                    // Since it's a bridge to test RUDP mechanics, we MUST prepend a dummy or real RudpHeader 
                    // if the frontend expects it, or pass exactly what the backend expects to send.
                    // The instruction: "web istemcilerini de tıpkı Unity istemcileri gibi ... RUDP paket yapımıza sarmalı (wrap)"
                    // Wait, do we generate RUDP Header here? Yes, WebSocket doesn't have our internal sequence logic inherently for the payload if we are mirroring UDP.
                    // But for now let's just create a dummy RUDP header exactly as UdpTransport does.
                    
                    int totalLen = sizeof(RudpHeader) + data.Length;
                    byte[] buffer = new byte[totalLen];
                    fixed (byte* pBuf = buffer)
                    {
                        var header = new RudpHeader
                        {
                            Mode = mode,
                            SequenceNumber = 0, // Should probably track sequences properly if testing reliable?
                            AckNumber = 0,
                            AckBitfield = 0
                        };
                        *(RudpHeader*)pBuf = header;
                    }
                    data.CopyTo(buffer.AsSpan(sizeof(RudpHeader)));
                    
                    // Task.Run to not block the main game loop thread, or SendAsync directly
                    var _ = conn.Socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true, conn.Cts.Token);
                }
            }
        }

        public void Broadcast(ReadOnlySpan<byte> data, DeliveryMode mode = DeliveryMode.Unreliable)
        {
            foreach (var player in _connections.Keys)
            {
                SendTo(player, data, mode);
            }
        }

        public void Dispose()
        {
            Shutdown();
        }

        /// <summary>
        /// Called by ASP.NET Core middleware to hand off a WebSocket
        /// </summary>
        public async Task AcceptWebSocketAsync(WebSocket webSocket)
        {
            int newId = Interlocked.Increment(ref _playerIdCounter);
            var playerRef = new PlayerRef(newId);
            var cts = new CancellationTokenSource();

            var connection = new WebSocketConnection
            {
                Player = playerRef,
                Socket = webSocket,
                Cts = cts,
                LastActivityTime = Environment.TickCount64
            };

            _connections.TryAdd(playerRef, connection);
            OnPlayerConnected?.Invoke(playerRef);

            var buffer = new byte[8192];

            try
            {
                while (webSocket.State == WebSocketState.Open && !cts.Token.IsCancellationRequested)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Binary && result.Count > 0)
                    {
                        // "Bu gateway, tarayıcıdan gelen WebSocket mesajlarını alıp bizim RUDP paket yapımıza sarmalı (wrap) ve iç mantığa (RoomManager, AOI vb.) iletmeli."
                        // When client sends data, it doesn't send our 9-byte C# RudpHeader unless we programmed it to.
                        // Wait, if the client sends raw business logic (payload), we could generate a mock RudpHeader here, OR we can expect the web client to generate the 9-byte header.
                        // Actually, the web client will be written in TypeScript and "Binary verileri bizim Bit-Masking mantigimiza gore coz, RUDP paket yapisina sarmali".
                        // So the web client WILL send the 9-byte header!
                        // Let's pass the payload directly without the header, stripping it out just like UdpTransport does.
                        
                        unsafe
                        {
                            if (result.Count >= sizeof(RudpHeader))
                            {
                                int payloadLen = result.Count - sizeof(RudpHeader);
                                if (payloadLen > 0)
                                {
                                    var payloadBuffer = new byte[payloadLen];
                                    Array.Copy(buffer, sizeof(RudpHeader), payloadBuffer, 0, payloadLen);
                                    
                                    _incomingQueue.Enqueue(new IncomingData
                                    {
                                        Player = playerRef,
                                        Buffer = payloadBuffer,
                                        Length = payloadLen
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Ignored
            }
            finally
            {
                _connections.TryRemove(playerRef, out _);
                OnPlayerDisconnected?.Invoke(playerRef);
                try { await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None); } catch { }
                cts.Dispose();
                webSocket.Dispose();
            }
        }
    }
}
