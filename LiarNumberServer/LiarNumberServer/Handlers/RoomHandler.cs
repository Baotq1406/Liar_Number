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
        private readonly int _avatarCount;

        public RoomHandler(RoomManager roomManager, int avatarCount = 12)
        {
            _roomManager = roomManager;
            _avatarCount = Math.Max(1, avatarCount);
        }

        public void HandleCreateRoom(string payloadJson, ClientConnection connection)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var connectionId = connection.ConnectionId;

            try
            {
                LogRoomAction(timestamp, connectionId, connection.PlayerId, connection.CurrentRoomId, "CreateRoom", "BAT DAU XU LY");

                if (string.IsNullOrWhiteSpace(payloadJson))
                {
                    SendCreateRoomFailed(connection, "INVALID_PAYLOAD", "CreateRoom payload is empty");
                    LogRoomAction(timestamp, connectionId, connection.PlayerId, connection.CurrentRoomId, "CreateRoom", "THAT BAI: INVALID_PAYLOAD (empty payload)");
                    return;
                }

                var request = JsonSerializer.Deserialize<CreateRoomRequest>(payloadJson);
                if (request == null)
                {
                    SendCreateRoomFailed(connection, "INVALID_PAYLOAD", "Invalid CreateRoom payload");
                    return;
                }

                if (string.IsNullOrWhiteSpace(connection.PlayerId) || string.IsNullOrWhiteSpace(connection.Nickname))
                {
                    SendCreateRoomFailed(connection, "NOT_AUTHENTICATED", "JoinLobby is required before CreateRoom");
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI CreateRoom: NOT_AUTHENTICATED");
                    return;
                }

                var playerId = connection.PlayerId.Trim();
                var nickname = connection.Nickname.Trim();
                var serverAvatarId = ClampAvatar(connection.AvatarId);
                var avatarId = ResolveAvatarForRoom(playerId, request.avatarId, serverAvatarId, timestamp, connectionId, "CreateRoom", roomId: "-");

                if (!string.IsNullOrWhiteSpace(request.playerId) && !string.Equals(request.playerId.Trim(), playerId, StringComparison.Ordinal))
                {
                    SendCreateRoomFailed(connection, "INVALID_REQUEST", "playerId does not match authenticated player");
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI CreateRoom: spoofed playerId req={request.playerId}, auth={playerId}");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(request.nickname) && !string.Equals(request.nickname.Trim(), nickname, StringComparison.Ordinal))
                {
                    SendCreateRoomFailed(connection, "INVALID_REQUEST", "nickname does not match authenticated player");
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI CreateRoom: spoofed nickname req={request.nickname}, auth={nickname}");
                    return;
                }

                lock (_lock)
                {
                    if (_playerRoomMap.ContainsKey(playerId) ||
                        _connectionRoomMap.ContainsKey(connection.ConnectionId) ||
                        !string.IsNullOrEmpty(connection.CurrentRoomId))
                    {
                        SendCreateRoomFailed(connection, "ALREADY_HOSTING", "Client already hosts a room");
                        Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI CreateRoom: already in room");
                        return;
                    }
                }

                if (!_roomManager.TryCreateRoom(playerId, nickname, avatarId, out var room, out var errorCode, out var message) || room == null)
                {
                    SendCreateRoomFailed(connection, errorCode, message);
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI CreateRoom: playerId={playerId}, errorCode={errorCode}, message={message}");
                    return;
                }

                connection.AvatarId = avatarId;
                connection.CurrentRoomId = room.RoomId;

                lock (_lock)
                {
                    _connectionRoomMap[connection.ConnectionId] = room.RoomId;
                    _playerRoomMap[playerId] = room.RoomId;
                    _playerConnections[playerId] = connection;

                    if (!_roomPlayers.TryGetValue(room.RoomId, out var players))
                    {
                        players = new HashSet<string>();
                        _roomPlayers[room.RoomId] = players;
                    }

                    players.Add(playerId);
                }

                var response = new RoomCreatedEvent
                {
                    roomId = room.RoomId,
                    hostId = room.HostPlayerId,
                    hostNickname = room.HostNickname,
                    hostAvatarId = GetRoomPlayerAvatar(room, room.HostPlayerId)
                };

                connection.SendMessage("RoomCreated", response);
                BroadcastRoomState(room.RoomId);
                BroadcastRoomPlayersUpdated(room.RoomId);
                LogRoomAction(timestamp, connectionId, room.HostPlayerId, room.RoomId, "CreateRoom", "THANH CONG");
            }
            catch (JsonException e)
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connectionId}] EXCEPTION CreateRoom JSON: {e.Message}");
                SendCreateRoomFailed(connection, "INVALID_PAYLOAD", "Invalid CreateRoom payload");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connectionId}] EXCEPTION CreateRoom: {e.Message}");
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connectionId}] Stack: {e.StackTrace}");
                SendCreateRoomFailed(connection, "INTERNAL_ERROR", "Internal server error");
            }
        }

        public void HandleJoinRoom(string payloadJson, ClientConnection connection)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var connectionId = connection.ConnectionId;

            try
            {
                LogRoomAction(timestamp, connectionId, connection.PlayerId, connection.CurrentRoomId, "JoinRoom", "BAT DAU XU LY");

                if (string.IsNullOrWhiteSpace(payloadJson))
                {
                    SendJoinRoomFailed(connection, "INVALID_PAYLOAD", "JoinRoom payload is empty");
                    LogRoomAction(timestamp, connectionId, connection.PlayerId, connection.CurrentRoomId, "JoinRoom", "THAT BAI: INVALID_PAYLOAD (empty payload)");
                    return;
                }

                var request = JsonSerializer.Deserialize<JoinRoomRequest>(payloadJson);
                if (request == null)
                {
                    SendJoinRoomFailed(connection, "INVALID_PAYLOAD", "Invalid JoinRoom payload");
                    return;
                }

                var roomId = (request.roomId ?? string.Empty).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(connection.PlayerId) || string.IsNullOrWhiteSpace(connection.Nickname))
                {
                    SendJoinRoomFailed(connection, "NOT_AUTHENTICATED", "JoinLobby is required before JoinRoom");
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI JoinRoom: NOT_AUTHENTICATED");
                    return;
                }

                var playerId = connection.PlayerId.Trim();
                var nickname = connection.Nickname.Trim();
                var serverAvatarId = ClampAvatar(connection.AvatarId);
                var avatarId = ResolveAvatarForRoom(playerId, request.avatarId, serverAvatarId, timestamp, connectionId, "JoinRoom", roomId);

                if (string.IsNullOrWhiteSpace(roomId))
                {
                    SendJoinRoomFailed(connection, "INVALID_REQUEST", "roomId is required");
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI JoinRoom: invalid input");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(request.playerId) && !string.Equals(request.playerId.Trim(), playerId, StringComparison.Ordinal))
                {
                    SendJoinRoomFailed(connection, "INVALID_REQUEST", "playerId does not match authenticated player");
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI JoinRoom: spoofed playerId req={request.playerId}, auth={playerId}");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(request.nickname) && !string.Equals(request.nickname.Trim(), nickname, StringComparison.Ordinal))
                {
                    SendJoinRoomFailed(connection, "INVALID_REQUEST", "nickname does not match authenticated player");
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI JoinRoom: spoofed nickname req={request.nickname}, auth={nickname}");
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
                    nickname = nickname,
                    avatarId = avatarId
                };

                if (!_roomManager.TryJoinRoom(roomId, joinPlayer, out var errorCode, out var errorMessage, out var room) || room == null)
                {
                    SendJoinRoomFailed(connection, errorCode, errorMessage);
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI JoinRoom: roomId={roomId}, playerId={playerId}, errorCode={errorCode}");
                    return;
                }

                connection.AvatarId = avatarId;
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
                    hostNickname = room.HostNickname,
                    hostAvatarId = GetRoomPlayerAvatar(room, room.HostPlayerId)
                };

                connection.SendMessage("RoomJoined", roomJoined);
                BroadcastPlayerJoined(roomId, nickname, avatarId);
                BroadcastRoomState(roomId);
                BroadcastRoomPlayersUpdated(roomId);

                LogRoomAction(timestamp, connectionId, playerId, roomId, "JoinRoom", $"THANH CONG: avatarId={avatarId}, roomSize={room.Players.Count}/{room.MaxPlayers}");
            }
            catch (JsonException e)
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connectionId}] EXCEPTION JoinRoom JSON: {e.Message}");
                SendJoinRoomFailed(connection, "INVALID_PAYLOAD", "Invalid JoinRoom payload");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connectionId}] EXCEPTION JoinRoom: {e.Message}");
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connectionId}] Stack: {e.StackTrace}");
                SendJoinRoomFailed(connection, "INTERNAL_ERROR", "Internal server error");
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
                BroadcastRoomState(roomId);
                BroadcastRoomPlayersUpdated(roomId);

                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] LeaveRoom THANH CONG: roomId={roomId}, playerId={playerId}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] EXCEPTION LeaveRoom: {e.Message}");
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] Stack: {e.StackTrace}");
            }
        }

        public void HandleStartGame(string payloadJson, ClientConnection connection)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

            try
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] BAT DAU XU LY StartGame");

                var request = JsonSerializer.Deserialize<StartGameRequest>(payloadJson);
                if (request == null)
                {
                    SendStartGameFailed(connection, "INVALID_PAYLOAD", "Invalid StartGame payload");
                    return;
                }

                var roomId = (request.roomId ?? string.Empty).Trim().ToUpperInvariant();
                var playerId = (request.playerId ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(playerId))
                {
                    SendStartGameFailed(connection, "INVALID_REQUEST", "roomId and playerId are required");
                    return;
                }

                if (!_roomManager.TryGetRoom(roomId, out var room) || room == null)
                {
                    SendStartGameFailed(connection, "ROOM_NOT_FOUND", "Room not found");
                    return;
                }

                if (!string.Equals(room.HostPlayerId, playerId, StringComparison.Ordinal))
                {
                    SendStartGameFailed(connection, "NOT_HOST", "Only host can start game");
                    return;
                }

                if (room.IsGameStarted)
                {
                    SendStartGameFailed(connection, "GAME_ALREADY_STARTED", "Game already started");
                    return;
                }

                var players = room.Players
                    .Select(p => p.nickname)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();

                if (players.Count < 2 || players.Count > 4)
                {
                    SendStartGameFailed(connection, "INVALID_PLAYER_COUNT", "Player count must be between 2 and 4");
                    return;
                }

                room.IsGameStarted = true;

                var gameStarted = new GameStartedEvent
                {
                    roomId = roomId,
                    players = players,
                    initialCardCount = Math.Max(0, request.initialCardCount)
                };

                List<ClientConnection> targets;
                lock (_lock)
                {
                    targets = GetRoomConnectionsSnapshotUnsafe(roomId);
                }

                foreach (var target in targets)
                {
                    target.SendMessage("GameStarted", gameStarted);
                }

                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] StartGame THANH CONG: roomId={roomId}, players={players.Count}, initialCardCount={gameStarted.initialCardCount}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] EXCEPTION StartGame: {e.Message}");
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] Stack: {e.StackTrace}");
                SendStartGameFailed(connection, "INTERNAL_ERROR", "Internal server error");
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
                BroadcastRoomState(roomId);
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

        private int ClampAvatar(int avatarId)
        {
            if (avatarId < 0 || avatarId >= _avatarCount)
            {
                return 0;
            }

            return avatarId;
        }

        private int GetRoomPlayerAvatar(RoomInfo room, string playerId)
        {
            if (room == null || string.IsNullOrWhiteSpace(playerId))
            {
                return 0;
            }

            var player = room.Players.FirstOrDefault(p => string.Equals(p.playerId, playerId, StringComparison.Ordinal));
            if (player == null)
            {
                return 0;
            }

            return ClampAvatar(player.avatarId);
        }

        private int ResolveAvatarForRoom(string playerId, int? requestedAvatarId, int serverAvatarId, string timestamp, string connectionId, string actionName, string roomId)
        {
            var authoritativeAvatarId = ClampAvatar(serverAvatarId);

            if (!requestedAvatarId.HasValue)
            {
                LogRoomAction(timestamp, connectionId, playerId, roomId, actionName, $"avatar source=server, clientAvatar=(none), authoritativeAvatar={authoritativeAvatarId}");
                return authoritativeAvatarId;
            }

            var requested = requestedAvatarId.Value;
            var clampedRequested = ClampAvatar(requested);

            if (requested != clampedRequested)
            {
                LogRoomAction(timestamp, connectionId, playerId, roomId, actionName, $"AVATAR_SPOOF_DETECTED: clientAvatar={requested} out-of-range -> authoritativeAvatar={authoritativeAvatarId}");
                return authoritativeAvatarId;
            }

            if (requested != authoritativeAvatarId)
            {
                LogRoomAction(timestamp, connectionId, playerId, roomId, actionName, $"AVATAR_MISMATCH: clientAvatar={requested}, authoritativeAvatar={authoritativeAvatarId} -> use authoritativeAvatar");
                return authoritativeAvatarId;
            }

            LogRoomAction(timestamp, connectionId, playerId, roomId, actionName, $"avatar validated={authoritativeAvatarId}");
            return authoritativeAvatarId;
        }

        private void LogRoomAction(string timestamp, string connectionId, string? playerId, string? roomId, string actionName, string message)
        {
            Console.WriteLine($"[{timestamp}] [RoomHandler] [{connectionId}] action={actionName}, playerId={playerId ?? "-"}, roomId={roomId ?? "-"}, {message}");
        }

        private void BroadcastPlayerJoined(string roomId, string playerName, int avatarId)
        {
            var payload = new PlayerJoinedEvent
            {
                roomId = roomId,
                playerName = playerName,
                avatarId = ClampAvatar(avatarId)
            };

            List<ClientConnection> targets;
            lock (_lock)
            {
                targets = GetRoomConnectionsSnapshotUnsafe(roomId);
            }

            Console.WriteLine($"[RoomHandler] Broadcast PlayerJoined: roomId={roomId}, playerName={playerName}, avatarId={payload.avatarId}, targets={targets.Count}");
            foreach (var target in targets)
            {
                target.SendMessage("PlayerJoined", payload);
            }
        }

        private void BroadcastRoomState(string roomId)
        {
            if (!_roomManager.TryGetRoom(roomId, out var room) || room == null)
            {
                return;
            }

            var payload = new RoomStateEvent
            {
                roomId = roomId,
                players = room.Players
                    .Select(p => new RoomStatePlayerInfo
                    {
                        playerName = p.nickname,
                        avatarId = ClampAvatar(p.avatarId)
                    })
                    .ToList()
            };

            List<ClientConnection> targets;
            lock (_lock)
            {
                targets = GetRoomConnectionsSnapshotUnsafe(roomId);
            }

            Console.WriteLine($"[RoomHandler] Broadcast RoomState: roomId={roomId}, players={payload.players.Count}, targets={targets.Count}");
            foreach (var target in targets)
            {
                target.SendMessage("RoomState", payload);
            }
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

        private void SendStartGameFailed(ClientConnection connection, string errorCode, string message)
        {
            var response = new StartGameFailedEvent
            {
                errorCode = errorCode,
                message = message
            };

            connection.SendMessage("StartGameFailed", response);
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
