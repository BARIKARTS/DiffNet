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

        // This method will be called from the admin panel
        public bool CloseRoom(string roomId)
        {
            // TODO: Logic for closing the specified room will be integrated
            return true;
        }
    }
}
