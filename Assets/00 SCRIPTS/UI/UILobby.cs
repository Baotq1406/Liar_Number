using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;

[System.Serializable]
public class CancelRoomRequest
{
    public string roomId;
    public string playerId;
}

[System.Serializable]
public class LeaveRoomRequest
{
    public string roomId;
    public string playerId;
}

[System.Serializable]
public class StartGameRequest
{
    public string roomId;
    public string playerId;
    public int initialCardCount;
}

public class UILobby : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text roomCodeText;
    [SerializeField] private TMP_Text[] playerNameSlots;
    [SerializeField] private Image[] playerAvatarSlots;
    [SerializeField] private GameObject[] playerSlotObjects;
    [SerializeField] private GameObject cancelButtonObject;
    [SerializeField] private GameObject leaveRoomButtonObject;
    [SerializeField] private GameObject startButtonObject;

    [Header("Cau hinh")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private string gameSceneName = "GameScene";
    [SerializeField] private bool sendCancelToServer = true;
    [SerializeField] private bool sendLeaveToServer = true;
    [SerializeField] private bool sendStartToServer = true;
    [SerializeField] private string waitingPlayerText = "Dang cho nguoi choi...";
    [SerializeField] private int initialCardCount = 6;
    [SerializeField] private string avatarResourcesPath = "Avatars";

    private bool _isCancelling;
    private bool _isLeaving;
    private Sprite[] _avatars;

    private void OnEnable()
    {
        GameManager.RoomInfoChanged += RefreshRoomInfo;
    }

    private void OnDisable()
    {
        GameManager.RoomInfoChanged -= RefreshRoomInfo;
    }

    private void Start()
    {
        _avatars = Resources.LoadAll<Sprite>(avatarResourcesPath);
        if (_avatars == null || _avatars.Length == 0)
        {
            Debug.LogWarning("[UILobby] Khong load duoc avatar tu Resources/" + avatarResourcesPath);
        }
        else
        {
            Debug.Log("[UILobby] Da load " + _avatars.Length + " avatar tu Resources/" + avatarResourcesPath);
        }

        EnsureAvatarSlotBindings();

        RefreshRoomInfo();
    }

    public void RefreshRoomInfo()
    {
        if (GameManager.Instant == null)
        {
            Debug.LogError("[UILobby] GameManager khong ton tai!");
            return;
        }

        var roomId = string.IsNullOrEmpty(GameManager.Instant.RoomId) ? "----" : GameManager.Instant.RoomId;
        var hostNickname = string.IsNullOrEmpty(GameManager.Instant.HostNickname)
            ? (string.IsNullOrEmpty(GameManager.Instant.Nickname) ? "Player" : GameManager.Instant.Nickname)
            : GameManager.Instant.HostNickname;

        if (roomCodeText != null)
        {
            roomCodeText.text = "CODE: " + roomId;
        }

        UpdatePlayerSlots(hostNickname, GameManager.Instant.RoomPlayers);
        UpdateActionButtons();
    }

    private void UpdateActionButtons()
    {
        var isHost = IsCurrentPlayerHost();
        var joinedPlayerCount = GetJoinedPlayerCount();
        var canStart = isHost && joinedPlayerCount >= 2 && joinedPlayerCount <= 4;

        if (cancelButtonObject != null)
        {
            cancelButtonObject.SetActive(isHost);
        }

        if (leaveRoomButtonObject != null)
        {
            leaveRoomButtonObject.SetActive(!isHost);
        }

        if (startButtonObject != null)
        {
            startButtonObject.SetActive(isHost);

            var button = startButtonObject.GetComponent<Button>();
            if (button != null)
            {
                button.interactable = canStart;
            }
        }
    }

    private bool IsCurrentPlayerHost()
    {
        if (GameManager.Instant == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(GameManager.Instant.HostId) && !string.IsNullOrEmpty(GameManager.Instant.PlayerId))
        {
            return string.Equals(GameManager.Instant.PlayerId, GameManager.Instant.HostId, System.StringComparison.Ordinal);
        }

        if (!string.IsNullOrEmpty(GameManager.Instant.HostNickname) && !string.IsNullOrEmpty(GameManager.Instant.Nickname))
        {
            return string.Equals(GameManager.Instant.Nickname, GameManager.Instant.HostNickname, System.StringComparison.Ordinal);
        }

        return false;
    }

    private void UpdatePlayerSlots(string hostNickname, IReadOnlyList<string> players)
    {
        if (playerNameSlots == null || playerNameSlots.Length == 0)
        {
            return;
        }

        var displayNames = new List<string>();
        var rawNames = new List<string>();
        displayNames.Add(hostNickname + " (host)");
        rawNames.Add(hostNickname);

        if (players != null)
        {
            for (int i = 0; i < players.Count; i++)
            {
                var playerName = players[i];
                if (string.IsNullOrEmpty(playerName))
                {
                    continue;
                }

                if (string.Equals(playerName, hostNickname, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!ContainsNameIgnoreCase(rawNames, playerName))
                {
                    displayNames.Add(playerName);
                    rawNames.Add(playerName);
                }
            }
        }

        for (int i = 0; i < playerNameSlots.Length; i++)
        {
            var hasPlayer = i < displayNames.Count;

            if (playerNameSlots[i] == null)
            {
                continue;
            }

            playerNameSlots[i].text = hasPlayer ? displayNames[i] : waitingPlayerText;

            if (playerAvatarSlots != null && i < playerAvatarSlots.Length && playerAvatarSlots[i] != null)
            {
                var avatarImage = playerAvatarSlots[i];
                avatarImage.gameObject.SetActive(hasPlayer);

                if (hasPlayer)
                {
                    avatarImage.sprite = GetAvatarSprite(rawNames[i]);
                }
            }
        }

        UpdatePlayerSlotObjects(displayNames.Count);
    }

    private Sprite GetAvatarSprite(string playerName)
    {
        if (_avatars == null || _avatars.Length == 0)
        {
            return null;
        }

        var avatarId = GameManager.Instant != null ? GameManager.Instant.GetPlayerAvatarId(playerName) : 0;
        if (avatarId < 0 || avatarId >= _avatars.Length)
        {
            Debug.LogWarning("[UILobby] avatarId ngoai range, fallback ve 0. Player=" + playerName + ", avatarId=" + avatarId + ", so avatar=" + _avatars.Length);
            avatarId = 0;
        }

        return _avatars[avatarId];
    }

    private void EnsureAvatarSlotBindings()
    {
        if (playerNameSlots == null || playerNameSlots.Length == 0)
        {
            return;
        }

        var requiredSize = playerNameSlots.Length;
        if (playerAvatarSlots == null || playerAvatarSlots.Length < requiredSize)
        {
            var resized = new Image[requiredSize];
            if (playerAvatarSlots != null)
            {
                for (int i = 0; i < playerAvatarSlots.Length && i < resized.Length; i++)
                {
                    resized[i] = playerAvatarSlots[i];
                }
            }

            playerAvatarSlots = resized;
        }

        for (int i = 0; i < playerAvatarSlots.Length; i++)
        {
            if (playerAvatarSlots[i] != null)
            {
                continue;
            }

            if (playerSlotObjects == null || i >= playerSlotObjects.Length || playerSlotObjects[i] == null)
            {
                continue;
            }

            var images = playerSlotObjects[i].GetComponentsInChildren<Image>(true);
            for (int j = 0; j < images.Length; j++)
            {
                var candidate = images[j];
                if (candidate == null || candidate.gameObject == playerSlotObjects[i])
                {
                    continue;
                }

                var lowerName = candidate.gameObject.name.ToLowerInvariant();
                if (lowerName.Contains("avatar"))
                {
                    playerAvatarSlots[i] = candidate;
                    break;
                }
            }

            if (playerAvatarSlots[i] == null)
            {
                Debug.LogWarning("[UILobby] Chua gan duoc avatar slot index=" + i + ". Vui long gan playerAvatarSlots trong Inspector.");
            }
        }
    }

    private static bool ContainsNameIgnoreCase(List<string> names, string value)
    {
        if (names == null || string.IsNullOrEmpty(value))
        {
            return false;
        }

        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], value, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void UpdatePlayerSlotObjects(int activePlayerCount)
    {
        if (playerSlotObjects == null || playerSlotObjects.Length == 0)
        {
            return;
        }

        for (int i = 0; i < playerSlotObjects.Length; i++)
        {
            if (playerSlotObjects[i] == null)
            {
                continue;
            }

            var shouldActive = i < activePlayerCount;
            if (playerSlotObjects[i].activeSelf != shouldActive)
            {
                playerSlotObjects[i].SetActive(shouldActive);
            }
        }
    }

    public void OnCancelButtonClicked()
    {
        if (_isCancelling || _isLeaving)
        {
            return;
        }

        if (GameManager.Instant == null)
        {
            Debug.LogError("[UILobby] GameManager khong ton tai, khong the huy room");
            return;
        }

        var roomId = GameManager.Instant.RoomId;
        var playerId = GameManager.Instant.PlayerId;

        if (sendCancelToServer && NetworkClient.Instant != null && NetworkClient.Instant.IsConnected && !string.IsNullOrEmpty(roomId))
        {
            _isCancelling = true;

            var payload = new CancelRoomRequest
            {
                roomId = roomId,
                playerId = playerId
            };

            NetworkClient.Instant.SendNetworkMessage("CancelRoom", payload);
            Debug.Log("[UILobby] Da gui CancelRoom len server, doi RoomClosed de quay ve MainMenu");
            return;
        }

        Debug.LogWarning("[UILobby] Khong the gui CancelRoom, fallback quay ve MainMenu local");
        GameManager.Instant.ClearRoomId();
        SceneManager.LoadScene(mainMenuSceneName);
    }

    public void OnLeaveRoomButtonClicked()
    {
        if (_isLeaving || _isCancelling)
        {
            return;
        }

        if (GameManager.Instant == null)
        {
            Debug.LogError("[UILobby] GameManager khong ton tai, khong the roi room");
            return;
        }

        var roomId = GameManager.Instant.RoomId;
        var playerId = GameManager.Instant.PlayerId;

        if (sendLeaveToServer && NetworkClient.Instant != null && NetworkClient.Instant.IsConnected && !string.IsNullOrEmpty(roomId))
        {
            _isLeaving = true;

            var payload = new LeaveRoomRequest
            {
                roomId = roomId,
                playerId = playerId
            };

            NetworkClient.Instant.SendNetworkMessage("LeaveRoom", payload);
            Debug.Log("[UILobby] Da gui LeaveRoom len server, quay ve MainMenu local");
        }

        GameManager.Instant.ClearRoomId();
        SceneManager.LoadScene(mainMenuSceneName);
    }

    public void OnStartButtonClicked()
    {
        if (GameManager.Instant == null)
        {
            Debug.LogError("[UILobby] GameManager khong ton tai, khong the bat dau game");
            return;
        }

        if (!IsCurrentPlayerHost())
        {
            Debug.LogWarning("[UILobby] Chi host moi duoc bat dau game");
            return;
        }

        var joinedPlayerCount = GetJoinedPlayerCount();
        if (joinedPlayerCount < 2 || joinedPlayerCount > 4)
        {
            Debug.LogWarning("[UILobby] So nguoi choi phai tu 2 den 4 moi duoc start");
            return;
        }

        if (sendStartToServer && NetworkClient.Instant != null && NetworkClient.Instant.IsConnected)
        {
            var payload = new StartGameRequest
            {
                roomId = GameManager.Instant.RoomId,
                playerId = GameManager.Instant.PlayerId,
                initialCardCount = initialCardCount
            };

            NetworkClient.Instant.SendNetworkMessage("StartGame", payload);
            Debug.Log("[UILobby] Da gui yeu cau StartGame len server");
            return;
        }

        if (!GameManager.Instant.TryStartGame(initialCardCount, out var errorMessage))
        {
            Debug.LogWarning("[UILobby] Khong the bat dau game local: " + errorMessage);
            return;
        }

        if (!string.IsNullOrEmpty(gameSceneName) && Application.CanStreamedLevelBeLoaded(gameSceneName))
        {
            SceneManager.LoadScene(gameSceneName);
            return;
        }

        Debug.LogError("[UILobby] Khong tim thay GameScene trong Build Settings: " + gameSceneName);
    }

    private int GetJoinedPlayerCount()
    {
        if (GameManager.Instant == null)
        {
            return 0;
        }

        var names = new List<string>();

        if (!string.IsNullOrEmpty(GameManager.Instant.HostNickname))
        {
            names.Add(GameManager.Instant.HostNickname);
        }

        var roomPlayers = GameManager.Instant.RoomPlayers;
        if (roomPlayers != null)
        {
            for (int i = 0; i < roomPlayers.Count; i++)
            {
                var player = roomPlayers[i];
                if (string.IsNullOrEmpty(player) || names.Contains(player))
                {
                    continue;
                }

                names.Add(player);
            }
        }

        if (!string.IsNullOrEmpty(GameManager.Instant.Nickname) && !names.Contains(GameManager.Instant.Nickname))
        {
            names.Add(GameManager.Instant.Nickname);
        }

        return names.Count;
    }
}
