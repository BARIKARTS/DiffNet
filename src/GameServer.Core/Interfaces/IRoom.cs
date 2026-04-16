using System.Collections.Generic;
using GameServer.Core.Types;

namespace GameServer.Core.Interfaces
{
    public interface IRoom
    {
        string RoomId { get; }
        IReadOnlyCollection<IPlayerSession> Players { get; }
        
        void AddPlayer(IPlayerSession player);
        void RemovePlayer(PlayerRef playerId);

        void SpawnObject(INetworkObject netObj);
        void DespawnObject(NetworkId objId);

        void HandleHostMigration(PlayerRef disconnectedPlayer);
    }
}
