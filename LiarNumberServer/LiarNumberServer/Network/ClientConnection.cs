using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LiarNumberServer.Network
{
    public class ClientConnection
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly MessageRouter _router;
        private readonly Thread _receiveThread;
        private bool _isConnected;
        private readonly string _clientEndpoint;
        private readonly GameServer _server; // Reference to server for cleanup

        // Thong tin nguoi choi (duoc gan sau khi login)
        public string? PlayerId { get; set; }
        public string? Nickname { get; set; }
        public string? CurrentRoomId { get; set; }
        
        // ID ket noi unique de phan biet log
        public string ConnectionId { get; }

        public ClientConnection(TcpClient client, MessageRouter router, GameServer server)
        {
            _client = client;
            _stream = client.GetStream();
            _router = router;
            _server = server;
            _isConnected = true;
            
            // Tao connection ID unique (khong trung lap)
            ConnectionId = server.GenerateUniqueConnectionId();
            _clientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";

            Console.WriteLine($"[ClientConnection] [{ConnectionId}] Created for {_clientEndpoint}");

            // Bat dau thread nhan du lieu tu client
            _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
            _receiveThread.Start();
        }

        private void ReceiveLoop()
        {
            // Dung StreamReader de doc line-based JSON
            using (var reader = new StreamReader(_stream, Encoding.UTF8))
            {
                try
                {
                    while (_isConnected)
                    {
                        // Doc 1 dong JSON
                        var line = reader.ReadLine();
                        if (line == null)
                        {
                            // Client ngat ket noi
                            Console.WriteLine($"[ClientConnection] [{ConnectionId}] Client ngat ket noi (EOF)");
                            break;
                        }

                        // Log message nhan duoc voi timestamp
                        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                        Console.WriteLine($"[{timestamp}] [RECV] [{ConnectionId}] {line}");

                        // Route message den handler tuong ung
                        _router.Route(line, this);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[ClientConnection] [{ConnectionId}] Loi ReceiveLoop: {e.Message}");
                }
            }

            // Dong ket noi khi thoat loop
            Disconnect();
        }

        // Gui raw JSON line toi client
        public void SendRaw(string jsonLine)
        {
            if (!_isConnected) return;

            try
            {
                // Them \n de client doc duoc line
                var data = Encoding.UTF8.GetBytes(jsonLine + "\n");
                _stream.Write(data, 0, data.Length);
                _stream.Flush();

                // Log message gui di voi timestamp
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                Console.WriteLine($"[{timestamp}] [SEND] [{ConnectionId}] {jsonLine}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ClientConnection] [{ConnectionId}] Loi SendRaw: {e.Message}");
            }
        }

        // Gui message co type va payload (tu dong serialize)
        public void SendMessage(string type, object payload)
        {
            // Tao wrapper message voi type va payload
            var wrapper = new Messages.BaseMessage
            {
                type = type,
                payload = JsonSerializer.Serialize(payload)
            };

            var json = JsonSerializer.Serialize(wrapper);
            
            // Log truoc khi gui
            Console.WriteLine($"[ClientConnection] [{ConnectionId}] Gui message type='{type}' toi client '{Nickname ?? "Unknown"}'");
            SendRaw(json);
        }

        // Ngat ket noi voi client
        public void Disconnect()
        {
            if (!_isConnected) return;

            _isConnected = false;
            
            // Log thong tin truoc khi dong
            Console.WriteLine($"[ClientConnection] [{ConnectionId}] Ngat ket noi - PlayerId: {PlayerId ?? "None"}, Nickname: {Nickname ?? "None"}");

            _router.RoomHandler.HandleDisconnect(this);
            
            // Cleanup lobby handler neu co nickname
            if (!string.IsNullOrEmpty(Nickname))
            {
                _router.LobbyHandler.RemovePlayer(Nickname);
            }
            
            // Giai phong ConnectionId de co the tai su dung
            _server.ReleaseConnectionId(ConnectionId);
            
            _stream?.Close();
            _client?.Close();
        }

        // Lay thong tin client de hien thi trong log
        public string GetClientInfo()
        {
            return $"{Nickname ?? "Unknown"}({PlayerId ?? ConnectionId}) @ {_clientEndpoint}";
        }
    }
}
