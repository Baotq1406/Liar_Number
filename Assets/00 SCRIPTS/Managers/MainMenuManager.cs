using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Manager cho Main Menu - hien thi ten nguoi choi
public class MainMenuManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text playerNameText;  // Text hien thi "Hello, [name]"
    [SerializeField] private GameObject JoinPanel; // Panel nhap code phong
    [SerializeField] private TMP_InputField joinRoomCodeInput;
    [SerializeField] private Button joinRoomButton;
    [SerializeField] private TMP_Text joinStatusText;
    [SerializeField] private bool autoConnectIfNeeded = true;
    [SerializeField] private float joinRoomTimeoutSeconds = 8f;

    private bool _isJoiningRoom;

    private void Awake()
    {
        if (joinRoomButton != null)
        {
            joinRoomButton.onClick.RemoveListener(OnJoinRoomButtonClicked);
            joinRoomButton.onClick.AddListener(OnJoinRoomButtonClicked);
        }
    }

    private void Start()
    {
        // Hien thi ten nguoi choi tu GameManager
        if (JoinPanel != null)
        {
            JoinPanel.SetActive(false);
        }

        if (joinStatusText != null)
        {
            joinStatusText.text = string.Empty;
        }

        DisplayPlayerName();
        
        Debug.Log("[MainMenuManager] Main Menu da san sang");
    }

    private void OnEnable()
    {
        RoomMessageHandler.JoinRoomFailedReceived += OnJoinRoomFailedReceived;
    }

    private void OnDisable()
    {
        RoomMessageHandler.JoinRoomFailedReceived -= OnJoinRoomFailedReceived;
        CancelInvoke(nameof(OnJoinRoomTimeout));
    }

    private void OnDestroy()
    {
        if (joinRoomButton != null)
        {
            joinRoomButton.onClick.RemoveListener(OnJoinRoomButtonClicked);
        }
    }

    // Hien thi ten nguoi choi
    private void DisplayPlayerName()
    {
        if (GameManager.Instant == null)
        {
            Debug.LogError("[MainMenuManager] GameManager khong ton tai!");
            
            // Hien thi text mac dinh
            if (playerNameText != null)
            {
                playerNameText.text = "Hello, Player";
            }
            return;
        }

        if (string.IsNullOrEmpty(GameManager.Instant.Nickname))
        {
            Debug.LogWarning("[MainMenuManager] Nickname trong GameManager rong!");
            
            // Hien thi text mac dinh
            if (playerNameText != null)
            {
                playerNameText.text = "Hello, Player";
            }
            return;
        }

        // Hien thi ten nguoi choi
        if (playerNameText != null)
        {
            playerNameText.text = "Hello, " + GameManager.Instant.Nickname;
            Debug.Log("[MainMenuManager] Da hien thi ten: " + GameManager.Instant.Nickname);
        }
        else
        {
            Debug.LogError("[MainMenuManager] PlayerNameText chua duoc gan trong Inspector!");
        }
    }

    public void DisplayJoinPanel()
    {
        // Hien thi panel nhap code phong
        if (JoinPanel != null)
        {
            JoinPanel.SetActive(true);
        }

        if (joinStatusText != null)
        {
            joinStatusText.text = string.Empty;
        }
    }

    public void HideJoinPanel()
    {
        // An panel nhap code phong
        if (JoinPanel != null)
        {
            JoinPanel.SetActive(false);
        }

        _isJoiningRoom = false;
        SetJoinButtonInteractable(true);

        if (joinStatusText != null)
        {
            joinStatusText.text = string.Empty;
        }
    }

    public void OnExitButtonClicked()
    {
        // Thoat game
        Debug.Log("[MainMenuManager] Exit button clicked, thoat game");
        Application.Quit();
    }

    // Nut HOST trong MainMenu
    public void OnHostButtonClicked()
    {
        if (NetworkClient.Instant == null)
        {
            Debug.LogError("[MainMenuManager] NetworkClient khong ton tai!");
            return;
        }

        if (GameManager.Instant == null || string.IsNullOrEmpty(GameManager.Instant.PlayerId))
        {
            Debug.LogError("[MainMenuManager] Chua co thong tin nguoi choi, khong the host room");
            return;
        }

        if (!NetworkClient.Instant.IsConnected)
        {
            if (!autoConnectIfNeeded)
            {
                Debug.LogError("[MainMenuManager] Chua ket noi server");
                return;
            }

            NetworkClient.Instant.Connect();
            if (!NetworkClient.Instant.IsConnected)
            {
                Debug.LogError("[MainMenuManager] Ket noi server that bai, khong the host room");
                return;
            }
        }

        var payload = new CreateRoomRequest
        {
            playerId = GameManager.Instant.PlayerId,
            nickname = GameManager.Instant.Nickname
        };

        NetworkClient.Instant.SendNetworkMessage("CreateRoom", payload);
        Debug.Log("[MainMenuManager] Da gui yeu cau CreateRoom");
    }

    public void OnJoinRoomButtonClicked()
    {
        Debug.Log("[MainMenuManager] JoinRoom button clicked");

        if (_isJoiningRoom)
        {
            return;
        }

        if (GameManager.Instant == null || string.IsNullOrEmpty(GameManager.Instant.PlayerId))
        {
            SetJoinStatus("Chua dang nhap");
            return;
        }

        var roomCode = joinRoomCodeInput != null ? joinRoomCodeInput.text.Trim() : string.Empty;
        if (!IsValidRoomCode(roomCode))
        {
            SetJoinStatus("Ma phong phai gom 4 so");
            return;
        }

        if (NetworkClient.Instant == null)
        {
            SetJoinStatus("NetworkClient khong ton tai");
            return;
        }

        if (!NetworkClient.Instant.IsConnected)
        {
            if (!autoConnectIfNeeded)
            {
                SetJoinStatus("Chua ket noi server");
                return;
            }

            NetworkClient.Instant.Connect();
            if (!NetworkClient.Instant.IsConnected)
            {
                SetJoinStatus("Ket noi server that bai");
                return;
            }
        }

        var payload = new JoinRoomRequest
        {
            roomId = roomCode,
            playerId = GameManager.Instant.PlayerId,
            nickname = GameManager.Instant.Nickname
        };

        _isJoiningRoom = true;
        SetJoinButtonInteractable(false);
        SetJoinStatus("Dang vao phong...");

        NetworkClient.Instant.SendNetworkMessage("JoinRoom", payload);
        CancelInvoke(nameof(OnJoinRoomTimeout));
        Invoke(nameof(OnJoinRoomTimeout), joinRoomTimeoutSeconds);
        Debug.Log("[MainMenuManager] Da gui yeu cau JoinRoom: " + roomCode);
    }

    private void OnJoinRoomFailedReceived(string payloadJson)
    {
        CancelInvoke(nameof(OnJoinRoomTimeout));
        _isJoiningRoom = false;
        SetJoinButtonInteractable(true);
        SetJoinStatus("Khong vao duoc phong");
        Debug.LogError("[MainMenuManager] JoinRoom that bai: " + payloadJson);
    }

    private void OnJoinRoomTimeout()
    {
        if (!_isJoiningRoom)
        {
            return;
        }

        _isJoiningRoom = false;
        SetJoinButtonInteractable(true);
        SetJoinStatus("Join timeout, thu lai");
        Debug.LogWarning("[MainMenuManager] JoinRoom timeout, khong nhan duoc phan hoi tu server");
    }

    private static bool IsValidRoomCode(string roomCode)
    {
        if (string.IsNullOrEmpty(roomCode) || roomCode.Length != 4)
        {
            return false;
        }

        for (int i = 0; i < roomCode.Length; i++)
        {
            if (!char.IsDigit(roomCode[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void SetJoinButtonInteractable(bool value)
    {
        if (joinRoomButton != null)
        {
            joinRoomButton.interactable = value;
        }
    }

    private void SetJoinStatus(string message)
    {
        if (joinStatusText != null)
        {
            joinStatusText.text = message;
        }
    }
}
