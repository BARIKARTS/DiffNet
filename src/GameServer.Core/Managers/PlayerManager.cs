using System.Collections.Concurrent;

namespace GameServer.Core.Managers
{
    public class PlayerManager
    {
        private int _ccu;
        
        public int CCU => _ccu;

        // Stub methods to manage players
        public void AddPlayer() => Interlocked.Increment(ref _ccu);
        public void RemovePlayer() => Interlocked.Decrement(ref _ccu);
        
        // This method will be called from the admin panel
        public bool KickPlayer(string playerId)
        {
            // TODO: Logic for disconnecting the current connection will be integrated
            return true;
        }

        public void BroadcastMessage(string message)
        {
            // TODO: Logic for sending messages (packets) to all players will be integrated
        }
    }
}
