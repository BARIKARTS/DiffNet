using System.Collections.Concurrent;

namespace GameServer.Core.Managers
{
    public class RoomManager
    {
        private int _activeRooms;
        
        public int ActiveRoomCount => _activeRooms;

        // Stub methods to manage rooms
        public void CreateRoom() => Interlocked.Increment(ref _activeRooms);
        public void RemoveRoom() => Interlocked.Decrement(ref _activeRooms);

        // Bu metod admin panelinden çağrılacak
        public bool CloseRoom(string roomId)
        {
            // TODO: Belirtilen odayı kapatma mantığı entegre edilecek
            return true;
        }
    }
}
