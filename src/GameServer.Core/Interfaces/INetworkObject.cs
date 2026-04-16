using GameServer.Core.Types;

namespace GameServer.Core.Interfaces
{
    public interface INetworkObject
    {
        NetworkId ObjectId { get; }
        PlayerRef OwnerRef { get; }
        
        bool HasStateAuthority(PlayerRef player);
        bool HasInputAuthority(PlayerRef player);

        // Uses unsafe serializer for zero-allocation
        unsafe void Serialize(INetworkSerializer serializer);
        unsafe void Deserialize(INetworkSerializer serializer);

        void TransferOwnership(PlayerRef newOwner);
    }
}
