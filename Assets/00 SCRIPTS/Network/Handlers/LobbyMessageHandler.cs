using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// DTO cho payload JoinLobby tu client len server
[Serializable]
public class JoinLobbyRequest
{
    public string nickname;
}

// DTO cho payload LobbyJoined tu server ve client
[Serializable]
public class LobbyJoinedPayload
{
    public string playerId;
    public string nickname;
    // BO roomId - user moi dang nhap chua co room
}

public class LobbyMessageHandler : MonoBehaviour
{
    [Header("Cau hinh")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";  // Scene chuyen den sau khi login

    private bool _isRegistered = false;

    private void Awake()
    {
        // Dang ky ngay khi Awake (som hon OnEnable)
        RegisterHandlers();
    }

    private void OnEnable()
    {
        // Dang ky lai neu chua dang ky
        if (!_isRegistered)
        {
            RegisterHandlers();
        }
    }

    private void RegisterHandlers()
    {
        // Cho NetworkClient san sang
        if (NetworkClient.Instant == null || NetworkClient.Instant.Dispatcher == null)
        {
            Debug.LogWarning("[LobbyMessageHandler] NetworkClient chua san sang, doi 0.1s...");
            Invoke(nameof(RegisterHandlers), 0.1f);
            return;
        }

        if (_isRegistered)
        {
            Debug.Log("[LobbyMessageHandler] Handler da duoc dang ky truoc do");
            return;
        }

        NetworkClient.Instant.Dispatcher.RegisterHandler("LobbyJoined", OnLobbyJoined);
        NetworkClient.Instant.Dispatcher.RegisterHandler("LobbyFailed", OnLobbyFailed);
        _isRegistered = true;
        
        Debug.Log("[LobbyMessageHandler] Da dang ky handler thanh cong");
    }

    private void OnDisable()
    {
        // Huy dang ky khi object bi disable
        if (_isRegistered && NetworkClient.Instant != null && NetworkClient.Instant.Dispatcher != null)
        {
            NetworkClient.Instant.Dispatcher.UnregisterHandler("LobbyJoined", OnLobbyJoined);
            NetworkClient.Instant.Dispatcher.UnregisterHandler("LobbyFailed", OnLobbyFailed);
            _isRegistered = false;
        }
    }

    // Ham duoc goi khi nhan message type = "LobbyJoined"
    private void OnLobbyJoined(string payloadJson)
    {
        Debug.Log("[LobbyMessageHandler] OnLobbyJoined duoc goi! Payload: " + payloadJson);
        
        try
        {
            var payload = JsonUtility.FromJson<LobbyJoinedPayload>(payloadJson);
            
            Debug.Log($"[LobbyMessageHandler] Parse thanh cong! PlayerId={payload.playerId}, Nickname={payload.nickname}");

            // Luu thong tin nguoi choi vao GameManager (CHUA CO roomId)
            if (GameManager.Instant != null)
            {
                GameManager.Instant.SetPlayerInfo(payload.playerId, payload.nickname);
                Debug.Log("[LobbyMessageHandler] Da luu thong tin vao GameManager");
            }
            else
            {
                Debug.LogError("[LobbyMessageHandler] GameManager khong ton tai!");
                return;
            }

            // Chuyen sang scene MainMenu
            Debug.Log($"[LobbyMessageHandler] Bat dau chuyen sang scene '{mainMenuSceneName}'...");
            SceneManager.LoadScene(mainMenuSceneName);
            Debug.Log("[LobbyMessageHandler] Da goi SceneManager.LoadScene()");
        }
        catch (Exception e)
        {
            Debug.LogError("[LobbyMessageHandler] Loi parse LobbyJoined: " + e.Message);
            Debug.LogError("[LobbyMessageHandler] Stack trace: " + e.StackTrace);
        }
    }

    // Ham duoc goi khi dang nhap that bai
    private void OnLobbyFailed(string payloadJson)
    {
        Debug.LogError("[LobbyMessageHandler] Dang nhap that bai: " + payloadJson);
        
        // TODO: hien thi thong bao loi len UI
    }
}
