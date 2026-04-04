using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
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

public class UILobby : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text roomCodeText;
    [SerializeField] private TMP_Text[] playerNameSlots;
    [SerializeField] private GameObject[] playerSlotObjects;
    [SerializeField] private GameObject cancelButtonObject;
    [SerializeField] private GameObject leaveRoomButtonObject;

    [Header("Cau hinh")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private bool sendCancelToServer = true;
    [SerializeField] private bool sendLeaveToServer = true;
    [SerializeField] private string waitingPlayerText = "Dang cho nguoi choi...";

    private bool _isCancelling;
    private bool _isLeaving;

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

        if (cancelButtonObject != null)
        {
            cancelButtonObject.SetActive(isHost);
        }

        if (leaveRoomButtonObject != null)
        {
            leaveRoomButtonObject.SetActive(!isHost);
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
        displayNames.Add(hostNickname + " (host)");

        if (players != null)
        {
            for (int i = 0; i < players.Count; i++)
            {
                var playerName = players[i];
                if (string.IsNullOrEmpty(playerName))
                {
                    continue;
                }

                if (string.Equals(playerName, hostNickname))
                {
                    continue;
                }

                if (!displayNames.Contains(playerName))
                {
                    displayNames.Add(playerName);
                }
            }
        }

        for (int i = 0; i < playerNameSlots.Length; i++)
        {
            if (playerNameSlots[i] == null)
            {
                continue;
            }

            playerNameSlots[i].text = i < displayNames.Count ? displayNames[i] : waitingPlayerText;
        }

        UpdatePlayerSlotObjects(displayNames.Count);
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
}
