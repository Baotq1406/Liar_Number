using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable]
public class CreateRoomRequest
{
    public string playerId;
    public string nickname;
}

[Serializable]
public class RoomCreatedPayload
{
    public string roomId;
    public string hostId;
    public string hostNickname;
}

[Serializable]
public class JoinRoomRequest
{
    public string roomId;
    public string playerId;
    public string nickname;
}

[Serializable]
public class RoomJoinedPayload
{
    public string roomId;
    public string hostId;
    public string hostNickname;
}

[Serializable]
public class RoomPlayersUpdatedPayload
{
    public string roomId;
    public List<string> players;
}

[Serializable]
public class RoomClosedPayload
{
    public string roomId;
    public string reason;
    public string closedBy;
}

public class RoomMessageHandler : MonoBehaviour
{
    public static event Action<string> JoinRoomFailedReceived;

    [Header("Cau hinh")]
    [SerializeField] private string roomSceneName = "RoomScene";
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private string fallbackMainMenuSceneName = "MainMenuScene";

    private bool _isRegistered;

    private void Awake()
    {
        RegisterHandlers();
    }

    private void OnEnable()
    {
        if (!_isRegistered)
        {
            RegisterHandlers();
        }
    }

    private void RegisterHandlers()
    {
        if (NetworkClient.Instant == null || NetworkClient.Instant.Dispatcher == null)
        {
            Debug.LogWarning("[RoomMessageHandler] NetworkClient chua san sang, doi 0.1s...");
            Invoke(nameof(RegisterHandlers), 0.1f);
            return;
        }

        if (_isRegistered)
        {
            return;
        }

        NetworkClient.Instant.Dispatcher.RegisterHandler("RoomCreated", OnRoomCreated);
        NetworkClient.Instant.Dispatcher.RegisterHandler("RoomJoined", OnRoomJoined);
        NetworkClient.Instant.Dispatcher.RegisterHandler("JoinRoomFailed", OnJoinRoomFailed);
        NetworkClient.Instant.Dispatcher.RegisterHandler("RoomPlayersUpdated", OnRoomPlayersUpdated);
        NetworkClient.Instant.Dispatcher.RegisterHandler("CreateRoomFailed", OnCreateRoomFailed);
        NetworkClient.Instant.Dispatcher.RegisterHandler("RoomCreateFailed", OnCreateRoomFailed);
        NetworkClient.Instant.Dispatcher.RegisterHandler("RoomClosed", OnRoomClosed);
        NetworkClient.Instant.Dispatcher.RegisterHandler("CancelRoomFailed", OnCancelRoomFailed);
        _isRegistered = true;

        Debug.Log("[RoomMessageHandler] Da dang ky handler cho room message");
    }

    private void OnDisable()
    {
        if (!_isRegistered || NetworkClient.Instant == null || NetworkClient.Instant.Dispatcher == null)
        {
            return;
        }

        NetworkClient.Instant.Dispatcher.UnregisterHandler("RoomCreated", OnRoomCreated);
        NetworkClient.Instant.Dispatcher.UnregisterHandler("RoomJoined", OnRoomJoined);
        NetworkClient.Instant.Dispatcher.UnregisterHandler("JoinRoomFailed", OnJoinRoomFailed);
        NetworkClient.Instant.Dispatcher.UnregisterHandler("RoomPlayersUpdated", OnRoomPlayersUpdated);
        NetworkClient.Instant.Dispatcher.UnregisterHandler("CreateRoomFailed", OnCreateRoomFailed);
        NetworkClient.Instant.Dispatcher.UnregisterHandler("RoomCreateFailed", OnCreateRoomFailed);
        NetworkClient.Instant.Dispatcher.UnregisterHandler("RoomClosed", OnRoomClosed);
        NetworkClient.Instant.Dispatcher.UnregisterHandler("CancelRoomFailed", OnCancelRoomFailed);
        _isRegistered = false;
    }

    private void OnRoomCreated(string payloadJson)
    {
        Debug.Log("[RoomMessageHandler] Nhan RoomCreated: " + payloadJson);

        try
        {
            var payload = JsonUtility.FromJson<RoomCreatedPayload>(payloadJson);
            if (payload == null || string.IsNullOrEmpty(payload.roomId))
            {
                Debug.LogError("[RoomMessageHandler] Payload RoomCreated khong hop le");
                return;
            }

            if (GameManager.Instant == null)
            {
                Debug.LogError("[RoomMessageHandler] GameManager khong ton tai!");
                return;
            }

            GameManager.Instant.SetRoomId(payload.roomId);
            GameManager.Instant.SetRoomHostInfo(payload.hostId, payload.hostNickname);

            var players = new List<string>();
            if (!string.IsNullOrEmpty(payload.hostNickname))
            {
                players.Add(payload.hostNickname);
            }
            GameManager.Instant.SetRoomPlayers(players);

            SceneManager.LoadScene(roomSceneName);
        }
        catch (Exception e)
        {
            Debug.LogError("[RoomMessageHandler] Loi parse RoomCreated: " + e.Message);
        }
    }

