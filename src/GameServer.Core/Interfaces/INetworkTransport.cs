using System;
using GameServer.Core.Types;

namespace GameServer.Core.Interfaces
{
    public interface INetworkTransport : IDisposable
    {
        // Zero-allocation events
        delegate void PlayerConnectedHandler(PlayerRef player);
        delegate void PlayerDisconnectedHandler(PlayerRef player);
        unsafe delegate void DataReceivedHandler(PlayerRef player, byte* rawData, int length);

        event PlayerConnectedHandler OnPlayerConnected;
        event PlayerDisconnectedHandler OnPlayerDisconnected;
        event DataReceivedHandler OnDataReceived;

        void StartServer(int port);
        void Shutdown();
        void Tick();

        // High-performance send
        void SendTo(PlayerRef player, ReadOnlySpan<byte> data, DeliveryMode mode = DeliveryMode.Unreliable);
        void Broadcast(ReadOnlySpan<byte> data, DeliveryMode mode = DeliveryMode.Unreliable);
    }
}
