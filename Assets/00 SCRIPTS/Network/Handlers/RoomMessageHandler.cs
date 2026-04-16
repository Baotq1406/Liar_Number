using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable]
public class CreateRoomRequest
{
    public string playerId;
    public string nickname;
    public int avatarId;
}

[Serializable]
public class RoomCreatedPayload
{
    public string roomId;
    public string hostId;
    public string hostNickname;
    public int hostAvatarId;
}

[Serializable]
public class JoinRoomRequest
{
    public string roomId;
    public string playerId;
    public string nickname;
    public int avatarId;
}

[Serializable]
public class RoomJoinedPayload
{
    public string roomId;
    public string hostId;
    public string hostNickname;
    public int hostAvatarId;
}

[Serializable]
public class RoomPlayersUpdatedPayload
{
    public string roomId;
    public List<string> players;
}

[Serializable]
public class PlayerJoinedPayload
{
    public string roomId;
    public string playerId;
    public string nickname;
    public string playerName;
    public int avatarId;
    public int cardCount;
    public bool isDead;
}

[Serializable]
public class RoomStatePayload
{
    public string roomId;
    public List<RoomPlayerState> players;
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
    [SerializeField] private string gameSceneName = "GameScene";
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
        NetworkClient.Instant.Dispatcher.RegisterHandler("PlayerJoined", OnPlayerJoined);
        NetworkClient.Instant.Dispatcher.RegisterHandler("RoomState", OnRoomState);
        NetworkClient.Instant.Dispatcher.RegisterHandler("CreateRoomFailed", OnCreateRoomFailed);
        NetworkClient.Instant.Dispatcher.RegisterHandler("RoomCreateFailed", OnCreateRoomFailed);
        NetworkClient.Instant.Dispatcher.RegisterHandler("RoomClosed", OnRoomClosed);
        NetworkClient.Instant.Dispatcher.RegisterHandler("CancelRoomFailed", OnCancelRoomFailed);
        NetworkClient.Instant.Dispatcher.RegisterHandler("GameStarted", OnGameStarted);
        NetworkClient.Instant.Dispatcher.RegisterHandler("GAME_STARTED", OnGameStarted);
        NetworkClient.Instant.Dispatcher.RegisterHandler("StartGameFailed", OnStartGameFailed);
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
        NetworkClient.Instant.Dispatcher.UnregisterHandler("PlayerJoined", OnPlayerJoined);
        NetworkClient.Instant.Dispatcher.UnregisterHandler("RoomState", OnRoomState);
        NetworkClient.Instant.Dispatcher.UnregisterHandler("CreateRoomFailed", OnCreateRoomFailed);
        NetworkClient.Instant.Dispatcher.UnregisterHandler("RoomCreateFailed", OnCreateRoomFailed);
        NetworkClient.Instant.Dispatcher.UnregisterHandler("RoomClosed", OnRoomClosed);
        NetworkClient.Instant.Dispatcher.UnregisterHandler("CancelRoomFailed", OnCancelRoomFailed);
        NetworkClient.Instant.Dispatcher.UnregisterHandler("GameStarted", OnGameStarted);
        NetworkClient.Instant.Dispatcher.UnregisterHandler("GAME_STARTED", OnGameStarted);
        NetworkClient.Instant.Dispatcher.UnregisterHandler("StartGameFailed", OnStartGameFailed);
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

            if (string.IsNullOrEmpty(payload.hostNickname))
            {
                Debug.LogWarning("[RoomMessageHandler] RoomCreated thieu hostNickname, UI co the hien thi fallback.");
            }

            if (payload.hostAvatarId < 0)
            {
                Debug.LogWarning("[RoomMessageHandler] hostAvatarId am trong RoomCreated, fallback ve 0. host=" + payload.hostNickname + ", avatarId=" + payload.hostAvatarId);
                payload.hostAvatarId = 0;
            }

            if (GameManager.Instant == null)
            {
                Debug.LogError("[RoomMessageHandler] GameManager khong ton tai!");
                return;
            }

            GameManager.Instant.SetRoomId(payload.roomId);
            GameManager.Instant.SetRoomHostInfo(payload.hostId, payload.hostNickname);

