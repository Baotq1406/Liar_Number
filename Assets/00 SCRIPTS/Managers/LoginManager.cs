using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LoginManager : MonoBehaviour
{
    [Header("Tham chieu UI")]
    [SerializeField] private TMP_InputField nameInput;  // O nhap ten (TextMeshPro)
    [SerializeField] private Button loginButton;        // Nut LOGIN
    [SerializeField] private GameObject panelNotify;    // Panel thong bao loi ket noi
    [SerializeField] private Button okButton;           // Nut OK trong panel notify

    private bool _isConnecting = false;

    private void Awake()
    {
        // Gan su kien click cho nut login
        if (loginButton != null)
        {
            loginButton.onClick.AddListener(OnLoginClicked);
        }

        // Gan su kien click cho nut OK
        if (okButton != null)
        {
            okButton.onClick.AddListener(HideNotifyPanel);
        }

        // An panel thong bao ban dau
        if (panelNotify != null)
        {
            panelNotify.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (loginButton != null)
        {
            loginButton.onClick.RemoveListener(OnLoginClicked);
        }

        if (okButton != null)
        {
            okButton.onClick.RemoveListener(HideNotifyPanel);
        }
    }

    private void Start()
    {
        // Dam bao NetworkClient ton tai trong scene
        if (NetworkClient.Instant == null)
        {
            Debug.LogError("[LoginManager] Khong tim thay NetworkClient trong scene!");
            Debug.LogError("[LoginManager] Vui long tao mot GameObject va gan script NetworkClient vao");
            ShowNotifyPanel();
            return;
        }

        Debug.Log("[LoginManager] LoginManager da san sang");
    }

    // Ham duoc goi khi nguoi choi bam nut LOGIN
    private void OnLoginClicked()
    {
        if (_isConnecting)
        {
            Debug.LogWarning("[LoginManager] Dang ket noi, vui long doi...");
            return;
        }

        // Kiem tra NetworkClient
        if (NetworkClient.Instant == null)
        {
            Debug.LogError("[LoginManager] NetworkClient chua san sang");
            ShowNotifyPanel();
            return;
        }

        // Lay nickname tu input field
        var nickname = nameInput != null ? nameInput.text.Trim() : string.Empty;
        
        // Kiem tra nickname rong
        if (string.IsNullOrEmpty(nickname))
        {
            Debug.LogWarning("[LoginManager] Ten dang nhap rong, vui long nhap ten");
            // TODO: hien thi thong bao loi len UI
            return;
        }

        // Kiem tra do dai nickname
        if (nickname.Length < 3 || nickname.Length > 20)
        {
            Debug.LogWarning("[LoginManager] Ten phai tu 3 den 20 ky tu");
            // TODO: hien thi thong bao loi len UI
            return;
        }

        // An panel thong bao neu dang hien
        if (panelNotify != null)
        {
            panelNotify.SetActive(false);
        }

        // Bat dau qua trinh dang nhap
        StartLogin(nickname);
    }

    private void StartLogin(string nickname)
    {
        _isConnecting = true;

        // Disable nut login de tranh spam
        if (loginButton != null)
        {
            loginButton.interactable = false;
        }

        Debug.Log("[LoginManager] Bat dau dang nhap voi nickname: " + nickname);

        // Neu chua ket noi thi ket noi truoc
        if (!NetworkClient.Instant.IsConnected)
        {
            NetworkClient.Instant.Connect();
            
            // Doi 1 giay de dam bao ket noi on dinh (trong thuc te nen dung callback)
            Invoke(nameof(SendJoinLobby), 1f);
        }
        else
        {
            // Da ket noi roi thi gui luon
            SendJoinLobby();
        }
    }

    private void SendJoinLobby()
    {
        // Kiem tra xem co ket noi thanh cong khong
        if (!NetworkClient.Instant.IsConnected)
        {
            Debug.LogError("[LoginManager] Ket noi that bai - khong the ket noi den server");
            ShowNotifyPanel();
            ResetLoginUI();
            return;
        }

        var nickname = nameInput != null ? nameInput.text.Trim() : string.Empty;
        var payload = new JoinLobbyRequest { nickname = nickname };
        
        // Sua lai: Dung SendNetworkMessage thay vi SendMessage
        NetworkClient.Instant.SendNetworkMessage("JoinLobby", payload);

        Debug.Log("[LoginManager] Da gui JoinLobby request");

        // Sau khi gui xong, enable lai nut login sau 2 giay
        Invoke(nameof(ResetLoginUI), 2f);
    }

    private void ResetLoginUI()
    {
        _isConnecting = false;

        // Enable lai nut login
        if (loginButton != null)
        {
            loginButton.interactable = true;
        }
    }

    // Hien thi panel thong bao loi
    private void ShowNotifyPanel()
    {
        if (panelNotify != null)
        {
            panelNotify.SetActive(true);
        }
    }

    // Ham public de an panel thong bao (co the goi tu nut Close tren panel)
    public void HideNotifyPanel()
    {
        if (panelNotify != null)
        {
            panelNotify.SetActive(false);
        }
    }

    // Ham ho tro: cho phep bam Enter trong input field de dang nhap
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (nameInput != null && nameInput.isFocused)
            {
                OnLoginClicked();
            }
        }
    }
}
