using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class NetworkClient : Singleton<NetworkClient>
{
    [Header("Cau hinh server")]
    [SerializeField] private string serverHost = "192.168.1.55";
    [SerializeField] private int serverPort = 5555;

    private TcpClient _client;
    private NetworkStream _stream;
    private Thread _receiveThread;
    private volatile bool _connected;
    private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();

    // Dispatcher dung de route message theo type
    public MessageDispatcher Dispatcher { get; private set; }

    // Property de kiem tra trang thai ket noi
    public bool IsConnected => _connected;

    // Override Awake de khoi tao som hon Start
    private void Awake()
    {
        // Kiem tra instance truoc khi khoi tao
        if (Instant != null && Instant != this)
        {
            // Da co instance khac, huy object nay
            Debug.LogWarning("[NetworkClient] Instance da ton tai, huy duplicate");
            Destroy(gameObject);
            return;
        }

        // Day la instance duy nhat
        DontDestroyOnLoad(gameObject);
        
        // Khoi tao Dispatcher
        if (Dispatcher == null)
        {
            Dispatcher = new MessageDispatcher();
            Debug.Log("[NetworkClient] Da khoi tao Dispatcher");
        }

        // Tu dong tao LobbyMessageHandler neu chua co
        InitializeMessageHandlers();

        Debug.Log("[NetworkClient] Da khoi tao NetworkClient");
    }

    // Ham khoi tao cac message handler
    private void InitializeMessageHandlers()
    {
        // Tim hoac tao LobbyMessageHandler
        var lobbyHandler = FindObjectOfType<LobbyMessageHandler>();
        if (lobbyHandler == null)
        {
            var handlerObj = new GameObject("LobbyMessageHandler");
            // Dat lam child cua NetworkClient de cung DontDestroyOnLoad
            handlerObj.transform.SetParent(transform);
            lobbyHandler = handlerObj.AddComponent<LobbyMessageHandler>();
            Debug.Log("[NetworkClient] Da tu dong tao LobbyMessageHandler");
        }
        else
        {
            // Neu tim thay trong scene, di chuyen vao DontDestroyOnLoad
            if (lobbyHandler.transform.parent != transform)
            {
                lobbyHandler.transform.SetParent(transform);
                Debug.Log("[NetworkClient] Da di chuyen LobbyMessageHandler vao DontDestroyOnLoad");
            }
            else
            {
                Debug.Log("[NetworkClient] Da tim thay LobbyMessageHandler trong scene");
            }
        }
    }

    private void OnDestroy()
    {
        // Chi disconnect neu day la instance chinh
        if (Instant == this)
        {
            Disconnect();
        }
    }

    private void OnApplicationQuit()
    {
        // Dam bao disconnect khi thoat game
        Disconnect();
    }

    // Ham dung de ket noi toi server
    public void Connect()
    {
        if (_connected)
        {
            Debug.LogWarning("[NetworkClient] Da ket noi roi");
            return;
        }

        try
        {
            _client = new TcpClient();
            _client.Connect(serverHost, serverPort);
            _stream = _client.GetStream();
            _connected = true;

            // Tao thread nhan du lieu
            _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
            _receiveThread.Start();

            Debug.Log("[NetworkClient] Da ket noi den server " + serverHost + ":" + serverPort);
        }
        catch (Exception e)
        {
            Debug.LogError("[NetworkClient] Loi ket noi: " + e.Message);
            _connected = false;
        }
    }

    // Ham ngat ket noi
    public void Disconnect()
    {
        if (!_connected) return;

        _connected = false;

        try
        {
            _stream?.Close();
            _client?.Close();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[NetworkClient] Loi dong ket noi: " + e.Message);
        }

        // Doi thread ket thuc (toi da 1 giay)
        if (_receiveThread != null && _receiveThread.IsAlive)
        {
            if (!_receiveThread.Join(1000))
            {
                try
                {
                    _receiveThread.Abort();
                }
                catch
                {
                    // Bo qua loi abort
                }
            }
        }

        Debug.Log("[NetworkClient] Da ngat ket noi");
    }

    // Vong lap nhan du lieu tu server (chay tren thread rieng)
    private void ReceiveLoop()
    {
        using (var reader = new StreamReader(_stream, Encoding.UTF8))
        {
            try
            {
                while (_connected)
                {
                    var line = reader.ReadLine();
                    if (line == null)
                    {
                        Debug.Log("[NetworkClient] Server da dong ket noi");
                        break;
                    }

                    // Luu line de xu ly tren main thread
                    string lineToProcess = line;

                    // Day json sang dispatcher xu ly (tren thread chinh)
                    QueueOnMainThread(() =>
                    {
                        if (Dispatcher != null)
                        {
                            Dispatcher.Dispatch(lineToProcess);
                        }
                    });
                }
            }
            catch (Exception e)
            {
                if (_connected)
                {
                    Debug.LogError("[NetworkClient] Loi ReceiveLoop: " + e.Message);
                }
            }
        }

        _connected = false;
    }

    // Helper de chay action tren main thread
    private void QueueOnMainThread(Action action)
    {
        lock (_mainThreadQueue)
        {
            _mainThreadQueue.Enqueue(action);
        }
    }

    private void Update()
    {
        // Xu ly cac action tu background thread
        lock (_mainThreadQueue)
        {
            while (_mainThreadQueue.Count > 0)
            {
                _mainThreadQueue.Dequeue()?.Invoke();
            }
        }
    }

    // Gui mot chuoi json thuan toi server
    public void SendRaw(string jsonLine)
    {
        if (!_connected || _stream == null)
        {
            Debug.LogWarning("[NetworkClient] Chua ket noi, khong gui duoc");
            return;
        }

        try
        {
            var data = Encoding.UTF8.GetBytes(jsonLine + "\n");
            _stream.Write(data, 0, data.Length);
            _stream.Flush();

            Debug.Log("[NetworkClient] Da gui: " + jsonLine);
        }
        catch (Exception e)
        {
            Debug.LogError("[NetworkClient] Loi SendRaw: " + e.Message);
        }
    }

    // Gui message theo format NetworkMessage { type, payload }
    // Doi ten tu SendMessage thanh SendNetworkMessage de tranh conflict voi MonoBehaviour.SendMessage
    public void SendNetworkMessage(string type, object payload)
    {
        var wrapper = new NetworkMessage
        {
            type = type,
            payload = JsonUtility.ToJson(payload)
        };

        var json = JsonUtility.ToJson(wrapper);
        SendRaw(json);
    }
}
