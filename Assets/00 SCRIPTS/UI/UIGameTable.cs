using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIGameTable : MonoBehaviour
{
    [Header("Local Player UI")]
    [SerializeField] private TMP_Text playerNameText;
    [SerializeField] private Image localAvatarImage;

    [Header("Opponent UI")]
    [SerializeField] private GameObject[] opponentSlotObjects;
    [SerializeField] private TMP_Text[] opponentNameTexts;
    [SerializeField] private TMP_Text[] opponentCardCountTexts;
    [SerializeField] private Image[] opponentAvatarImages;

    [Header("Text Format")]
    [SerializeField] private string opponentCardCountFormat = "(Card : {0})";
    [SerializeField] private string emptyNameText = "Opponent";

    [Header("Avatar")]
    [SerializeField] private string avatarResourcesPath = "Avatars";

    private Sprite[] _avatars;

    private void Awake()
    {
        _avatars = Resources.LoadAll<Sprite>(avatarResourcesPath);
        if (_avatars == null || _avatars.Length == 0)
        {
            Debug.LogWarning("[UIGameTable] Khong load duoc avatar tu Resources/" + avatarResourcesPath);
        }
        else
        {
            Debug.Log("[UIGameTable] Da load " + _avatars.Length + " avatar tu Resources/" + avatarResourcesPath);
        }

        EnsureAvatarBindings();
    }

    private void OnEnable()
    {
        GameManager.GameInfoChanged += Refresh;
        GameManager.RoomInfoChanged += Refresh;
    }

    private void OnDisable()
    {
        GameManager.GameInfoChanged -= Refresh;
        GameManager.RoomInfoChanged -= Refresh;
    }

    private void Start()
    {
        Refresh();
    }

    public void Refresh()
    {
        if (GameManager.Instant == null)
        {
            return;
        }

        if (opponentNameTexts == null)
        {
            return;
        }

        var localName = string.IsNullOrEmpty(GameManager.Instant.Nickname) ? "Player" : GameManager.Instant.Nickname;

        if (playerNameText != null)
        {
            playerNameText.text = localName;
        }

        SetAvatar(localAvatarImage, localName, true);

        var opponents = BuildOpponentList(localName);

        for (int i = 0; i < opponentNameTexts.Length; i++)
        {
            bool hasOpponent = i < opponents.Count;

            if (opponentSlotObjects != null && i < opponentSlotObjects.Length && opponentSlotObjects[i] != null)
            {
                opponentSlotObjects[i].SetActive(hasOpponent);
            }

            if (opponentNameTexts[i] != null)
            {
                opponentNameTexts[i].text = hasOpponent ? opponents[i] : emptyNameText;

                if (opponentSlotObjects == null || i >= opponentSlotObjects.Length || opponentSlotObjects[i] == null)
                {
                    opponentNameTexts[i].gameObject.SetActive(hasOpponent);
                }
            }

            if (opponentCardCountTexts != null && i < opponentCardCountTexts.Length && opponentCardCountTexts[i] != null)
            {
                int cardCount = hasOpponent ? GameManager.Instant.GetPlayerCardCount(opponents[i]) : 0;
                opponentCardCountTexts[i].text = string.Format(opponentCardCountFormat, cardCount);

                if (opponentSlotObjects == null || i >= opponentSlotObjects.Length || opponentSlotObjects[i] == null)
                {
                    opponentCardCountTexts[i].gameObject.SetActive(hasOpponent);
                }
            }

            if (opponentAvatarImages != null && i < opponentAvatarImages.Length)
            {
                var playerName = hasOpponent ? opponents[i] : string.Empty;
                SetAvatar(opponentAvatarImages[i], playerName, hasOpponent);
            }
        }
    }

    private void SetAvatar(Image avatarImage, string playerName, bool visible)
    {
        if (avatarImage == null)
        {
            return;
        }

        avatarImage.gameObject.SetActive(visible);
        if (!visible)
        {
            return;
        }

        if (_avatars == null || _avatars.Length == 0 || GameManager.Instant == null)
        {
            return;
        }

        var avatarId = GameManager.Instant.GetPlayerAvatarId(playerName);
        if (avatarId < 0 || avatarId >= _avatars.Length)
        {
            Debug.LogWarning("[UIGameTable] avatarId ngoai range, fallback ve 0. Player=" + playerName + ", avatarId=" + avatarId + ", so avatar=" + _avatars.Length);
            avatarId = 0;
        }

        avatarImage.sprite = _avatars[avatarId];
    }

    private List<string> BuildOpponentList(string localName)
    {
        var result = new List<string>();

        var gamePlayers = GameManager.Instant.GamePlayers;
        if (gamePlayers != null && gamePlayers.Count > 0)
        {
            for (int i = 0; i < gamePlayers.Count; i++)
            {
                var playerName = gamePlayers[i];
                if (string.IsNullOrEmpty(playerName) || string.Equals(playerName, localName, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!ContainsNameIgnoreCase(result, playerName))
                {
                    result.Add(playerName);
                }
            }

            return result;
        }

        var roomPlayers = GameManager.Instant.RoomPlayers;
        if (roomPlayers == null)
        {
            return result;
        }

        for (int i = 0; i < roomPlayers.Count; i++)
        {
            var playerName = roomPlayers[i];
            if (string.IsNullOrEmpty(playerName) || string.Equals(playerName, localName, System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ContainsNameIgnoreCase(result, playerName))
            {
                result.Add(playerName);
            }
        }

        return result;
    }

    private void EnsureAvatarBindings()
    {
        if (opponentNameTexts == null || opponentNameTexts.Length == 0)
        {
            return;
        }

        var requiredSize = opponentNameTexts.Length;
        if (opponentAvatarImages == null || opponentAvatarImages.Length < requiredSize)
        {
            var resized = new Image[requiredSize];
            if (opponentAvatarImages != null)
            {
                for (int i = 0; i < opponentAvatarImages.Length && i < resized.Length; i++)
                {
                    resized[i] = opponentAvatarImages[i];
                }
            }

            opponentAvatarImages = resized;
        }

        for (int i = 0; i < opponentAvatarImages.Length; i++)
        {
            if (opponentAvatarImages[i] != null)
            {
                continue;
            }

            if (opponentSlotObjects == null || i >= opponentSlotObjects.Length || opponentSlotObjects[i] == null)
            {
                continue;
            }

            var images = opponentSlotObjects[i].GetComponentsInChildren<Image>(true);
            for (int j = 0; j < images.Length; j++)
            {
                var candidate = images[j];
                if (candidate == null || candidate.gameObject == opponentSlotObjects[i])
                {
                    continue;
                }

                if (candidate.gameObject.name.ToLowerInvariant().Contains("avatar"))
                {
                    opponentAvatarImages[i] = candidate;
                    break;
                }
            }

            if (opponentAvatarImages[i] == null)
            {
                Debug.LogWarning("[UIGameTable] Chua bind duoc opponent avatar image index=" + i + ". Vui long gan trong Inspector.");
            }
        }
    }

    private static bool ContainsNameIgnoreCase(List<string> source, string playerName)
    {
        if (source == null || string.IsNullOrEmpty(playerName))
        {
            return false;
        }

        for (int i = 0; i < source.Count; i++)
        {
            if (string.Equals(source[i], playerName, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
