using System.Net;
using System.Net.Sockets;

namespace LiarNumberServer.Network
{
    public class GameServer
    {
        private readonly string _host;
        private readonly int _port;
        private TcpListener? _listener;
        private bool _isRunning;
        private readonly MessageRouter _router;
        private readonly List<ClientConnection> _clients = new();
        private readonly object _clientsLock = new();
        
        // Track connection IDs de tranh trung lap
        private readonly HashSet<string> _usedConnectionIds = new();

        public GameServer(string host, int port)
        {
            _host = host;
            _port = port;
            _router = new MessageRouter();
        }

        // Tao connection ID unique
        public string GenerateUniqueConnectionId()
        {
            lock (_clientsLock)
            {
                string connectionId;
                int attempts = 0;
                const int maxAttempts = 100;
                
                do
                {
                    connectionId = Guid.NewGuid().ToString().Substring(0, 8);
                    attempts++;
                    
                    if (attempts >= maxAttempts)
                    {
                        // Fallback: dung full GUID neu khong tim duoc ID unique sau 100 lan thu
                        connectionId = Guid.NewGuid().ToString();
                        Console.WriteLine($"[Server] WARNING: Phai dung full GUID lam ConnectionId");
                        break;
                    }
                } 
                while (_usedConnectionIds.Contains(connectionId));
                
                _usedConnectionIds.Add(connectionId);
                return connectionId;
            }
        }

        // Giai phong connection ID khi client disconnect
        public void ReleaseConnectionId(string connectionId)
        {
            lock (_clientsLock)
            {
                _usedConnectionIds.Remove(connectionId);
            }
        }

        public void Start()
        {
            try
            {
                // Tao TCP listener
                _listener = new TcpListener(IPAddress.Parse(_host), _port);
                _listener.Start();
                _isRunning = true;

                Console.WriteLine($"[Server] Listening on {_host}:{_port}");
                Console.WriteLine($"[Server] Ready to accept clients...");

                // Bat dau thread lang nghe ket noi
                var acceptThread = new Thread(AcceptClients) { IsBackground = true };
                acceptThread.Start();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Server] Loi start: {e.Message}");
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();

            // Ngat tat ca client
            lock (_clientsLock)
            {
                foreach (var client in _clients)
                {
                    client.Disconnect();
                }
                _clients.Clear();
            }

            Console.WriteLine("[Server] Stopped");
        }

        private void AcceptClients()
        {
            // Vong lap lang nghe ket noi client moi
            while (_isRunning)
            {
                try
                {
                    // Cho client ket noi (blocking call)
                    var tcpClient = _listener!.AcceptTcpClient();
                    var endpoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";
                    
                    // Tao object ClientConnection dai dien cho client nay
                    var connection = new ClientConnection(tcpClient, _router, this);
                    
                    lock (_clientsLock)
                    {
                        _clients.Add(connection);
                        
                        // Log chi tiet ket noi moi
                        Console.WriteLine($"[Server] ===================================");
                        Console.WriteLine($"[Server] CLIENT KET NOI");
                        Console.WriteLine($"[Server] IP:Port   : {endpoint}");
                        Console.WriteLine($"[Server] Connection: {connection.ConnectionId}");
                        Console.WriteLine($"[Server] Tong client: {_clients.Count}");
                        Console.WriteLine($"[Server] ===================================");
                    }
                }
                catch (Exception e)
                {
                    // Chi log loi neu server van dang chay (khong phai do Stop())
                    if (_isRunning)
                    {
                        Console.WriteLine($"[Server] Loi accept: {e.Message}");
                    }
                }
            }
        }

        // Goi khi client ngat ket noi de xoa khoi danh sach
        public void RemoveClient(ClientConnection connection)
        {
            lock (_clientsLock)
            {
                _clients.Remove(connection);
                
                // Log chi tiet ngat ket noi
                Console.WriteLine($"[Server] ===================================");
                Console.WriteLine($"[Server] CLIENT NGAT KET NOI");
                Console.WriteLine($"[Server] Connection: {connection.ConnectionId}");
                Console.WriteLine($"[Server] Player    : {connection.GetClientInfo()}");
                Console.WriteLine($"[Server] Con lai   : {_clients.Count} client");
                Console.WriteLine($"[Server] ===================================");
            }
        }
    }
}
