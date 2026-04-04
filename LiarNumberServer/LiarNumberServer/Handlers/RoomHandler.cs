using System.Text.Json;
using LiarNumberServer.Managers;
using LiarNumberServer.Messages.ClientToServer;
using LiarNumberServer.Messages.ServerToClient;
using LiarNumberServer.Models;
using LiarNumberServer.Network;

namespace LiarNumberServer.Handlers
{
    public class RoomHandler
    {
        private readonly RoomManager _roomManager;
        private readonly Dictionary<string, string> _connectionRoomMap = new(); // connectionId -> roomId
        private readonly Dictionary<string, string> _playerRoomMap = new(); // playerId -> roomId
        private readonly Dictionary<string, ClientConnection> _playerConnections = new(); // playerId -> connection
        private readonly Dictionary<string, HashSet<string>> _roomPlayers = new(); // roomId -> playerIds
        private readonly object _lock = new();

        public RoomHandler(RoomManager roomManager)
        {
            _roomManager = roomManager;
        }

        public void HandleCreateRoom(string payloadJson, ClientConnection connection)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

            try
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] BAT DAU XU LY CreateRoom");

                var request = JsonSerializer.Deserialize<CreateRoomRequest>(payloadJson);
                if (request == null)
                {
                    SendCreateRoomFailed(connection, "INVALID_PAYLOAD", "Invalid CreateRoom payload");
                    return;
                }

                if (string.IsNullOrWhiteSpace(request.playerId) || string.IsNullOrWhiteSpace(request.nickname))
                {
                    SendCreateRoomFailed(connection, "INVALID_REQUEST", "playerId and nickname are required");
                    return;
                }

                lock (_lock)
                {
                    if (_playerRoomMap.ContainsKey(request.playerId) ||
                        _connectionRoomMap.ContainsKey(connection.ConnectionId) ||
                        !string.IsNullOrEmpty(connection.CurrentRoomId))
                    {
                        SendCreateRoomFailed(connection, "ALREADY_HOSTING", "Client already hosts a room");
                        Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI CreateRoom: already in room");
                        return;
                    }
                }

                if (!_roomManager.TryCreateRoom(request.playerId, request.nickname, out var room, out var errorCode, out var message) || room == null)
                {
                    SendCreateRoomFailed(connection, errorCode, message);
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI CreateRoom: playerId={request.playerId}, errorCode={errorCode}, message={message}");
                    return;
                }

                connection.PlayerId = request.playerId;
                connection.Nickname = request.nickname;
                connection.CurrentRoomId = room.RoomId;

                lock (_lock)
                {
                    _connectionRoomMap[connection.ConnectionId] = room.RoomId;
                    _playerRoomMap[request.playerId] = room.RoomId;
                    _playerConnections[request.playerId] = connection;

                    if (!_roomPlayers.TryGetValue(room.RoomId, out var players))
                    {
                        players = new HashSet<string>();
                        _roomPlayers[room.RoomId] = players;
                    }

                    players.Add(request.playerId);
                }

                var response = new RoomCreatedEvent
                {
                    roomId = room.RoomId,
                    hostId = room.HostPlayerId,
                    hostNickname = room.HostNickname
                };