    private void OnCreateRoomFailed(string payloadJson)
    {
        Debug.LogError("[RoomMessageHandler] Tao room that bai: " + payloadJson);
    }

    private void OnRoomJoined(string payloadJson)
    {
        Debug.Log("[RoomMessageHandler] Nhan RoomJoined: " + payloadJson);

        try
        {
            var payload = JsonUtility.FromJson<RoomJoinedPayload>(payloadJson);
            if (payload == null || string.IsNullOrEmpty(payload.roomId))
            {
                Debug.LogError("[RoomMessageHandler] Payload RoomJoined khong hop le");
                return;
            }

            if (GameManager.Instant == null)
            {
                Debug.LogError("[RoomMessageHandler] GameManager khong ton tai!");
                return;
            }

            GameManager.Instant.SetRoomId(payload.roomId);
            GameManager.Instant.SetRoomHostInfo(payload.hostId, payload.hostNickname);

            var players = new List<string>();
            if (!string.IsNullOrEmpty(payload.hostNickname))
            {
                players.Add(payload.hostNickname);
            }

            if (!string.IsNullOrEmpty(GameManager.Instant.Nickname) &&
                (string.IsNullOrEmpty(payload.hostNickname) || !string.Equals(GameManager.Instant.Nickname, payload.hostNickname, StringComparison.Ordinal)))
            {
                players.Add(GameManager.Instant.Nickname);
            }

            GameManager.Instant.SetRoomPlayers(players);
            SceneManager.LoadScene(roomSceneName);
        }
        catch (Exception e)
        {
            Debug.LogError("[RoomMessageHandler] Loi parse RoomJoined: " + e.Message);
        }
    }

    private void OnJoinRoomFailed(string payloadJson)
    {
        Debug.LogError("[RoomMessageHandler] Join room that bai: " + payloadJson);
        JoinRoomFailedReceived?.Invoke(payloadJson);
    }

    private void OnRoomPlayersUpdated(string payloadJson)
    {
        Debug.Log("[RoomMessageHandler] Nhan RoomPlayersUpdated: " + payloadJson);

        try
        {
            var payload = JsonUtility.FromJson<RoomPlayersUpdatedPayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            if (GameManager.Instant != null)
            {
                GameManager.Instant.SetRoomPlayers(payload.players);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[RoomMessageHandler] Loi parse RoomPlayersUpdated: " + e.Message);
        }
    }

    private void OnRoomClosed(string payloadJson)
    {
        Debug.Log("[RoomMessageHandler] Nhan RoomClosed: " + payloadJson);

        try
        {
            var payload = JsonUtility.FromJson<RoomClosedPayload>(payloadJson);
            if (GameManager.Instant != null)
            {
                GameManager.Instant.ClearRoomId();
            }

            if (!string.IsNullOrEmpty(payload?.reason))
            {
                Debug.Log("[RoomMessageHandler] Ly do dong room: " + payload.reason);
            }

            LoadMainMenuSceneSafely();
        }
        catch (Exception e)
        {
            Debug.LogError("[RoomMessageHandler] Loi parse RoomClosed: " + e.Message);
            LoadMainMenuSceneSafely();
        }
    }

    private void OnCancelRoomFailed(string payloadJson)
    {
        Debug.LogError("[RoomMessageHandler] Huy room that bai: " + payloadJson);
    }

    private void LoadMainMenuSceneSafely()
    {
        if (CanLoadScene(mainMenuSceneName))
        {
            SceneManager.LoadScene(mainMenuSceneName);
            return;
        }

        if (CanLoadScene(fallbackMainMenuSceneName))
        {
            Debug.LogWarning("[RoomMessageHandler] Scene main menu chinh khong ton tai, dung fallback: " + fallbackMainMenuSceneName);
            SceneManager.LoadScene(fallbackMainMenuSceneName);
            return;
        }

        Debug.LogError("[RoomMessageHandler] Khong tim thay scene main menu trong Build Settings. Vui long them scene vao Build Settings hoac sua ten scene trong Inspector.");
    }

    private static bool CanLoadScene(string sceneName)
    {
        return !string.IsNullOrEmpty(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName);
    }
}
