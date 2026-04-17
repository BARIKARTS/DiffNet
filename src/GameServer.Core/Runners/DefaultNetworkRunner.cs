using GameServer.Core.Interfaces;
using GameServer.Core.Transport.UdpTransport;
using GameServer.Core.Transport.WebSocketTransport;
using System;

namespace GameServer.Core.Runners
{
    public class DefaultNetworkRunner : INetworkRunner
    {
        private readonly UdpTransport _udpTransport;
        public WebSocketTransport WebTransport { get; }
        
        public int TickRate { get; private set; } = 60;
        public int CurrentTick { get; private set; } = 0;
        public bool IsServer => true;

        public DefaultNetworkRunner()
        {
            _udpTransport = new UdpTransport();
            WebTransport = new WebSocketTransport();
            
            // To bridge events to higher level components when they are implemented
            // e.g., OnPlayerConnected, OnDataReceived can be invoked here.
        }

        public void StartRunner(int port)
        {
            _udpTransport.StartServer(port);
            WebTransport.StartServer(port);
            CurrentTick = 0;
        }

        public void UpdateLoop()
        {
            _udpTransport.Tick();
            WebTransport.Tick();
            CurrentTick++;
        }

        public void StopRunner()
        {
            _udpTransport.Shutdown();
            WebTransport.Shutdown();
        }
        
        // Expose a unified SendTo / Broadcast or similar when RoomManager interacts.
        // For now, depending on player ID we can route the packet.
        public void SendTo(GameServer.Core.Types.PlayerRef player, ReadOnlySpan<byte> data, GameServer.Core.Types.DeliveryMode mode = GameServer.Core.Types.DeliveryMode.Unreliable)
        {
            if (player.Id >= 20000)
            {
                WebTransport.SendTo(player, data, mode);
            }
            else
            {
                _udpTransport.SendTo(player, data, mode);
            }
        }

        public void Broadcast(ReadOnlySpan<byte> data, GameServer.Core.Types.DeliveryMode mode = GameServer.Core.Types.DeliveryMode.Unreliable)
        {
            _udpTransport.Broadcast(data, mode);
            WebTransport.Broadcast(data, mode);
        }
    }
}
