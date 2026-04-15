using System.Text.Json;
using LiarNumberServer.Messages.ClientToServer;
using LiarNumberServer.Messages.ServerToClient;
using LiarNumberServer.Network;

namespace LiarNumberServer.Handlers
{
    public class LobbyHandler
    {
        // Map luu nickname -> playerId de tranh trung lap
        private readonly Dictionary<string, string> _activePlayers = new(); // nickname -> playerId
        private readonly object _lock = new();
        private readonly Random _random = new();
        private readonly int _avatarCount;

        public LobbyHandler(int avatarCount = 12)
        {
            _avatarCount = Math.Max(1, avatarCount);
        }

        // Xu ly message JoinLobby tu client
        public void HandleJoinLobby(string payloadJson, ClientConnection connection)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            
            try
            {
                // Log bat dau xu ly
                Console.WriteLine($"[{timestamp}] [Handler] [{connection.ConnectionId}] ========================================");
                Console.WriteLine($"[{timestamp}] [Handler] [{connection.ConnectionId}] BAT DAU XU LY JoinLobby");
                
                // Parse payload JSON thanh object JoinLobbyRequest
                var request = JsonSerializer.Deserialize<JoinLobbyRequest>(payloadJson);
                
                if (request == null)
                {
                    Console.WriteLine($"[{timestamp}] [Handler] [{connection.ConnectionId}] THAT BAI: Request null");
                    SendLobbyFailed(connection, "Invalid request format");
                    return;
                }

                Console.WriteLine($"[{timestamp}] [Handler] [{connection.ConnectionId}] Nickname yeu cau: '{request.nickname}'");

                // Validate nickname (khong rong, do dai 3-20 ky tu)
                var validationError = ValidateNickname(request.nickname);
                if (validationError != null)
                {
                    Console.WriteLine($"[{timestamp}] [Handler] [{connection.ConnectionId}] THAT BAI: {validationError}");
                    SendLobbyFailed(connection, validationError);
                    return;
                }

                // Kiem tra nickname da duoc su dung chua
                lock (_lock)
                {
                    if (_activePlayers.ContainsKey(request.nickname))
                    {
                        Console.WriteLine($"[{timestamp}] [Handler] [{connection.ConnectionId}] THAT BAI: Nickname da ton tai");
                        SendLobbyFailed(connection, "Nickname already taken");
                        return;
                    }

                    // Tao playerId moi (dung Guid, lay 8 ky tu dau)
                    var playerId = Guid.NewGuid().ToString().Substring(0, 8);
                    
                    var avatarId = _random.Next(0, _avatarCount);

                    // Luu thong tin vao map va connection
                    _activePlayers[request.nickname] = playerId;
                    connection.PlayerId = playerId;
                    connection.Nickname = request.nickname;
                    connection.AvatarId = avatarId;

                    Console.WriteLine($"[{timestamp}] [Handler] [{connection.ConnectionId}] Tao PlayerId: {playerId}");
                    Console.WriteLine($"[{timestamp}] [Handler] [{connection.ConnectionId}] Gan Nickname: {request.nickname}");
                    Console.WriteLine($"[{timestamp}] [Handler] [{connection.ConnectionId}] Random avatarId: {avatarId} (range 0..{_avatarCount - 1})");
                    Console.WriteLine($"[{timestamp}] [Handler] [{connection.ConnectionId}] Tong nguoi choi: {_activePlayers.Count}");
                }

                // Tao response LobbyJoined
                var response = new LobbyJoinedEvent
                {
                    playerId = connection.PlayerId,
                    nickname = connection.Nickname,
                    avatarId = connection.AvatarId,
                    roomId = null // Player chua vao room nao, se co sau khi CreateRoom hoac JoinRoom
                };

                // Log ket qua thanh cong
                Console.WriteLine($"[{timestamp}] [Handler] [{connection.ConnectionId}] JoinLobby THANH CONG");
                Console.WriteLine($"[{timestamp}] [Handler] [{connection.ConnectionId}] Response: playerId={response.playerId}, nickname={response.nickname}, avatarId={response.avatarId}, roomId={response.roomId ?? "null"}");
                Console.WriteLine($"[{timestamp}] [Handler] [{connection.ConnectionId}] ========================================");
                
                // Gui response ve client
                connection.SendMessage("LobbyJoined", response);
            }
            catch (Exception e)
            {
                // Log exception day du voi stack trace
                Console.WriteLine($"[{timestamp}] [Handler] [{connection.ConnectionId}] EXCEPTION: {e.Message}");
                Console.WriteLine($"[{timestamp}] [Handler] [{connection.ConnectionId}] Stack: {e.StackTrace}");
                Console.WriteLine($"[{timestamp}] [Handler] [{connection.ConnectionId}] ========================================");
                SendLobbyFailed(connection, "Internal server error");
            }
        }

        // Validate nickname theo yeu cau: khong rong, do dai 3-20 ky tu
        private string? ValidateNickname(string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname))
            {
                return "Nickname khong duoc rong";
            }

            if (nickname.Length < 3)
            {
                return "Nickname phai co it nhat 3 ky tu";
            }

            if (nickname.Length > 20)
            {
                return "Nickname khong duoc qua 20 ky tu";
            }

            return null; // Valid
        }

        // Gui message LobbyFailed ve client
        private void SendLobbyFailed(ClientConnection connection, string reason)
        {
            var response = new LobbyFailedEvent { reason = reason };
            connection.SendMessage("LobbyFailed", response);
        }

        // Xoa nguoi choi khoi danh sach (goi khi disconnect)
        public void RemovePlayer(string nickname)
        {
            lock (_lock)
            {
                if (_activePlayers.Remove(nickname))
                {
                    Console.WriteLine($"[LobbyHandler] Remove player: {nickname}, Con lai: {_activePlayers.Count}");
                }
            }
        }
    }
}
