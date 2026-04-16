using GameServer.Core.Types;

namespace GameServer.Core.Interfaces
{
    public interface IPlayerSession
    {
        PlayerRef PlayerId { get; }
        
        float Ping { get; }
        bool IsConnected { get; }

        void UpdatePing(float newPing);
        void Disconnect();
    }
}