                connection.SendMessage("RoomCreated", response);
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] CreateRoom THANH CONG: playerId={room.HostPlayerId}, roomId={room.RoomId}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] EXCEPTION CreateRoom: {e.Message}");
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] Stack: {e.StackTrace}");
                SendCreateRoomFailed(connection, "INTERNAL_ERROR", "Internal server error");
            }
        }

        public void HandleJoinRoom(string payloadJson, ClientConnection connection)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

            try
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] BAT DAU XU LY JoinRoom");

                var request = JsonSerializer.Deserialize<JoinRoomRequest>(payloadJson);
                if (request == null)
                {
                    SendJoinRoomFailed(connection, "INVALID_REQUEST", "Invalid JoinRoom payload");
                    return;
                }

                var roomId = (request.roomId ?? string.Empty).Trim().ToUpperInvariant();
                var playerId = (request.playerId ?? string.Empty).Trim();
                var nickname = (request.nickname ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(nickname))
                {
                    SendJoinRoomFailed(connection, "INVALID_REQUEST", "roomId, playerId, nickname are required");
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI JoinRoom: invalid input");
                    return;
                }

                lock (_lock)
                {
                    if (_playerRoomMap.ContainsKey(playerId) ||
                        _connectionRoomMap.ContainsKey(connection.ConnectionId) ||
                        !string.IsNullOrEmpty(connection.CurrentRoomId))
                    {
                        SendJoinRoomFailed(connection, "ALREADY_IN_ROOM", "Player already in another room");
                        Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI JoinRoom: playerId={playerId}, reason=ALREADY_IN_ROOM");
                        return;
                    }
                }

                var joinPlayer = new RoomPlayerInfo
                {
                    playerId = playerId,
                    nickname = nickname
                };

                if (!_roomManager.TryJoinRoom(roomId, joinPlayer, out var errorCode, out var errorMessage, out var room) || room == null)
                {
                    SendJoinRoomFailed(connection, errorCode, errorMessage);
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI JoinRoom: roomId={roomId}, playerId={playerId}, errorCode={errorCode}");
                    return;
                }

                connection.PlayerId = playerId;
                connection.Nickname = nickname;
                connection.CurrentRoomId = roomId;

                lock (_lock)
                {
                    _playerRoomMap[playerId] = roomId;
                    _connectionRoomMap[connection.ConnectionId] = roomId;
                    _playerConnections[playerId] = connection;

                    if (!_roomPlayers.TryGetValue(roomId, out var roomPlayerIds))
                    {
                        roomPlayerIds = new HashSet<string>();
                        _roomPlayers[roomId] = roomPlayerIds;
                    }

                    roomPlayerIds.Add(playerId);
                    if (!string.IsNullOrWhiteSpace(room.HostPlayerId))
                    {
                        roomPlayerIds.Add(room.HostPlayerId);
                    }
                }

                var roomJoined = new RoomJoinedEvent
                {
                    roomId = room.RoomId,
                    hostId = room.HostPlayerId,
                    hostNickname = room.HostNickname
                };

                connection.SendMessage("RoomJoined", roomJoined);
                BroadcastRoomPlayersUpdated(roomId);

                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] JoinRoom THANH CONG: roomId={roomId}, playerId={playerId}, roomSize={room.Players.Count}/{room.MaxPlayers}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] EXCEPTION JoinRoom: {e.Message}");
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] Stack: {e.StackTrace}");
                SendJoinRoomFailed(connection, "INVALID_REQUEST", "Invalid JoinRoom request");
            }
        }

        public void HandleLeaveRoom(string payloadJson, ClientConnection connection)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

            try
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] BAT DAU XU LY LeaveRoom");

                var request = JsonSerializer.Deserialize<LeaveRoomRequest>(payloadJson);
                if (request == null)
                {
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI LeaveRoom: invalid payload");
                    return;
                }

                var roomId = (request.roomId ?? string.Empty).Trim().ToUpperInvariant();
                var playerId = (request.playerId ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(playerId))
                {
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI LeaveRoom: roomId/playerId rong");
                    return;
                }

                if (!_roomManager.TryGetRoom(roomId, out var room) || room == null)
                {
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI LeaveRoom: roomId={roomId}, reason=ROOM_NOT_FOUND");
                    return;
                }

                if (!room.Players.Any(p => p.playerId == playerId))
                {
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI LeaveRoom: roomId={roomId}, playerId={playerId}, reason=PLAYER_NOT_IN_ROOM");
                    return;
                }

                if (room.HostPlayerId == playerId)
                {
                    if (!TryCloseRoom(roomId, playerId, "HostCanceled", includeClosedBy: true, out var affectedCount, out var errorCode, out var errorMessage))
                    {
                        Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI LeaveRoom(host): roomId={roomId}, errorCode={errorCode}, message={errorMessage}");
                        return;
                    }

                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] LeaveRoom(host) dong room: roomId={roomId}, hostId={playerId}, affectedMembers={affectedCount}");
                    return;
                }

                if (!_roomManager.TryLeaveRoom(roomId, playerId, out var leaveErrorCode, out var leaveErrorMessage, out _))
                {
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI LeaveRoom: roomId={roomId}, playerId={playerId}, errorCode={leaveErrorCode}, message={leaveErrorMessage}");
                    return;
                }

                CleanupPlayerRoomMapping(playerId, roomId, connection);
                BroadcastRoomPlayersUpdated(roomId);

                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] LeaveRoom THANH CONG: roomId={roomId}, playerId={playerId}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] EXCEPTION LeaveRoom: {e.Message}");
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] Stack: {e.StackTrace}");
            }
        }

        public void HandleCancelRoom(string payloadJson, ClientConnection connection)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

            try
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] BAT DAU XU LY CancelRoom");

                var request = JsonSerializer.Deserialize<CancelRoomRequest>(payloadJson);
                if (request == null)
                {
                    SendCancelRoomFailed(connection, "INVALID_PAYLOAD", "Invalid CancelRoom payload");
                    return;
                }

                if (string.IsNullOrWhiteSpace(request.roomId) || string.IsNullOrWhiteSpace(request.playerId))
                {
                    SendCancelRoomFailed(connection, "INVALID_REQUEST", "roomId and playerId are required");
                    return;
                }

                if (!_roomManager.TryGetRoom(request.roomId, out var room) || room == null)
                {
                    SendCancelRoomFailed(connection, "ROOM_NOT_FOUND", "Room not found");
                    return;
                }

                if (!room.Players.Any(p => p.playerId == request.playerId))
                {
                    SendCancelRoomFailed(connection, "PLAYER_NOT_IN_ROOM", "Player is not in room");
                    return;
                }

                if (room.HostPlayerId != request.playerId)
                {
                    SendCancelRoomFailed(connection, "NOT_HOST", "Only host can cancel room");
                    return;
                }

                if (!TryCloseRoom(request.roomId, request.playerId, "HostCanceled", includeClosedBy: true, out var affectedCount, out var errorCode, out var errorMessage))
                {
                    SendCancelRoomFailed(connection, errorCode, errorMessage);
                    return;
                }

                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] CancelRoom THANH CONG: roomId={request.roomId}, hostId={request.playerId}, affectedMembers={affectedCount}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] EXCEPTION CancelRoom: {e.Message}");
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] Stack: {e.StackTrace}");
                SendCancelRoomFailed(connection, "INTERNAL_ERROR", "Internal server error");
            }
        }

        public void HandleDisconnect(ClientConnection connection)
        {
            var playerId = connection.PlayerId;
            if (string.IsNullOrWhiteSpace(playerId))
            {
                return;
            }

            string? roomId;
            lock (_lock)
            {
                _playerConnections.Remove(playerId);
                _connectionRoomMap.Remove(connection.ConnectionId);
                _playerRoomMap.TryGetValue(playerId, out roomId);
            }

            if (string.IsNullOrWhiteSpace(roomId))
            {
                return;
            }

            if (!_roomManager.TryGetRoom(roomId, out var room) || room == null)
            {
                CleanupPlayerRoomMapping(playerId, roomId, connection);
                return;
            }

            if (room.HostPlayerId == playerId)
            {
                if (TryCloseRoom(roomId, playerId, "HostCanceled", includeClosedBy: false, out var affectedCount, out _, out _))
                {
                    Console.WriteLine($"[RoomHandler] [{connection.ConnectionId}] Host disconnect dong room: roomId={roomId}, hostId={playerId}, affectedMembers={affectedCount}");
                }

                return;
            }

            if (_roomManager.TryLeaveRoom(roomId, playerId, out _, out _, out _))
            {
                CleanupPlayerRoomMapping(playerId, roomId, connection);
                BroadcastRoomPlayersUpdated(roomId);
            }
        }

        private bool TryCloseRoom(string roomId, string closedByPlayerId, string reason, bool includeClosedBy, out int affectedMembers, out string errorCode, out string message)
        {
            affectedMembers = 0;
            errorCode = string.Empty;
            message = string.Empty;

            List<ClientConnection> snapshotConnections;
            lock (_lock)
            {
                snapshotConnections = GetRoomConnectionsSnapshotUnsafe(roomId);
            }

            if (!_roomManager.TryRemoveRoom(roomId, out var removedRoom) || removedRoom == null)
            {
                errorCode = "ROOM_NOT_FOUND";
                message = "Room not found";
                return false;
            }

            var memberIds = removedRoom.Players.Select(p => p.playerId).ToList();

            lock (_lock)
            {
                foreach (var memberId in memberIds)
                {
                    _playerRoomMap.Remove(memberId);
                    if (_playerConnections.TryGetValue(memberId, out var memberConnection))
                    {
                        memberConnection.CurrentRoomId = null;
                        _connectionRoomMap.Remove(memberConnection.ConnectionId);
                    }
                }

                _roomPlayers.Remove(roomId);
            }

            var targets = includeClosedBy
                ? snapshotConnections
                : snapshotConnections.Where(c => !string.Equals(c.PlayerId, closedByPlayerId, StringComparison.Ordinal)).ToList();

            var roomClosedEvent = new RoomClosedEvent
            {
                roomId = roomId,
                reason = reason,
                closedBy = closedByPlayerId
            };

            foreach (var target in targets)
            {
                target.SendMessage("RoomClosed", roomClosedEvent);
            }

            affectedMembers = targets.Count;
            return true;
        }

        private List<ClientConnection> GetRoomConnectionsSnapshotUnsafe(string roomId)
        {
            var result = new List<ClientConnection>();

            if (!_roomPlayers.TryGetValue(roomId, out var playerIds))
            {
                return result;
            }

            foreach (var playerId in playerIds)
            {
                if (_playerConnections.TryGetValue(playerId, out var connection))
                {
                    result.Add(connection);
                }
            }

            return result;
        }

        private void CleanupPlayerRoomMapping(string playerId, string roomId, ClientConnection connection)
        {
            lock (_lock)
            {
                _playerRoomMap.Remove(playerId);
                _connectionRoomMap.Remove(connection.ConnectionId);
                if (_roomPlayers.TryGetValue(roomId, out var players))
                {
                    players.Remove(playerId);
                    if (players.Count == 0)
                    {
                        _roomPlayers.Remove(roomId);
                    }
                }
            }

            connection.CurrentRoomId = null;
        }

        private void SendCreateRoomFailed(ClientConnection connection, string errorCode, string message)
        {
            var response = new CreateRoomFailedEvent
            {
                errorCode = errorCode,
                message = message
            };

            connection.SendMessage("CreateRoomFailed", response);
        }

        private void SendCancelRoomFailed(ClientConnection connection, string errorCode, string message)
        {
            var response = new CancelRoomFailedEvent
            {
                errorCode = errorCode,
                message = message
            };

            connection.SendMessage("CancelRoomFailed", response);
        }

        private void SendJoinRoomFailed(ClientConnection connection, string errorCode, string message)
        {
            var response = new JoinRoomFailedEvent
            {
                errorCode = errorCode,
                message = message
            };

            connection.SendMessage("JoinRoomFailed", response);
        }

        private void BroadcastRoomPlayersUpdated(string roomId)
        {
            var payload = new RoomPlayersUpdatedEvent
            {
                roomId = roomId,
                players = _roomManager.GetPlayerNames(roomId)
            };

            List<ClientConnection> targets;
            lock (_lock)
            {
                targets = GetRoomConnectionsSnapshotUnsafe(roomId);
            }

            foreach (var target in targets)
            {
                target.SendMessage("RoomPlayersUpdated", payload);
            }
        }
    }
}
