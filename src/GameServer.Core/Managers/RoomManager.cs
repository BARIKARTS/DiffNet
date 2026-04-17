using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace GameServer.Core.Managers
{
    public class RoomInfo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public int PlayerCount { get; set; }
        public int MaxPlayers { get; set; } = 50;
    }

    public class RoomManager
    {
        private readonly ConcurrentDictionary<string, RoomInfo> _rooms = new();
        
        public int ActiveRoomCount => _rooms.Count;

        public RoomInfo CreateRoom(string name, int maxPlayers)
        {
            var room = new RoomInfo { Name = name, MaxPlayers = maxPlayers };
            _rooms.TryAdd(room.Id, room);
            return room;
        }

        public bool RemoveRoom(string roomId)
        {
            return _rooms.TryRemove(roomId, out _);
        }

        public bool CloseRoom(string roomId)
        {
            return RemoveRoom(roomId);
        }

        public IEnumerable<RoomInfo> GetRooms()
        {
            return _rooms.Values.ToList();
        }

        public bool JoinRoom(string roomId)
        {
            if (_rooms.TryGetValue(roomId, out var room))
            {
                lock (room)
                {
                    if (room.PlayerCount < room.MaxPlayers)
                    {
                        room.PlayerCount++;
                        return true;
                    }
                }
            }
            return false;
        }

        public void LeaveRoom(string roomId)
        {
            if (_rooms.TryGetValue(roomId, out var room))
            {
                lock (room)
                {
                    room.PlayerCount--;
                    if (room.PlayerCount <= 0)
                    {
                        RemoveRoom(roomId);
                    }
                }
            }
        }
    }
}
