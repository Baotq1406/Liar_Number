using System.Text.Json;
using LiarNumberServer.Messages;
using LiarNumberServer.Handlers;
using LiarNumberServer.Managers;

namespace LiarNumberServer.Network
{
    public class MessageRouter
    {
        private readonly Dictionary<string, Action<string, ClientConnection>> _handlers;
        private readonly LobbyHandler _lobbyHandler;
        private readonly RoomHandler _roomHandler;

        // Expose LobbyHandler de cho phep cleanup khi client disconnect
        public LobbyHandler LobbyHandler => _lobbyHandler;
        public RoomHandler RoomHandler => _roomHandler;

        public MessageRouter()
        {
            const int avatarCount = 12;
            _handlers = new Dictionary<string, Action<string, ClientConnection>>();
            _lobbyHandler = new LobbyHandler(avatarCount);
            _roomHandler = new RoomHandler(new RoomManager(), avatarCount);

            // Dang ky cac handler cho tung loai message
            RegisterHandler("JoinLobby", _lobbyHandler.HandleJoinLobby);
            RegisterHandler("CreateRoom", _roomHandler.HandleCreateRoom);
            RegisterHandler("JoinRoom", _roomHandler.HandleJoinRoom);
            RegisterHandler("LeaveRoom", _roomHandler.HandleLeaveRoom);
            RegisterHandler("CancelRoom", _roomHandler.HandleCancelRoom);
            RegisterHandler("StartGame", _roomHandler.HandleStartGame);
            RegisterHandler("Ready", HandleReady);
            RegisterHandler("PlayCard", HandlePlayCard);
            RegisterHandler("CallLiar", HandleCallLiar);
            RegisterHandler("RequestGameState", HandleRequestGameState);
        }

        // Dang ky handler cho 1 message type
        private void RegisterHandler(string type, Action<string, ClientConnection> handler)
        {
            _handlers[type] = handler;
            Console.WriteLine($"[Router] Registered handler for '{type}'");
        }

        // Route message den handler tuong ung
        public void Route(string jsonLine, ClientConnection connection)
        {
            try
            {
                // Parse JSON thanh BaseMessage de lay type va payload
                var msg = JsonSerializer.Deserialize<BaseMessage>(jsonLine);
                if (msg == null || string.IsNullOrEmpty(msg.type))
                {
                    Console.WriteLine($"[Router] [{connection.ConnectionId}] Message khong hop le: type rong hoac null");
                    return;
                }

                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                
                // Log chi tiet message nhan duoc
                Console.WriteLine($"[{timestamp}] [ROUTER] [{connection.ConnectionId}] Type: '{msg.type}'");
                Console.WriteLine($"[{timestamp}] [ROUTER] [{connection.ConnectionId}] Payload: {msg.payload}");

                // Tim handler tuong ung voi type
                if (_handlers.TryGetValue(msg.type, out var handler))
                {
                    Console.WriteLine($"[{timestamp}] [ROUTER] [{connection.ConnectionId}] Bat dau xu ly '{msg.type}'...");
                    
                    // Goi handler voi payload va connection
                    handler.Invoke(msg.payload, connection);
                }
                else
                {
                    Console.WriteLine($"[Router] [{connection.ConnectionId}] Khong co handler cho type: '{msg.type}'");
                }
            }
            catch (Exception e)
            {
                // Log loi voi stack trace day du
                Console.WriteLine($"[Router] [{connection.ConnectionId}] Loi route: {e.Message}");
                Console.WriteLine($"[Router] [{connection.ConnectionId}] Stack: {e.StackTrace}");
            }
        }

        // Cac handler tam thoi cho cac message chua implement
        
        private void HandleReady(string payloadJson, ClientConnection connection)
        {
            Console.WriteLine($"[Handler] [{connection.ConnectionId}] XU LY Ready (chua implement)");
            // TODO: Implement ready logic
        }

        private void HandlePlayCard(string payloadJson, ClientConnection connection)
        {
            Console.WriteLine($"[Handler] [{connection.ConnectionId}] XU LY PlayCard (chua implement)");
            // TODO: Implement play card logic
        }

        private void HandleCallLiar(string payloadJson, ClientConnection connection)
        {
            Console.WriteLine($"[Handler] [{connection.ConnectionId}] XU LY CallLiar (chua implement)");
            // TODO: Implement call liar logic
        }

        private void HandleRequestGameState(string payloadJson, ClientConnection connection)
        {
            Console.WriteLine($"[Handler] [{connection.ConnectionId}] XU LY RequestGameState (chua implement)");
            // TODO: Implement request game state logic
        }
    }
}
