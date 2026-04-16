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
        private const int CardsPerPlayer = 5;
        private const int SpecialCard = 3;
        private const int SpecialCardCount = 2;
        private const int LiarResolveDelayMs = 5000;
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

        public void HandlePlayCard(string payloadJson, ClientConnection connection)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

            try
            {
                if (string.IsNullOrWhiteSpace(payloadJson))
                {
                    SendError(connection, "INVALID_PAYLOAD", "PlayCard payload is empty");
                    return;
                }

                var request = JsonSerializer.Deserialize<PlayCardRequest>(payloadJson);
                if (request == null)
                {
                    SendError(connection, "INVALID_PAYLOAD", "Invalid PlayCard payload");
                    return;
                }

                var roomId = (request.roomId ?? string.Empty).Trim().ToUpperInvariant();
                var playerId = (request.playerId ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(playerId) || request.cards == null || request.cards.Count == 0)
                {
                    SendError(connection, "INVALID_REQUEST", "roomId, playerId and cards are required");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(connection.PlayerId) && !string.Equals(connection.PlayerId.Trim(), playerId, StringComparison.Ordinal))
                {
                    SendError(connection, "INVALID_REQUEST", "playerId does not match authenticated player");
                    return;
                }

                if (!_roomManager.TryGetRoom(roomId, out var room) || room == null)
                {
                    SendError(connection, "ROOM_NOT_FOUND", "Room not found");
                    return;
                }

                if (!room.IsGameStarted || room.TurnState == null)
                {
                    SendError(connection, "INVALID_PHASE", "Game is not started");
                    return;
                }

                var turnState = room.TurnState;
                if (turnState.phase != TurnPhase.WaitingPlay)
                {
                    SendError(connection, "INVALID_PHASE", "Room is not in WaitingPlay phase");
                    return;
                }

                if (!room.Players.Any(p => string.Equals(p.playerId, playerId, StringComparison.Ordinal)))
                {
                    SendError(connection, "INVALID_REQUEST", "Player is not in room");
                    return;
                }

                if (IsPlayerDead(room, playerId))
                {
                    SendError(connection, "PLAYER_DEAD", "Dead player cannot PLAY_CARD");
                    return;
                }

                if (!string.Equals(turnState.currentTurnPlayerId, playerId, StringComparison.Ordinal))
                {
                    SendError(connection, "NOT_YOUR_TURN", "Not your turn");
                    return;
                }

                var actor = room.Players.FirstOrDefault(p => string.Equals(p.playerId, playerId, StringComparison.Ordinal));
                if (actor != null)
                {
                    if (actor.cardCount <= 0)
                    {
                        LogRoomAction(timestamp, connection.ConnectionId, playerId, roomId, "AUTO_SKIP_NO_CARDS", "current turn player has no cards -> skip to next playable player");
                        var roundResetPayload = NextTurnClockwise(room, roomId, connection.ConnectionId, trigger: "PLAY_CARD_NO_CARDS", playId: null);
                        if (roundResetPayload != null)
                        {
                            BroadcastToRoom(roomId, "ROUND_RESET", roundResetPayload);
                        }

                        BroadcastTurnUpdate(roomId, room);
                        return;
                    }

                    if (request.cards.Count > actor.cardCount)
                    {
                        SendError(connection, "INVALID_REQUEST", "Played cards exceed player's card count");
                        return;
                    }
                }

                var playedCardCount = Math.Max(0, request.cards.Count);
                var playId = turnState.nextPlayId == int.MaxValue ? 1 : turnState.nextPlayId + 1;
                turnState.nextPlayId = playId;

                turnState.lastPlay = new LastPlayContext
                {
                    playId = playId,
                    actorPlayerId = playerId,
                    declaredNumber = request.declaredNumber,
                    playedCardCount = playedCardCount,
                    cards = request.cards.ToList()
                };

                if (actor != null)
                {
                    actor.cardCount = Math.Max(0, actor.cardCount - playedCardCount);
                }

                turnState.phase = TurnPhase.WaitingResponses;
                turnState.resolvingPlayId = null;
                turnState.respondedIds.Clear();
                turnState.pendingResponderIds = room.Players
                    .Select(p => p.playerId)
                    .Where(id => !string.Equals(id, playerId, StringComparison.Ordinal) && !IsPlayerDead(room, id))
                    .ToHashSet(StringComparer.Ordinal);

                var actorPayload = new ShowWaitingEvent
                {
                    roomId = roomId,
                    playId = playId,
                    actorPlayerId = playerId,
                    message = "Waiting for other players to SKIP or LIAR",
                    playedCardCount = playedCardCount,
                    phase = turnState.phase
                };

                if (turnState.pendingResponderIds.Count == 0)
                {
                    var roundResetPayload = NextTurnClockwise(room, roomId, connection.ConnectionId, trigger: "PLAY_CARD_NO_PENDING_RESPONDERS", playId);
                    if (roundResetPayload != null)
                    {
                        BroadcastToRoom(roomId, "ROUND_RESET", roundResetPayload);
                    }

                    BroadcastTurnUpdate(roomId, room);
                    LogRoomAction(timestamp, connection.ConnectionId, playerId, roomId, "PLAY_CARD", $"playId={playId}, no pending responders -> auto next turn");
                    return;
                }

                connection.SendMessage("SHOW_WAITING", actorPayload);

                var panelPayload = new ShowLiarPanelEvent
                {
                    roomId = roomId,
                    playId = playId,
                    actorPlayerId = playerId,
                    actorPlayerName = GetPlayerNameById(room, playerId),
                    declaredNumber = request.declaredNumber,
                    playedCardCount = playedCardCount,
                    previewCards = new List<int>(),
                    phase = turnState.phase
                };

                SendToRoomExcept(roomId, playerId, "SHOW_LIAR_PANEL", panelPayload);
                LogRoomAction(timestamp, connection.ConnectionId, playerId, roomId, "PLAY_CARD", $"playId={playId}, playedCardCount={playedCardCount}, phase={turnState.phase}, pending={turnState.pendingResponderIds.Count}");
            }
            catch (JsonException e)
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] EXCEPTION PlayCard JSON: {e.Message}");
                SendError(connection, "INVALID_PAYLOAD", "Invalid PlayCard payload");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] EXCEPTION PlayCard: {e.Message}");
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] Stack: {e.StackTrace}");
                SendError(connection, "INTERNAL_ERROR", "Internal server error");
            }
        }

        public void HandleSkip(string payloadJson, ClientConnection connection)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

            try
            {
                if (string.IsNullOrWhiteSpace(payloadJson))
                {
                    SendError(connection, "INVALID_PAYLOAD", "Skip payload is empty");
                    return;
                }

                var request = JsonSerializer.Deserialize<SkipRequest>(payloadJson);
                if (request == null)
                {
                    SendError(connection, "INVALID_PAYLOAD", "Invalid Skip payload");
                    return;
                }

                var roomId = (request.roomId ?? string.Empty).Trim().ToUpperInvariant();
                var playerId = (request.playerId ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(playerId))
                {
                    SendError(connection, "INVALID_REQUEST", "roomId and playerId are required");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(connection.PlayerId) && !string.Equals(connection.PlayerId.Trim(), playerId, StringComparison.Ordinal))
                {
                    SendError(connection, "INVALID_REQUEST", "playerId does not match authenticated player");
                    return;
                }

                if (!_roomManager.TryGetRoom(roomId, out var room) || room == null || room.TurnState == null)
                {
                    SendError(connection, "ROOM_NOT_FOUND", "Room not found");
                    return;
                }

                var turnState = room.TurnState;
                if (turnState.phase != TurnPhase.WaitingResponses)
                {
                    SendError(connection, "INVALID_PHASE", "Room is not in WaitingResponses phase");
                    return;
                }

                if (!turnState.pendingResponderIds.Contains(playerId))
                {
                    SendError(connection, "INVALID_REQUEST", "Player is not pending response");
                    return;
                }

                if (IsPlayerDead(room, playerId))
                {
                    SendError(connection, "PLAYER_DEAD", "Dead player cannot SKIP");
                    return;
                }

                turnState.pendingResponderIds.Remove(playerId);
                turnState.respondedIds.Add(playerId);

                if (turnState.pendingResponderIds.Count == 0)
                {
                    var roundResetPayload = NextTurnClockwise(room, roomId, connection.ConnectionId, trigger: "SKIP_ALL_RESPONDED", playId: turnState.lastPlay?.playId);
                    if (roundResetPayload != null)
                    {
                        BroadcastToRoom(roomId, "ROUND_RESET", roundResetPayload);
                    }

                    BroadcastTurnUpdate(roomId, room);
                }

                LogRoomAction(timestamp, connection.ConnectionId, playerId, roomId, "SKIP", $"pending={turnState.pendingResponderIds.Count}");
            }
            catch (JsonException e)
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] EXCEPTION Skip JSON: {e.Message}");
                SendError(connection, "INVALID_PAYLOAD", "Invalid Skip payload");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] EXCEPTION Skip: {e.Message}");
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] Stack: {e.StackTrace}");
                SendError(connection, "INTERNAL_ERROR", "Internal server error");
            }
        }

        public void HandleLiar(string payloadJson, ClientConnection connection)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

            try
            {
                if (string.IsNullOrWhiteSpace(payloadJson))
                {
                    SendError(connection, "INVALID_PAYLOAD", "Liar payload is empty");
                    return;
                }

                var request = JsonSerializer.Deserialize<LiarRequest>(payloadJson);
                if (request == null)
                {
                    SendError(connection, "INVALID_PAYLOAD", "Invalid Liar payload");
                    return;
                }

                var roomId = (request.roomId ?? string.Empty).Trim().ToUpperInvariant();
                var playerId = (request.playerId ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(playerId))
                {
                    SendError(connection, "INVALID_REQUEST", "roomId and playerId are required");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(connection.PlayerId) && !string.Equals(connection.PlayerId.Trim(), playerId, StringComparison.Ordinal))
                {
                    SendError(connection, "INVALID_REQUEST", "playerId does not match authenticated player");
                    return;
                }

                if (!_roomManager.TryGetRoom(roomId, out var room) || room == null || room.TurnState == null)
                {
                    SendError(connection, "ROOM_NOT_FOUND", "Room not found");
                    return;
                }

                if (!room.Players.Any(p => string.Equals(p.playerId, playerId, StringComparison.Ordinal)))
                {
                    SendError(connection, "INVALID_REQUEST", "Player is not in room");
                    return;
                }

                if (IsPlayerDead(room, playerId))
                {
                    SendError(connection, "PLAYER_DEAD", "Dead player cannot LIAR");
                    return;
                }

                RevealPlayedCardsEvent revealPayload;
                int playId;
                string actorPlayerId;
                lock (_lock)
                {
                    var turnState = room.TurnState;
                    if (turnState == null || turnState.phase != TurnPhase.WaitingResponses)
                    {
                        SendError(connection, "INVALID_PHASE", "Room is not in WaitingResponses phase");
                        return;
                    }

                    if (turnState.lastPlay == null)
                    {
                        SendError(connection, "INVALID_PHASE", "No current play to reveal");
                        return;
                    }

                    if (!turnState.pendingResponderIds.Contains(playerId))
                    {
                        SendError(connection, "INVALID_REQUEST", "Player is not pending response");
                        return;
                    }

                    var lastPlay = turnState.lastPlay;
                    playId = lastPlay.playId;
                    if (turnState.resolvingPlayId == playId || turnState.resolvedPlayIds.Contains(playId))
                    {
                        LogRoomAction(timestamp, connection.ConnectionId, playerId, roomId, "LIAR_DUPLICATE_REJECT", $"playId={playId}, resolving={turnState.resolvingPlayId}, resolved={turnState.resolvedPlayIds.Contains(playId)}");
                        SendError(connection, "PLAY_ALREADY_RESOLVING", "This play is already resolving/resolved");
                        return;
                    }

                    if (string.Equals(lastPlay.actorPlayerId, playerId, StringComparison.Ordinal))
                    {
                        SendError(connection, "INVALID_REQUEST", "Actor cannot self-accuse with LIAR");
                        return;
                    }

                    turnState.pendingResponderIds.Remove(playerId);
                    turnState.respondedIds.Add(playerId);
                    turnState.phase = TurnPhase.Resolving;
                    turnState.resolvingPlayId = playId;

                    actorPlayerId = lastPlay.actorPlayerId;
                    revealPayload = new RevealPlayedCardsEvent
                    {
                        roomId = roomId,
                        playId = playId,
                        actorPlayerId = actorPlayerId,
                        actorPlayerName = GetPlayerNameById(room, actorPlayerId),
                        cards = lastPlay.cards.ToList()
                    };
                }

                BroadcastToRoom(roomId, "REVEAL_PLAYED_CARDS", revealPayload);
                LogRoomAction(timestamp, connection.ConnectionId, actorPlayerId, roomId, "REVEAL_PLAYED_CARDS", $"playId={playId}, actor={actorPlayerId}, accuser={playerId}, cards=[{string.Join(',', revealPayload.cards)}]");
                LogRoomAction(timestamp, connection.ConnectionId, playerId, roomId, "LIAR_DELAY_START", $"playId={playId}, delayMs={LiarResolveDelayMs}");

                _ = ResolveLiarAfterDelayAsync(roomId, playId, playerId, connection.ConnectionId);
            }
            catch (JsonException e)
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] EXCEPTION Liar JSON: {e.Message}");
                SendError(connection, "INVALID_PAYLOAD", "Invalid Liar payload");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] EXCEPTION Liar: {e.Message}");
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] Stack: {e.StackTrace}");
                SendError(connection, "INTERNAL_ERROR", "Internal server error");
            }
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

                room.RouletteStates[playerId] = new RouletteState();

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

                room.RouletteStates[playerId] = new RouletteState();

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

                if (!string.IsNullOrWhiteSpace(connection.PlayerId) && !string.Equals(connection.PlayerId.Trim(), playerId, StringComparison.Ordinal))
                {
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI LeaveRoom: spoofed playerId req={playerId}, auth={connection.PlayerId}");
                    return;
                }

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

                if (!_roomManager.TryLeaveRoom(roomId, playerId, out var leaveErrorCode, out var leaveErrorMessage, out var updatedRoom) || updatedRoom == null)
                {
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] THAT BAI LeaveRoom: roomId={roomId}, playerId={playerId}, errorCode={leaveErrorCode}, message={leaveErrorMessage}");
                    return;
                }

                updatedRoom.RouletteStates.Remove(playerId);

                CleanupPlayerRoomMapping(playerId, roomId, connection);
                SynchronizeTurnStateAfterPlayerRemoved(roomId, updatedRoom, playerId);
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

                if (string.IsNullOrWhiteSpace(payloadJson))
                {
                    SendStartGameFailed(connection, "INVALID_PAYLOAD", "StartGame payload is empty");
                    return;
                }

                var request = JsonSerializer.Deserialize<StartGameRequest>(payloadJson);
                if (request == null)
                {
                    SendStartGameFailed(connection, "INVALID_PAYLOAD", "Invalid StartGame payload");
                    return;
                }

                var roomId = (request.roomId ?? string.Empty).Trim().ToUpperInvariant();
                var playerId = (request.playerId ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(connection.PlayerId) && !string.Equals(connection.PlayerId.Trim(), playerId, StringComparison.Ordinal))
                {
                    SendStartGameFailed(connection, "INVALID_REQUEST", "playerId does not match authenticated player");
                    return;
                }

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

                var seed = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));
                var random = new Random(seed);
                var deck = BuildGameDeck();

                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] StartGame: roomId={roomId}, playerId={playerId}, seed={seed}, deckBeforeShuffle={FormatDeckSummary(deck)}");
                ShuffleDeck(deck, random);
                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] StartGame: roomId={roomId}, playerId={playerId}, deckAfterShuffle={FormatDeckSummary(deck)}");

                if (!TryDealHands(players, deck, CardsPerPlayer, out var hands, out var dealError))
                {
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] StartGame THAT BAI: roomId={roomId}, playerId={playerId}, reason={dealError}");
                    SendStartGameFailed(connection, "DECK_NOT_ENOUGH", dealError);
                    return;
                }

                InitializeTurnState(room, random);
                foreach (var player in room.Players)
                {
                    player.cardCount = CardsPerPlayer;
                    player.isDead = false;
                    room.RouletteStates[player.playerId] = new RouletteState();
                }

                room.IsGameStarted = true;

                var gameStarted = new GameStartedEvent
                {
                    roomId = roomId,
                    players = players,
                    initialCardCount = CardsPerPlayer,
                    destinyCard = room.TurnState?.destinyCard ?? 0,
                    hands = hands
                };

                foreach (var hand in hands)
                {
                    Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] StartGame chia bai: roomId={roomId}, player={hand.playerName}, cardCount={hand.cards.Count}, cards=[{string.Join(',', hand.cards)}]");
                }

                List<ClientConnection> targets;
                lock (_lock)
                {
                    targets = GetRoomConnectionsSnapshotUnsafe(roomId);
                }

                foreach (var target in targets)
                {
                    target.SendMessage("GameStarted", gameStarted);
                }

                BroadcastTurnUpdate(roomId, room);

                Console.WriteLine($"[{timestamp}] [RoomHandler] [{connection.ConnectionId}] StartGame THANH CONG: roomId={roomId}, players={players.Count}, initialCardCount={gameStarted.initialCardCount}, destinyCard={gameStarted.destinyCard}");
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

                if (!string.IsNullOrWhiteSpace(connection.PlayerId) && !string.Equals(connection.PlayerId.Trim(), request.playerId.Trim(), StringComparison.Ordinal))
                {
                    SendCancelRoomFailed(connection, "INVALID_REQUEST", "playerId does not match authenticated player");
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

            if (_roomManager.TryLeaveRoom(roomId, playerId, out _, out _, out var updatedRoom) && updatedRoom != null)
            {
                updatedRoom.RouletteStates.Remove(playerId);
                CleanupPlayerRoomMapping(playerId, roomId, connection);
                SynchronizeTurnStateAfterPlayerRemoved(roomId, updatedRoom, playerId);
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
                removedRoom.TurnState = null;
                removedRoom.IsGameStarted = false;
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

        private void InitializeTurnState(RoomInfo room, Random random)
        {
            var order = room.Players.Select(p => p.playerId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
            if (order.Count < 2)
            {
                room.TurnState = null;
                return;
            }

            var currentTurnIndex = random.Next(0, order.Count);
            var destinyCard = random.Next(0, 3);
            room.TurnState = new RoomTurnState
            {
                turnOrderPlayerIds = order,
                currentTurnIndex = currentTurnIndex,
                currentTurnPlayerId = order[currentTurnIndex],
                destinyCard = destinyCard,
                nextPlayId = 0,
                phase = TurnPhase.WaitingPlay,
                lastPlay = null,
                resolvingPlayId = null,
                lastRoundResetPlayId = null,
                resolvedPlayIds = new HashSet<int>(),
                pendingResponderIds = new HashSet<string>(StringComparer.Ordinal),
                respondedIds = new HashSet<string>(StringComparer.Ordinal)
            };

            if (IsPlayerDead(room, room.TurnState.currentTurnPlayerId) && !TryMoveToNextAliveTurn(room.TurnState, room))
            {
                room.IsGameStarted = false;
                room.TurnState = null;
            }
        }

        private RoundResetEvent? NextTurnClockwise(RoomInfo room, string roomId, string connectionId, string trigger, int? playId)
        {
            var turnState = room.TurnState;
            if (turnState == null || turnState.turnOrderPlayerIds.Count == 0)
            {
                room.IsGameStarted = false;
                room.TurnState = null;
                return null;
            }

            if (turnState.turnOrderPlayerIds.Count < 2)
            {
                room.IsGameStarted = false;
                room.TurnState = null;
                return null;
            }

            RoundResetEvent? roundResetPayload = null;

            turnState.currentTurnIndex = (turnState.currentTurnIndex + 1) % turnState.turnOrderPlayerIds.Count;
            if (!TryMoveToNextAliveWithCards(turnState, room))
            {
                if (AllAlivePlayersOutOfCards(turnState, room))
                {
                    roundResetPayload = ResetRoundDeckAndHands(room, roomId, connectionId, trigger, playId);
                    if (!TryMoveToNextAliveWithCards(turnState, room))
                    {
                        room.IsGameStarted = false;
                        room.TurnState = null;
                        return roundResetPayload;
                    }
                }
                else
                {
                    room.IsGameStarted = false;
                    room.TurnState = null;
                    return null;
                }
            }

            turnState.phase = TurnPhase.WaitingPlay;
            turnState.lastPlay = null;
            turnState.resolvingPlayId = null;
            turnState.pendingResponderIds.Clear();
            turnState.respondedIds.Clear();
            return roundResetPayload;
        }

        private void SynchronizeTurnStateAfterPlayerRemoved(string roomId, RoomInfo room, string removedPlayerId)
        {
            var turnState = room.TurnState;
            if (turnState == null)
            {
                return;
            }

            var previousTurnIndex = turnState.currentTurnIndex;
            var removedIndex = turnState.turnOrderPlayerIds.FindIndex(id => string.Equals(id, removedPlayerId, StringComparison.Ordinal));
            var removedWasCurrentTurn = removedIndex == previousTurnIndex;
            if (removedIndex >= 0)
            {
                turnState.turnOrderPlayerIds.RemoveAt(removedIndex);
            }

            turnState.pendingResponderIds.Remove(removedPlayerId);
            turnState.respondedIds.Remove(removedPlayerId);
            room.RouletteStates.Remove(removedPlayerId);

            if (turnState.turnOrderPlayerIds.Count < 2)
            {
                room.TurnState = null;
                room.IsGameStarted = false;
                return;
            }

            if (removedIndex >= 0)
            {
                if (removedWasCurrentTurn)
                {
                    if (turnState.currentTurnIndex >= turnState.turnOrderPlayerIds.Count)
                    {
                        turnState.currentTurnIndex = 0;
                    }

                    turnState.currentTurnPlayerId = turnState.turnOrderPlayerIds[turnState.currentTurnIndex];
                    turnState.phase = TurnPhase.WaitingPlay;
                    turnState.lastPlay = null;
                    turnState.pendingResponderIds.Clear();
                    turnState.respondedIds.Clear();
                }
                else if (removedIndex < turnState.currentTurnIndex)
                {
                    turnState.currentTurnIndex--;
                }
            }

            if (turnState.currentTurnIndex < 0 || turnState.currentTurnIndex >= turnState.turnOrderPlayerIds.Count)
            {
                turnState.currentTurnIndex = 0;
            }

            turnState.currentTurnPlayerId = turnState.turnOrderPlayerIds[turnState.currentTurnIndex];

            if (IsPlayerDead(room, turnState.currentTurnPlayerId) && !TryMoveToNextAliveTurn(turnState, room))
            {
                room.TurnState = null;
                room.IsGameStarted = false;
                return;
            }

            RoundResetEvent? roundResetPayload = null;

            if (turnState.phase == TurnPhase.WaitingResponses && turnState.pendingResponderIds.Count == 0)
            {
                roundResetPayload = NextTurnClockwise(room, roomId, connectionId: "-", trigger: "SYNC_AFTER_REMOVE", playId: null);
            }

            if (room.IsGameStarted)
            {
                if (roundResetPayload != null)
                {
                    BroadcastToRoom(roomId, "ROUND_RESET", roundResetPayload);
                }

                BroadcastTurnUpdate(roomId, room);
            }
        }

        private void BroadcastTurnUpdate(string roomId, RoomInfo room)
        {
            var turnState = room.TurnState;
            if (turnState == null)
            {
                return;
            }

            if (turnState.phase == TurnPhase.WaitingPlay && !HasCards(room, turnState.currentTurnPlayerId))
            {
                var roundResetPayload = NextTurnClockwise(room, roomId, connectionId: "-", trigger: "TURN_UPDATE_NO_CARDS", playId: null);
                if (roundResetPayload != null)
                {
                    BroadcastToRoom(roomId, "ROUND_RESET", roundResetPayload);
                }

                turnState = room.TurnState;
                if (turnState == null)
                {
                    return;
                }
            }

            var payload = new TurnUpdateEvent
            {
                roomId = roomId,
                destinyCard = turnState.destinyCard,
                currentTurnPlayerId = turnState.currentTurnPlayerId,
                currentTurnPlayerName = GetPlayerNameById(room, turnState.currentTurnPlayerId),
                currentTurnIndex = turnState.currentTurnIndex,
                turnOrderPlayerIds = turnState.turnOrderPlayerIds.ToList(),
                phase = turnState.phase
            };

            BroadcastToRoom(roomId, "TURN_UPDATE", payload);
            BroadcastRoomState(roomId);
        }

        private async Task ResolveLiarAfterDelayAsync(string roomId, int playId, string accuserPlayerId, string connectionId)
        {
            await Task.Delay(LiarResolveDelayMs).ConfigureAwait(false);

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            ResolveResultEvent? resolvePayload = null;
            RoomInfo? roomForTurnUpdate = null;
            RoundResetEvent? roundResetPayload = null;

            lock (_lock)
            {
                if (!_roomManager.TryGetRoom(roomId, out var room) || room == null)
                {
                    LogRoomAction(timestamp, connectionId, accuserPlayerId, roomId, "LIAR_RESOLVE_REJECT_STALE", $"playId={playId}, reason=ROOM_NOT_FOUND_AFTER_DELAY");
                    return;
                }

                var turnState = room.TurnState;
                if (turnState == null)
                {
                    LogRoomAction(timestamp, connectionId, accuserPlayerId, roomId, "LIAR_RESOLVE_REJECT_STALE", $"playId={playId}, reason=TURN_STATE_NULL_AFTER_DELAY");
                    return;
                }

                if (turnState.lastPlay == null)
                {
                    LogRoomAction(timestamp, connectionId, accuserPlayerId, roomId, "LIAR_RESOLVE_REJECT_STALE", $"playId={playId}, reason=LAST_PLAY_NULL_AFTER_DELAY");
                    return;
                }

                if (turnState.lastPlay.playId != playId)
                {
                    LogRoomAction(timestamp, connectionId, accuserPlayerId, roomId, "LIAR_RESOLVE_REJECT_RACE", $"playId={playId}, currentPlayId={turnState.lastPlay.playId}");
                    return;
                }

                if (turnState.resolvedPlayIds.Contains(playId))
                {
                    LogRoomAction(timestamp, connectionId, accuserPlayerId, roomId, "LIAR_RESOLVE_REJECT_DUPLICATE", $"playId={playId}, reason=ALREADY_RESOLVED");
                    return;
                }

                if (turnState.resolvingPlayId != playId)
                {
                    LogRoomAction(timestamp, connectionId, accuserPlayerId, roomId, "LIAR_RESOLVE_REJECT_RACE", $"playId={playId}, resolvingPlayId={turnState.resolvingPlayId}");
                    return;
                }

                var lastPlay = turnState.lastPlay;
                var destiny = turnState.destinyCard;
                var revealedCards = lastPlay.cards.ToList();
                var allMatch = revealedCards.All(c => NormalizeCard(c, destiny) == destiny);
                var punishedPlayerId = allMatch ? accuserPlayerId : lastPlay.actorPlayerId;
                var reason = allMatch ? "ALL_MATCH_DESTINY" : "HAS_MISMATCH";

                var rouletteState = GetOrCreateRouletteState(room, punishedPlayerId);
                var stageBefore = Math.Clamp(rouletteState.survivalStage, 1, 6);
                var hit = Random.Shared.Next(0, 6) < stageBefore;
                var stageAfter = stageBefore;

                if (hit)
                {
                    ApplyDeath(room, punishedPlayerId);
                    rouletteState.isDead = true;
                }
                else
                {
                    stageAfter = Math.Min(6, stageBefore + 1);
                    rouletteState.survivalStage = stageAfter;
                    roundResetPayload = ResetRoundDeckAndHands(room, roomId, connectionId, trigger: "LIAR_NO_DEATH", playId);
                }

                turnState.resolvingPlayId = null;
                turnState.resolvedPlayIds.Add(playId);

                resolvePayload = new ResolveResultEvent
                {
                    roomId = roomId,
                    playId = playId,
                    accuserPlayerId = accuserPlayerId,
                    accusedPlayerId = lastPlay.actorPlayerId,
                    punishedPlayerId = punishedPlayerId,
                    liar = !allMatch,
                    reason = reason,
                    destinyCard = destiny,
                    revealedCards = revealedCards,
                    roulette = new ResolveRouletteResult
                    {
                        stageBefore = stageBefore,
                        stageAfter = stageAfter,
                        hit = hit,
                        isDead = rouletteState.isDead
                    }
                };

                LogRoomAction(timestamp, connectionId, accuserPlayerId, roomId, "LIAR_RESOLVE_AFTER_DELAY", $"playId={playId}, actor={lastPlay.actorPlayerId}, accuser={accuserPlayerId}, destiny={destiny}, cards=[{string.Join(',', revealedCards)}], allMatch={allMatch}, punishedPlayerId={punishedPlayerId}");
                LogRoomAction(timestamp, connectionId, punishedPlayerId, roomId, "LIAR_ROULETTE", $"playId={playId}, stageBefore={stageBefore}, stageAfter={stageAfter}, hit={hit}, isDead={rouletteState.isDead}");
                var punishedPlayer = room.Players.FirstOrDefault(p => string.Equals(p.playerId, punishedPlayerId, StringComparison.Ordinal));
                LogRoomAction(timestamp, connectionId, punishedPlayerId, roomId, "LIAR_PLAYER_STATE", $"playId={playId}, cardCount={punishedPlayer?.cardCount ?? -1}, isDead={punishedPlayer?.isDead ?? false}");

                var turnRoundResetPayload = NextTurnClockwise(room, roomId, connectionId, trigger: "LIAR_RESOLVE", playId);
                roundResetPayload ??= turnRoundResetPayload;
                roomForTurnUpdate = room;
            }

            if (resolvePayload == null)
            {
                return;
            }

            BroadcastToRoom(roomId, "RESOLVE_RESULT", resolvePayload);
            if (roundResetPayload != null)
            {
                BroadcastToRoom(roomId, "ROUND_RESET", roundResetPayload);
            }

            if (roomForTurnUpdate?.TurnState != null)
            {
                BroadcastTurnUpdate(roomId, roomForTurnUpdate);
            }
        }

        private static int NormalizeCard(int card, int destinyCard)
        {
            return card == SpecialCard ? destinyCard : card;
        }

        private bool IsPlayerDead(RoomInfo room, string playerId)
        {
            return room.Players.FirstOrDefault(p => string.Equals(p.playerId, playerId, StringComparison.Ordinal))?.isDead == true;
        }

        private RouletteState GetOrCreateRouletteState(RoomInfo room, string playerId)
        {
            if (!room.RouletteStates.TryGetValue(playerId, out var state))
            {
                state = new RouletteState();
                room.RouletteStates[playerId] = state;
            }

            state.survivalStage = Math.Clamp(state.survivalStage, 1, 6);
            return state;
        }

        private void ApplyDeath(RoomInfo room, string playerId)
        {
            var player = room.Players.FirstOrDefault(p => string.Equals(p.playerId, playerId, StringComparison.Ordinal));
            if (player != null)
            {
                player.isDead = true;
                player.cardCount = 0;
            }

            var rouletteState = GetOrCreateRouletteState(room, playerId);
            rouletteState.isDead = true;
        }

        private bool TryMoveToNextAliveTurn(RoomTurnState turnState, RoomInfo room)
        {
            var total = turnState.turnOrderPlayerIds.Count;
            if (total == 0)
            {
                return false;
            }

            for (var i = 0; i < total; i++)
            {
                var index = (turnState.currentTurnIndex + i) % total;
                var candidate = turnState.turnOrderPlayerIds[index];
                if (IsPlayerDead(room, candidate))
                {
                    continue;
                }

                turnState.currentTurnIndex = index;
                turnState.currentTurnPlayerId = candidate;
                return true;
            }

            return false;
        }

        private bool TryMoveToNextAliveWithCards(RoomTurnState turnState, RoomInfo room)
        {
            var total = turnState.turnOrderPlayerIds.Count;
            if (total == 0)
            {
                return false;
            }

            for (var i = 0; i < total; i++)
            {
                var index = (turnState.currentTurnIndex + i) % total;
                var candidate = turnState.turnOrderPlayerIds[index];
                if (IsPlayerDead(room, candidate) || !HasCards(room, candidate))
                {
                    continue;
                }

                turnState.currentTurnIndex = index;
                turnState.currentTurnPlayerId = candidate;
                return true;
            }

            return false;
        }

        private bool HasCards(RoomInfo room, string playerId)
        {
            return room.Players.FirstOrDefault(p => string.Equals(p.playerId, playerId, StringComparison.Ordinal))?.cardCount > 0;
        }

        private bool AllAlivePlayersOutOfCards(RoomTurnState turnState, RoomInfo room)
        {
            var alivePlayerIds = turnState.turnOrderPlayerIds.Where(id => !IsPlayerDead(room, id)).ToList();
            if (alivePlayerIds.Count == 0)
            {
                return false;
            }

            return alivePlayerIds.All(id => !HasCards(room, id));
        }

        private RoundResetEvent? ResetRoundDeckAndHands(RoomInfo room, string roomId, string connectionId, string trigger, int? playId)
        {
            var turnState = room.TurnState;
            if (turnState == null)
            {
                return null;
            }

            if (playId.HasValue && turnState.lastRoundResetPlayId == playId.Value)
            {
                LogRoomAction(DateTime.Now.ToString("HH:mm:ss.fff"), connectionId, "-", roomId, "ROUND_RESET_REJECT_DUPLICATE", $"trigger={trigger}, playId={playId.Value}, reason=ALREADY_RESET_FOR_PLAY");
                return null;
            }

            var alivePlayers = room.Players.Where(p => !p.isDead).ToList();
            var cardsNeeded = alivePlayers.Count * CardsPerPlayer;
            var deck = BuildGameDeck();
            ShuffleDeck(deck, Random.Shared);

            if (deck.Count < cardsNeeded)
            {
                LogRoomAction(DateTime.Now.ToString("HH:mm:ss.fff"), connectionId, "-", roomId, "ROUND_RESET_FAILED", $"trigger={trigger}, playId={playId?.ToString() ?? "-"}, reason=DECK_NOT_ENOUGH, needed={cardsNeeded}, available={deck.Count}");
                return null;
            }

            var cursor = 0;
            var hands = new List<RoundResetHandInfo>(room.Players.Count);
            var deadPlayerIds = new List<string>();

            foreach (var player in room.Players)
            {
                if (player.isDead)
                {
                    player.cardCount = 0;
                    deadPlayerIds.Add(player.playerId);
                    hands.Add(new RoundResetHandInfo
                    {
                        playerId = player.playerId,
                        playerName = player.nickname,
                        nickname = player.nickname,
                        cards = new List<int>()
                    });
                    continue;
                }

                var cards = new List<int>(CardsPerPlayer);
                for (var i = 0; i < CardsPerPlayer; i++)
                {
                    cards.Add(deck[cursor++]);
                }

                player.cardCount = cards.Count;
                hands.Add(new RoundResetHandInfo
                {
                    playerId = player.playerId,
                    playerName = player.nickname,
                    nickname = player.nickname,
                    cards = cards
                });
            }

            turnState.destinyCard = Random.Shared.Next(0, 3);
            turnState.phase = TurnPhase.WaitingPlay;
            turnState.lastPlay = null;
            turnState.resolvingPlayId = null;
            if (playId.HasValue)
            {
                turnState.lastRoundResetPlayId = playId.Value;
            }

            turnState.pendingResponderIds.Clear();
            turnState.respondedIds.Clear();

            var aliveSummary = string.Join(",", room.Players
                .Where(p => !p.isDead)
                .Select(p => $"{p.playerId}:{p.cardCount}"));

            var handsSummary = string.Join(";", hands.Select(h => $"{h.playerId}=[{string.Join(',', h.cards)}]"));
            LogRoomAction(DateTime.Now.ToString("HH:mm:ss.fff"), connectionId, "-", roomId, "ROUND_RESET", $"trigger={trigger}, playId={playId?.ToString() ?? "-"}, destinyCard={turnState.destinyCard}, aliveCardCounts=[{aliveSummary}], hands={handsSummary}");

            return new RoundResetEvent
            {
                roomId = roomId,
                playId = playId,
                destinyCard = turnState.destinyCard,
                cardsPerPlayer = CardsPerPlayer,
                hands = hands,
                deadPlayerIds = deadPlayerIds
            };
        }

        private void BroadcastToRoom(string roomId, string type, object payload)
        {
            List<ClientConnection> targets;
            lock (_lock)
            {
                targets = GetRoomConnectionsSnapshotUnsafe(roomId);
            }

            foreach (var target in targets)
            {
                target.SendMessage(type, payload);
            }
        }

        private void SendToRoomExcept(string roomId, string exceptPlayerId, string type, object payload)
        {
            List<ClientConnection> targets;
            lock (_lock)
            {
                targets = GetRoomConnectionsSnapshotUnsafe(roomId);
            }

            foreach (var target in targets)
            {
                if (string.Equals(target.PlayerId, exceptPlayerId, StringComparison.Ordinal))
                {
                    continue;
                }

                target.SendMessage(type, payload);
            }
        }

        private string GetPlayerNameById(RoomInfo room, string playerId)
        {
            if (room == null || string.IsNullOrWhiteSpace(playerId))
            {
                return string.Empty;
            }

            return room.Players.FirstOrDefault(p => string.Equals(p.playerId, playerId, StringComparison.Ordinal))?.nickname ?? string.Empty;
        }

        private List<int> BuildGameDeck()
        {
            var deck = new List<int>(20);

            for (var i = 0; i < 6; i++)
            {
                deck.Add(0);
                deck.Add(1);
                deck.Add(2);
            }

            for (var i = 0; i < SpecialCardCount; i++)
            {
                deck.Add(SpecialCard);
            }

            return deck;
        }

        private void ShuffleDeck(List<int> deck, Random random)
        {
            for (var i = deck.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
        }

        private bool TryDealHands(List<string> players, List<int> deck, int cardsPerPlayer, out List<GameStartedHandInfo> hands, out string error)
        {
            hands = new List<GameStartedHandInfo>();
            error = string.Empty;

            var requiredCards = players.Count * cardsPerPlayer;
            if (deck.Count < requiredCards)
            {
                error = $"Deck not enough cards: required={requiredCards}, available={deck.Count}";
                return false;
            }

            var cursor = 0;
            foreach (var player in players)
            {
                var cards = new List<int>(cardsPerPlayer);
                for (var i = 0; i < cardsPerPlayer; i++)
                {
                    cards.Add(deck[cursor++]);
                }

                hands.Add(new GameStartedHandInfo
                {
                    playerName = player,
                    cards = cards
                });
            }

            return true;
        }

        private string FormatDeckSummary(List<int> deck)
        {
            if (deck.Count == 0)
            {
                return "[]";
            }

            var previewCount = Math.Min(12, deck.Count);
            var preview = string.Join(',', deck.Take(previewCount));
            if (deck.Count <= previewCount)
            {
                return $"[{preview}]";
            }

            return $"[{preview},... total={deck.Count}]";
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
                        playerId = p.playerId,
                        nickname = p.nickname,
                        playerName = p.nickname,
                        avatarId = ClampAvatar(p.avatarId),
                        cardCount = p.cardCount,
                        isDead = p.isDead
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

        private void SendError(ClientConnection connection, string code, string message)
        {
            var payload = new ErrorEvent
            {
                code = code,
                message = message
            };

            connection.SendMessage("ERROR", payload);
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
