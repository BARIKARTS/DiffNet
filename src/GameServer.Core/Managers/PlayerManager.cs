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
        
        // Bu metod admin panelinden çağrılacak
        public bool KickPlayer(string playerId)
        {
            // TODO: Mevcut bağlantıyı kesme mantığı entegre edilecek
            return true;
        }

        public void BroadcastMessage(string message)
        {
            // TODO: Tüm oyunculara mesaj (paket) gönderme mantığı entegre edilecek
        }
    }
}