            if (!string.IsNullOrEmpty(payload.hostNickname))
            {
                GameManager.Instant.OnPlayerJoined(new RoomPlayerState
                {
                    playerName = payload.hostNickname,
                    avatarId = Mathf.Max(0, payload.hostAvatarId)
                });
            }

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

    private void OnPlayerJoined(string payloadJson)
    {
        Debug.Log("[RoomMessageHandler] Nhan PlayerJoined: " + payloadJson);

        try
        {
            var payload = JsonUtility.FromJson<PlayerJoinedPayload>(payloadJson);
            var resolvedName = payload != null && !string.IsNullOrEmpty(payload.nickname) ? payload.nickname : payload?.playerName;
            if (payload == null || string.IsNullOrEmpty(resolvedName))
            {
                Debug.LogWarning("[RoomMessageHandler] Payload PlayerJoined null hoac thieu playerName.");
                return;
            }

            if (string.IsNullOrEmpty(payload.roomId))
            {
                Debug.LogWarning("[RoomMessageHandler] Payload PlayerJoined thieu roomId.");
            }

            if (payload.avatarId < 0)
            {
                Debug.LogWarning("[RoomMessageHandler] avatarId am trong PlayerJoined, fallback ve 0. Player=" + payload.playerName + ", avatarId=" + payload.avatarId);
                payload.avatarId = 0;
            }

            if (GameManager.Instant != null)
            {
                GameManager.Instant.OnPlayerJoined(new RoomPlayerState
                {
                    playerId = payload.playerId,
                    nickname = payload.nickname,
                    playerName = resolvedName,
                    avatarId = payload.avatarId,
                    cardCount = payload.cardCount,
                    isDead = payload.isDead
                });
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[RoomMessageHandler] Loi parse PlayerJoined: " + e.Message);
        }
    }

    private void OnRoomState(string payloadJson)
    {
        Debug.Log("[RoomMessageHandler] Nhan RoomState: " + payloadJson);

        try
        {
            var payload = JsonUtility.FromJson<RoomStatePayload>(payloadJson);
            if (payload == null)
            {
                Debug.LogWarning("[RoomMessageHandler] Payload RoomState null.");
                return;
            }

            if (string.IsNullOrEmpty(payload.roomId))
            {
                Debug.LogWarning("[RoomMessageHandler] RoomState thieu roomId.");
            }

            if (GameManager.Instant != null &&
                !string.IsNullOrEmpty(payload.roomId) &&
                !string.IsNullOrEmpty(GameManager.Instant.CurrentRoomId) &&
                !string.Equals(payload.roomId, GameManager.Instant.CurrentRoomId, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("[RoomMessageHandler] Bo qua RoomState khac room. payloadRoomId=" + payload.roomId + ", currentRoomId=" + GameManager.Instant.CurrentRoomId);
                return;
            }

            if (GameManager.Instant != null)
            {
                if (payload.players == null)
                {
                    Debug.LogWarning("[RoomMessageHandler] RoomState khong co danh sach players");
                }

                if (payload.players != null)
                {
                    for (int i = 0; i < payload.players.Count; i++)
                    {
                        var player = payload.players[i];
                        if (player == null)
                        {
                            continue;
                        }

                        if (string.IsNullOrEmpty(player.playerName) && !string.IsNullOrEmpty(player.nickname))
                        {
                            player.playerName = player.nickname;
                        }
                    }
                }

                GameManager.Instant.OnRoomState(payload.players);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[RoomMessageHandler] Loi parse RoomState: " + e.Message);
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

            if (string.IsNullOrEmpty(payload.hostNickname))
            {
                Debug.LogWarning("[RoomMessageHandler] RoomJoined thieu hostNickname, UI co the hien thi fallback.");
            }

            if (payload.hostAvatarId < 0)
            {
                Debug.LogWarning("[RoomMessageHandler] hostAvatarId am trong RoomJoined, fallback ve 0. host=" + payload.hostNickname + ", avatarId=" + payload.hostAvatarId);
                payload.hostAvatarId = 0;
            }

            if (GameManager.Instant == null)
            {
                Debug.LogError("[RoomMessageHandler] GameManager khong ton tai!");
                return;
            }

            GameManager.Instant.SetRoomId(payload.roomId);
            GameManager.Instant.SetRoomHostInfo(payload.hostId, payload.hostNickname);

            if (!string.IsNullOrEmpty(payload.hostNickname))
            {
                GameManager.Instant.OnPlayerJoined(new RoomPlayerState
                {
                    playerName = payload.hostNickname,
                    avatarId = Mathf.Max(0, payload.hostAvatarId)
                });
            }

            var players = new List<string>();
            if (!string.IsNullOrEmpty(payload.hostNickname))
            {
                players.Add(payload.hostNickname);
            }

            if (!string.IsNullOrEmpty(GameManager.Instant.Nickname) &&
                (string.IsNullOrEmpty(payload.hostNickname) || !string.Equals(GameManager.Instant.Nickname, payload.hostNickname, StringComparison.OrdinalIgnoreCase)))
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

    private void OnGameStarted(string payloadJson)
    {
        Debug.Log("[RoomMessageHandler] Nhan GameStarted: " + payloadJson);

        try
        {
            var payload = JsonUtility.FromJson<GameStartedEvent>(payloadJson);
            if (payload == null)
            {
                Debug.LogWarning("[RoomMessageHandler] Payload GameStarted null.");
                return;
            }

            if (string.IsNullOrEmpty(payload.roomId))
            {
                Debug.LogWarning("[RoomMessageHandler] GameStarted thieu roomId.");
            }

            if (GameManager.Instant != null)
            {
                if (payload.players != null && payload.players.Count > 0)
                {
                    GameManager.Instant.SetRoomPlayers(payload.players);
                }
                else
                {
                    Debug.LogWarning("[RoomMessageHandler] GameStarted thieu players, fallback bang danh sach dang co.");
                }

                if (!GameManager.Instant.TryStartGame(payload.initialCardCount, payload.players, out var errorMessage))
                {
                    Debug.LogWarning("[RoomMessageHandler] Khoi tao game state that bai: " + errorMessage);
                }

                if (payload.destinyCard >= 0)
                {
                    GameManager.Instant.SetDestinyCardValue(payload.destinyCard);
                }
                else
                {
                    Debug.LogWarning("[RoomMessageHandler] GameStarted thieu destinyCard hop le.");
                }

                var localNickname = GameManager.Instant.Nickname;
                var foundLocalHand = false;

                if (payload.hands == null || payload.hands.Count == 0)
                {
                    Debug.LogWarning("[RoomMessageHandler] GameStarted khong co hands.");
                }
                else
                {
                    for (int i = 0; i < payload.hands.Count; i++)
                    {
                        var hand = payload.hands[i];
                        if (hand == null || string.IsNullOrEmpty(hand.playerName))
                        {
                            Debug.LogWarning("[RoomMessageHandler] GameStarted hand null hoac thieu playerName tai index=" + i);
                            continue;
                        }

                        var cardCount = hand.cards != null ? hand.cards.Count : 0;
                        GameManager.Instant.SetPlayerCardCount(hand.playerName, cardCount);

                        if (!string.IsNullOrEmpty(localNickname) &&
                            string.Equals(hand.playerName, localNickname, StringComparison.OrdinalIgnoreCase))
                        {
                            foundLocalHand = true;
                            GameManager.Instant.SetLocalHandCards(hand.cards);
                        }
                    }
                }

                if (!foundLocalHand)
                {
                    Debug.LogWarning("[RoomMessageHandler] Khong tim thay hand cua local player trong GameStarted.");
                    GameManager.Instant.SetLocalHandCards(new List<int>());
                }
            }

            if (!string.IsNullOrEmpty(gameSceneName) && Application.CanStreamedLevelBeLoaded(gameSceneName))
            {
                SceneManager.LoadScene(gameSceneName);
            }
            else
            {
                Debug.LogError("[RoomMessageHandler] Khong tim thay GameScene trong Build Settings: " + gameSceneName);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[RoomMessageHandler] Loi parse GameStarted: " + e.Message);
        }
    }

    private void OnStartGameFailed(string payloadJson)
    {
        Debug.LogError("[RoomMessageHandler] Bat dau game that bai: " + payloadJson);
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
