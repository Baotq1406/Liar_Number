using LiarNumberServer.Models;

namespace LiarNumberServer.Managers
{
    public class RoomManager
    {
        private readonly Dictionary<string, RoomInfo> _rooms = new();
        private readonly object _lock = new();
        private readonly Random _random = new();

        public bool TryCreateRoom(string hostPlayerId, string hostNickname, out RoomInfo? room, out string errorCode, out string message)
        {
            room = null;
            errorCode = string.Empty;
            message = string.Empty;

            lock (_lock)
            {
                for (var i = 0; i < 20; i++)
                {
                    var roomId = _random.Next(0, 10000).ToString("D4");
                    if (_rooms.ContainsKey(roomId))
                    {
                        continue;
                    }

                    room = new RoomInfo
                    {
                        RoomId = roomId,
                        HostPlayerId = hostPlayerId,
                        HostNickname = hostNickname,
                        IsGameStarted = false,
                        MaxPlayers = 4,
                        CreatedAt = DateTime.UtcNow
                    };

                    room.Players.Add(new RoomPlayerInfo
                    {
                        playerId = hostPlayerId,
                        nickname = hostNickname
                    });

                    _rooms[roomId] = room;
                    return true;
                }
            }

            errorCode = "ROOM_ID_EXHAUSTED";
            message = "Unable to generate unique roomId after 20 attempts";
            return false;
        }

        public bool TryGetRoom(string roomId, out RoomInfo? room)
        {
            lock (_lock)
            {
                return _rooms.TryGetValue(roomId, out room);
            }
        }

        public bool TryJoinRoom(string roomId, RoomPlayerInfo player, out string errorCode, out string message, out RoomInfo? room)
        {
            errorCode = string.Empty;
            message = string.Empty;
            room = null;

            lock (_lock)
            {
                if (!_rooms.TryGetValue(roomId, out room) || room == null)
                {
                    errorCode = "ROOM_NOT_FOUND";
                    message = "Room not found";
                    return false;
                }

                if (room.IsGameStarted)
                {
                    errorCode = "GAME_ALREADY_STARTED";
                    message = "Game already started";
                    return false;
                }

                if (room.Players.Any(p => p.playerId == player.playerId))
                {
                    errorCode = "ALREADY_IN_ROOM";
                    message = "Player already in room";
                    return false;
                }

                if (room.Players.Count >= room.MaxPlayers)
                {
                    errorCode = "ROOM_FULL";
                    message = "Room is full";
                    return false;
                }

                room.Players.Add(player);
                return true;
            }
        }

        public bool TryLeaveRoom(string roomId, string playerId, out string errorCode, out string message, out RoomInfo? room)
        {
            errorCode = string.Empty;
            message = string.Empty;
            room = null;

            lock (_lock)
            {
                if (!_rooms.TryGetValue(roomId, out room) || room == null)
                {
                    errorCode = "ROOM_NOT_FOUND";
                    message = "Room not found";
                    return false;
                }

                var removedCount = room.Players.RemoveAll(p => p.playerId == playerId);
                if (removedCount == 0)
                {
                    errorCode = "PLAYER_NOT_IN_ROOM";
                    message = "Player is not in room";
                    return false;
                }

                return true;
            }
        }

        public List<string> GetPlayerNames(string roomId)
        {
            lock (_lock)
            {
                if (!_rooms.TryGetValue(roomId, out var room) || room == null)
                {
                    return new List<string>();
                }

                return room.Players.Select(p => p.nickname).ToList();
            }
        }

        public bool RemoveRoom(string roomId)
        {
            lock (_lock)
            {
                return _rooms.Remove(roomId);
            }
        }

        public bool TryRemoveRoom(string roomId, out RoomInfo? room)
        {
            lock (_lock)
            {
                if (_rooms.TryGetValue(roomId, out room))
                {
                    _rooms.Remove(roomId);
                    return true;
                }

                room = null;
                return false;
            }
        }
    }
}
