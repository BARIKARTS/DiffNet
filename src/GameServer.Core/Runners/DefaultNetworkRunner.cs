using GameServer.Core.Interfaces;
using GameServer.Core.Transport.UdpTransport;

namespace GameServer.Core.Runners
{
    public class DefaultNetworkRunner : INetworkRunner
    {
        private readonly UdpTransport _transport;
        
        public int TickRate { get; private set; } = 60;
        public int CurrentTick { get; private set; } = 0;
        public bool IsServer => true;

        public DefaultNetworkRunner()
        {
            _transport = new UdpTransport();
        }

        public void StartRunner(int port)
        {
            _transport.StartServer(port);
            CurrentTick = 0;
        }

        public void UpdateLoop()
        {
            _transport.Tick();
            CurrentTick++;
        }

        public void StopRunner()
        {
            _transport.Shutdown();
        }
    }
}
