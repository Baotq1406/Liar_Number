using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
using DG.Tweening;

public class UIGameTable : MonoBehaviour
{
    [Header("Local Player UI")]
    [SerializeField] private TMP_Text playerNameText;
    [SerializeField] private Image localAvatarImage;
    [SerializeField] private TMP_Text localGunCountText;

    [Header("Opponent UI")]
    [SerializeField] private GameObject[] opponentSlotObjects;
    [SerializeField] private TMP_Text[] opponentNameTexts;
    [SerializeField] private TMP_Text[] opponentCardCountTexts;
    [SerializeField] private TMP_Text[] opponentGunCountTexts;
    [SerializeField] private Image[] opponentAvatarImages;

    [Header("Text Format")]
    [SerializeField] private string opponentCardCountFormat = "(Card : {0})";
    [SerializeField] private string gunCountFormat = "{0}/6";
    [SerializeField] private string emptyNameText = "Opponent";

    [Header("Avatar")]
    [SerializeField] private string avatarResourcesPath = "Avatars";
    [SerializeField] private Color aliveAvatarTint = Color.white;
    [SerializeField] private Color deadAvatarTint = new Color(0.85f, 0.85f, 0.85f, 1f);

    [Header("Destiny Card")]
    [SerializeField] private GameObject[] destinyCardObjectsByValue = new GameObject[3];
    [SerializeField] private TMP_Text destinyCardValueText;

    [Header("Card Deck Panel")]
    [SerializeField] private GameObject cardDeckPanel;
    [SerializeField] private Button deckButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private CanvasGroup cardDeckCanvasGroup;
    [SerializeField] private Image cardDeckBlockerImage;
    [SerializeField] private bool disableCardInteractionWhenPanelOpen = true;
    [SerializeField] private bool usePanelFadeAnimation = true;
    [SerializeField] private float panelFadeDuration = 0.2f;

    [Header("Turn UI")]
    [SerializeField] private Button playButton;
    [SerializeField] private HandManager handManager;
    [SerializeField] private int fallbackDeclaredNumber = 0;
    [SerializeField] private Color normalNameColor = Color.white;
    [SerializeField] private Color activeTurnNameColor = Color.yellow;
    [SerializeField] private Color deadNameColor = Color.red;

    [Header("Action Panels")]
    [SerializeField] private GameObject waitingPanel;
    [SerializeField] private TMP_Text waitingMessageText;
    [SerializeField] private GameObject liarPanel;
    [SerializeField] private TMP_Text liarPanelMessageText;
    [SerializeField] private string liarPanelMessageFormat = "{0} has played the following cards:";
    [SerializeField] private string liarPanelFallbackActorName = "Opponent";
    [SerializeField] private Button skipButton;
    [SerializeField] private Button liarButton;
    [SerializeField] private TMP_Text resolveResultText;
    [SerializeField] private TMP_Text errorText;
    [SerializeField] private float liarRevealHoldSeconds = 5f;

    [Header("Card Field")]
    [FormerlySerializedAs("waitingCardFieldRoot")]
    [SerializeField] private GameObject cardFieldWait;
    [FormerlySerializedAs("waitingCardFieldCardBackObjects")]
    [SerializeField] private GameObject[] cardFieldWaitCardBackObjects;
    [FormerlySerializedAs("liarCardFieldRoot")]
    [SerializeField] private GameObject cardFieldLiar;
    [FormerlySerializedAs("liarCardFieldCardBackObjects")]
    [SerializeField] private GameObject[] cardFieldLiarCardBackObjects;

    private Sprite[] _avatars;
    private Tween _panelTween;
    private bool _isActionPending;
    private bool _shouldRevealLiarCards;
    private int _revealLiarCardsPlayId;
    private float _liarRevealHoldUntil;
    private readonly List<int> _revealedLiarCards = new List<int>();
    private string _revealedLiarActorName = string.Empty;

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
        EnsureGunBindings();
        EnsureCardFieldBindings();
        ResolveCardDeckComponents();
        BindCardDeckButtons();
    }

    private void OnDestroy()
    {
        if (_panelTween != null && _panelTween.IsActive())
        {
            _panelTween.Kill();
        }

        ApplyPanelInputState(false);
        UnbindCardDeckButtons();
    }

    private void OnEnable()
    {
        GameManager.GameInfoChanged += Refresh;
        GameManager.RoomInfoChanged += Refresh;
        GameManager.TurnInfoChanged += Refresh;
        BindTurnButtons();
    }

    private void OnDisable()
    {
        GameManager.GameInfoChanged -= Refresh;
        GameManager.RoomInfoChanged -= Refresh;
        GameManager.TurnInfoChanged -= Refresh;
        UnbindTurnButtons();
    }

    private void Start()
    {
        SetCardDeckPanelActive(false);

        if (waitingPanel != null)
        {
            waitingPanel.SetActive(false);
        }

        if (liarPanel != null)
        {
            liarPanel.SetActive(false);
        }

        Refresh();
    }

    public void OnPlayButtonClicked()
    {
        if (GameManager.Instant == null || NetworkClient.Instant == null)
        {
            return;
        }

        if (GameManager.Instant.IsLocalPlayerDead)
        {
            return;
        }

        if (!GameManager.Instant.CanLocalPlay())
        {
            Debug.LogWarning("[UIGameTable] Chua den luot local player, khong the PLAY.");
            return;
        }

        var activeHandManager = handManager != null ? handManager : FindObjectOfType<HandManager>();
        if (activeHandManager == null)
        {
            Debug.LogError("[UIGameTable] Khong tim thay HandManager de lay bai da chon.");
            return;
        }

        var selectedCards = activeHandManager.GetSelectedCardValues();
        if (selectedCards == null || selectedCards.Count == 0)
        {
            Debug.LogWarning("[UIGameTable] Chua chon la bai nao de PLAY.");
            return;
        }

        var declaredNumber = DetermineDeclaredNumber(selectedCards);
        var sent = NetworkClient.Instant.SendPlayCard(selectedCards, declaredNumber);
        if (!sent)
        {
            return;
        }

        _isActionPending = true;

        var removedCount = activeHandManager.RemoveSelectedCards();
        if (removedCount > 0 && !string.IsNullOrEmpty(GameManager.Instant.Nickname))
        {
            GameManager.Instant.SetPlayerCardCount(GameManager.Instant.Nickname, activeHandManager.GetHandCardCount());
        }
    }

    public void OnDeckButtonClicked()
    {
        SetCardDeckPanelActive(true);
    }

    public void OnQuitButtonClicked()
    {
        SetCardDeckPanelActive(false);
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
            playerNameText.color = ResolveNameColor(localName);
        }

        if (localGunCountText != null)
        {
            localGunCountText.text = string.Format(gunCountFormat, GameManager.Instant.GetPlayerGunShotCount(localName));
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
                opponentNameTexts[i].color = hasOpponent ? ResolveNameColor(opponents[i]) : normalNameColor;

                if (opponentSlotObjects == null || i >= opponentSlotObjects.Length || opponentSlotObjects[i] == null)
                {
                    opponentNameTexts[i].gameObject.SetActive(hasOpponent);
                }
            }

            if (opponentGunCountTexts != null && i < opponentGunCountTexts.Length && opponentGunCountTexts[i] != null)
            {
                int gunCount = hasOpponent ? GameManager.Instant.GetPlayerGunShotCount(opponents[i]) : 0;
                opponentGunCountTexts[i].text = string.Format(gunCountFormat, gunCount);

                if (opponentSlotObjects == null || i >= opponentSlotObjects.Length || opponentSlotObjects[i] == null)
                {
                    opponentGunCountTexts[i].gameObject.SetActive(hasOpponent);
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

        RefreshDestinyCard();
        RefreshTurnPanels();
    }

    public void OnSkipButtonClicked()
    {
        if (GameManager.Instant == null || NetworkClient.Instant == null)
        {
            return;
        }

        if (GameManager.Instant.IsLocalPlayerDead)
        {
            return;
        }

        if (NetworkClient.Instant.SendSkip())
        {
            _isActionPending = true;
        }
    }

    public void OnLiarButtonClicked()
    {
        if (GameManager.Instant == null || NetworkClient.Instant == null)
        {
            return;
        }

        if (GameManager.Instant.IsLocalPlayerDead)
        {
            return;
        }

        if (NetworkClient.Instant.SendLiar())
        {
            _isActionPending = true;
            RefreshTurnPanels();
        }
    }

    private void RefreshDestinyCard()
    {
        if (GameManager.Instant == null)
        {
            return;
        }

        var destinyValue = GameManager.Instant.DestinyCardValue;

        if (destinyCardObjectsByValue != null)
        {
            for (int i = 0; i < destinyCardObjectsByValue.Length; i++)
            {
                if (destinyCardObjectsByValue[i] == null)
                {
                    continue;
                }

                destinyCardObjectsByValue[i].SetActive(i == destinyValue);
            }
        }

        if (destinyCardValueText != null)
        {
            destinyCardValueText.text = destinyValue >= 0 ? destinyValue.ToString() : "-";
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
        avatarImage.color = IsPlayerDead(playerName) ? deadAvatarTint : aliveAvatarTint;
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

    private void EnsureGunBindings()
    {
        if (opponentNameTexts == null || opponentNameTexts.Length == 0)
        {
            return;
        }

        var requiredSize = opponentNameTexts.Length;
        if (opponentGunCountTexts == null || opponentGunCountTexts.Length < requiredSize)
        {
            var resized = new TMP_Text[requiredSize];
            if (opponentGunCountTexts != null)
            {
                for (int i = 0; i < opponentGunCountTexts.Length && i < resized.Length; i++)
                {
                    resized[i] = opponentGunCountTexts[i];
                }
            }

            opponentGunCountTexts = resized;
        }

        for (int i = 0; i < opponentGunCountTexts.Length; i++)
        {
            if (opponentGunCountTexts[i] != null)
            {
                continue;
            }

            if (opponentSlotObjects == null || i >= opponentSlotObjects.Length || opponentSlotObjects[i] == null)
            {
                continue;
            }

            var texts = opponentSlotObjects[i].GetComponentsInChildren<TMP_Text>(true);
            for (int j = 0; j < texts.Length; j++)
            {
                var candidate = texts[j];
                if (candidate == null || candidate.gameObject == opponentSlotObjects[i])
                {
                    continue;
                }

                var lowerName = candidate.gameObject.name.ToLowerInvariant();
                if (lowerName.Contains("gun") || lowerName.Contains("co_quay") || lowerName.Contains("cogay"))
                {
                    opponentGunCountTexts[i] = candidate;
                    break;
                }
            }
        }
    }

    private void EnsureCardFieldBindings()
    {
        AutoResolveCardFieldRoots();

        AutoBindCardField(ref cardFieldWait, ref cardFieldWaitCardBackObjects, "WAITING");
        AutoBindCardField(ref cardFieldLiar, ref cardFieldLiarCardBackObjects, "LIAR");
    }

    private void AutoResolveCardFieldRoots()
    {
        if (cardFieldWait == null)
        {
            cardFieldWait = FindCardFieldRoot(waitingPanel != null ? waitingPanel.transform : null, "cardfieldwait", "cardfield_wait", "cardwait");
        }

        if (cardFieldLiar == null)
        {
            cardFieldLiar = FindCardFieldRoot(liarPanel != null ? liarPanel.transform : null, "cardfieldliar", "cardfield_liar", "cardliar");
        }

        if (cardFieldWait == null)
        {
            cardFieldWait = FindCardFieldRoot(transform, "cardfieldwait", "cardfield_wait", "cardwait");
        }

        if (cardFieldLiar == null)
        {
            cardFieldLiar = FindCardFieldRoot(transform, "cardfieldliar", "cardfield_liar", "cardliar");
        }
    }

    private static GameObject FindCardFieldRoot(Transform searchRoot, params string[] candidates)
    {
        if (searchRoot == null || candidates == null || candidates.Length == 0)
        {
            return null;
        }

        var all = searchRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            var current = all[i];
            if (current == null)
            {
                continue;
            }

            var currentName = current.gameObject.name;
            if (string.IsNullOrEmpty(currentName))
            {
                continue;
            }

            var normalized = currentName.Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
            for (int j = 0; j < candidates.Length; j++)
            {
                var candidate = candidates[j];
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                var normalizedCandidate = candidate.Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
                if (normalized == normalizedCandidate)
                {
                    return current.gameObject;
                }
            }
        }

        return null;
    }

    private void AutoBindCardField(ref GameObject root, ref GameObject[] cardBackObjects, string tag)
    {
        if (root == null)
        {
            return;
        }

        var needsAutoBind = cardBackObjects == null || cardBackObjects.Length < 2;
        if (!needsAutoBind)
        {
            for (int i = 0; i < cardBackObjects.Length; i++)
            {
                if (cardBackObjects[i] == null)
                {
                    needsAutoBind = true;
                    break;
                }
            }
        }

        if (!needsAutoBind)
        {
            return;
        }

        var discoveredCardBacks = new List<GameObject>();
        var cardFieldTransforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < cardFieldTransforms.Length; i++)
        {
            var candidate = cardFieldTransforms[i];
            if (candidate == null || candidate == root.transform)
            {
                continue;
            }

            var candidateName = candidate.gameObject.name;
            if (string.IsNullOrEmpty(candidateName) || !candidateName.ToLowerInvariant().Contains("card"))
            {
                continue;
            }

            discoveredCardBacks.Add(candidate.gameObject);
        }

        if (discoveredCardBacks.Count > 0)
        {
            cardBackObjects = discoveredCardBacks.ToArray();
            Debug.Log("[UIGameTable] Tu dong bind " + tag + " card field card backs: " + cardBackObjects.Length);
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

    private Color ResolveNameColor(string playerName)
    {
        if (IsPlayerDead(playerName))
        {
            return deadNameColor;
        }

        return IsCurrentTurnByName(playerName) ? activeTurnNameColor : normalNameColor;
    }

    private void BindCardDeckButtons()
    {
        if (deckButton != null)
        {
            deckButton.onClick.RemoveListener(OnDeckButtonClicked);
            deckButton.onClick.AddListener(OnDeckButtonClicked);
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveListener(OnQuitButtonClicked);
            quitButton.onClick.AddListener(OnQuitButtonClicked);
        }
    }

    private void UnbindCardDeckButtons()
    {
        if (deckButton != null)
        {
            deckButton.onClick.RemoveListener(OnDeckButtonClicked);
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveListener(OnQuitButtonClicked);
        }
    }

    private void SetCardDeckPanelActive(bool isActive)
    {
        if (cardDeckPanel == null)
        {
            Debug.LogWarning("[UIGameTable] Chua gan CardDeckPanel trong Inspector.");
            return;
        }

        if (_panelTween != null && _panelTween.IsActive())
        {
            _panelTween.Kill();
        }

        if (isActive)
        {
            cardDeckPanel.SetActive(true);
            ApplyPanelInputState(true); // block input ngay khi mo panel

            if (usePanelFadeAnimation && cardDeckCanvasGroup != null)
            {
                cardDeckCanvasGroup.alpha = 0f;
                _panelTween = cardDeckCanvasGroup.DOFade(1f, Mathf.Max(0f, panelFadeDuration)).SetEase(Ease.OutQuad);
            }
            else if (cardDeckCanvasGroup != null)
            {
                cardDeckCanvasGroup.alpha = 1f;
            }

            return;
        }

        if (!cardDeckPanel.activeSelf)
        {
            ApplyPanelInputState(false);
            return;
        }

        if (usePanelFadeAnimation && cardDeckCanvasGroup != null)
        {
            ApplyPanelInputState(true); // giu block input den khi tat xong
            _panelTween = cardDeckCanvasGroup
                .DOFade(0f, Mathf.Max(0f, panelFadeDuration))
                .SetEase(Ease.InQuad)
                .OnComplete(() =>
                {
                    cardDeckPanel.SetActive(false);
                    ApplyPanelInputState(false);
                });
            return;
        }

        cardDeckPanel.SetActive(false);
        ApplyPanelInputState(false);
    }

    private void ResolveCardDeckComponents()
    {
        if (cardDeckPanel == null)
        {
            return;
        }

        if (cardDeckCanvasGroup == null)
        {
            cardDeckCanvasGroup = cardDeckPanel.GetComponent<CanvasGroup>();
            if (cardDeckCanvasGroup == null)
            {
                cardDeckCanvasGroup = cardDeckPanel.AddComponent<CanvasGroup>();
            }
        }

        if (cardDeckBlockerImage == null)
        {
            cardDeckBlockerImage = cardDeckPanel.GetComponent<Image>();
            if (cardDeckBlockerImage == null)
            {
                cardDeckBlockerImage = cardDeckPanel.AddComponent<Image>();
                cardDeckBlockerImage.color = new Color(0f, 0f, 0f, 0f);
            }
        }

        if (cardDeckBlockerImage != null)
        {
            // dam bao image panel chan raycast xuong UI phia duoi
            cardDeckBlockerImage.raycastTarget = true;
        }
    }

    private void ApplyPanelInputState(bool isOpen)
    {
        if (cardDeckCanvasGroup != null)
        {
            cardDeckCanvasGroup.interactable = isOpen;
            cardDeckCanvasGroup.blocksRaycasts = isOpen;
        }

        if (cardDeckBlockerImage != null)
        {
            cardDeckBlockerImage.raycastTarget = isOpen;
        }

        if (disableCardInteractionWhenPanelOpen)
        {
            CardInteraction.SetGlobalInteractionLocked(isOpen);
        }

    }

    private bool IsCurrentTurnByName(string playerName)
    {
        if (GameManager.Instant == null || string.IsNullOrEmpty(playerName))
        {
            return false;
        }

        var currentTurnName = GameManager.Instant.CurrentTurnPlayerName;
        if (!string.IsNullOrEmpty(currentTurnName))
        {
            return string.Equals(playerName, currentTurnName, System.StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrEmpty(GameManager.Instant.CurrentTurnPlayerId) && !string.IsNullOrEmpty(GameManager.Instant.PlayerId))
        {
            return string.Equals(GameManager.Instant.CurrentTurnPlayerId, GameManager.Instant.PlayerId, System.StringComparison.OrdinalIgnoreCase)
                   && string.Equals(playerName, GameManager.Instant.Nickname, System.StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private void RefreshTurnPanels()
    {
        if (GameManager.Instant == null)
        {
            return;
        }

        var isLocalActor = !string.IsNullOrEmpty(GameManager.Instant.CurrentActorPlayerId)
            && !string.IsNullOrEmpty(GameManager.Instant.LocalPlayerId)
            && string.Equals(GameManager.Instant.CurrentActorPlayerId, GameManager.Instant.LocalPlayerId, System.StringComparison.OrdinalIgnoreCase);

        var showWaitingPanel = GameManager.Instant.IsWaitingForResponses && isLocalActor;
        var showLiarPanel = GameManager.Instant.IsLiarPanelVisible && !isLocalActor;
        var liarContext = GameManager.Instant.LastLiarPanelContext;
        var revealContext = GameManager.Instant.LastRevealPlayedCards;
        var hasRevealFromServer = revealContext != null
                                  && revealContext.cards != null
                                  && revealContext.cards.Count > 0
                                  && (revealContext.playId <= 0 || GameManager.Instant.CurrentPlayId <= 0 || revealContext.playId == GameManager.Instant.CurrentPlayId);

        var revealLocalActor = hasRevealFromServer
                               && !string.IsNullOrEmpty(revealContext.actorPlayerId)
                               && !string.IsNullOrEmpty(GameManager.Instant.LocalPlayerId)
                               && string.Equals(revealContext.actorPlayerId, GameManager.Instant.LocalPlayerId, System.StringComparison.OrdinalIgnoreCase);

        if (hasRevealFromServer)
        {
            showWaitingPanel = revealLocalActor;
            showLiarPanel = !revealLocalActor;
        }

        if (!showLiarPanel || liarContext == null)
        {
            _shouldRevealLiarCards = false;
            _revealLiarCardsPlayId = 0;
            _revealedLiarCards.Clear();
            _revealedLiarActorName = string.Empty;
        }
        else if (liarContext != null && _revealLiarCardsPlayId > 0 && liarContext.playId > 0 && liarContext.playId != _revealLiarCardsPlayId)
        {
            _shouldRevealLiarCards = false;
            _revealLiarCardsPlayId = 0;
            _revealedLiarCards.Clear();
            _revealedLiarActorName = string.Empty;
        }

        var isLocalDead = GameManager.Instant.IsLocalPlayerDead;

        if (playButton != null)
        {
            playButton.interactable = !isLocalDead && GameManager.Instant.CanLocalPlay() && !_isActionPending;
        }

        if (waitingPanel != null)
        {
            waitingPanel.SetActive(showWaitingPanel);
        }

        if (waitingMessageText != null)
        {
            var waitingMessage = GameManager.Instant.LastWaitingContext != null ? GameManager.Instant.LastWaitingContext.message : string.Empty;
            if (showWaitingPanel && hasRevealFromServer)
            {
                waitingMessage = "Da lat bai. Dang cho ket qua...";
            }
            waitingMessageText.text = string.IsNullOrEmpty(waitingMessage) ? "Dang cho nguoi choi khac phan hoi..." : waitingMessage;
        }

        if (liarPanel != null)
        {
            liarPanel.SetActive(showLiarPanel);
        }

        if (liarPanelMessageText != null)
        {
            if (liarContext != null)
            {
                var actorName = ResolveLiarPanelActorName(liarContext);
                liarPanelMessageText.text = string.Format(liarPanelMessageFormat, actorName);
            }
            else if (showLiarPanel && hasRevealFromServer)
            {
                var actorName = string.IsNullOrEmpty(revealContext.actorPlayerName) ? liarPanelFallbackActorName : revealContext.actorPlayerName;
                liarPanelMessageText.text = string.Format(liarPanelMessageFormat, actorName);
            }
            else
            {
                liarPanelMessageText.text = string.Empty;
            }
        }

        if (skipButton != null)
        {
            skipButton.interactable = !isLocalDead && showLiarPanel && !hasRevealFromServer && !_isActionPending;
        }

        if (liarButton != null)
        {
            liarButton.interactable = !isLocalDead && showLiarPanel && !hasRevealFromServer && !_isActionPending;
        }

        if (resolveResultText != null)
        {
            var resolve = GameManager.Instant.LastResolveResult;
            if (resolve == null)
            {
                resolveResultText.text = string.Empty;
            }
            else
            {
                var punishedPlayerId = ResolvePunishedPlayerId(resolve);
                var roulette = resolve.roulette;
                var rouletteText = roulette != null
                    ? (" | Roulette: " + roulette.stageBefore + "->" + roulette.stageAfter + (roulette.hit ? " HIT" : " MISS") + (roulette.isDead ? " (DEAD)" : " (ALIVE)"))
                    : string.Empty;

                resolveResultText.text = "Ket qua: punished=" + punishedPlayerId + ", reason=" + resolve.reason + ", liar=" + resolve.liar + rouletteText;
            }
        }

        if (errorText != null)
        {
            var error = GameManager.Instant.LastError;
            errorText.text = error != null && !string.IsNullOrEmpty(error.code)
                ? (error.code + ": " + error.message)
                : string.Empty;
        }

        var playedCardCount = ResolvePlayedCardCount();
        if (hasRevealFromServer)
        {
            playedCardCount = revealContext.cards.Count;
        }

        var revealLiarCards = showLiarPanel && hasRevealFromServer;

        List<int> cardsToReveal = null;
        if (hasRevealFromServer)
        {
            cardsToReveal = revealContext.cards;
        }

        var waitingCardsToReveal = hasRevealFromServer && showWaitingPanel ? revealContext.cards : null;

        SetCardFieldCount(cardFieldWait, cardFieldWaitCardBackObjects, showWaitingPanel ? playedCardCount : 0, "WAITING", waitingCardsToReveal);
        SetCardFieldCount(cardFieldLiar, cardFieldLiarCardBackObjects, showLiarPanel ? playedCardCount : 0, "LIAR", revealLiarCards ? cardsToReveal : null);

        if (!showWaitingPanel && !showLiarPanel)
        {
            _isActionPending = false;
        }
    }

    private void BindTurnButtons()
    {
        if (playButton != null)
        {
            playButton.onClick.RemoveListener(OnPlayButtonClicked);
            playButton.onClick.AddListener(OnPlayButtonClicked);
        }

        if (skipButton != null)
        {
            skipButton.onClick.RemoveListener(OnSkipButtonClicked);
            skipButton.onClick.AddListener(OnSkipButtonClicked);
        }

        if (liarButton != null)
        {
            liarButton.onClick.RemoveListener(OnLiarButtonClicked);
            liarButton.onClick.AddListener(OnLiarButtonClicked);
        }
    }

    private void UnbindTurnButtons()
    {
        if (playButton != null)
        {
            playButton.onClick.RemoveListener(OnPlayButtonClicked);
        }

        if (skipButton != null)
        {
            skipButton.onClick.RemoveListener(OnSkipButtonClicked);
        }

        if (liarButton != null)
        {
            liarButton.onClick.RemoveListener(OnLiarButtonClicked);
        }
    }

    private int DetermineDeclaredNumber(List<int> selectedCards)
    {
        if (selectedCards != null && selectedCards.Count > 0)
        {
            return GameManager.NormalizeCardValue(selectedCards[0]);
        }

        return GameManager.NormalizeCardValue(fallbackDeclaredNumber);
    }

    private int ResolvePlayedCardCount()
    {
        if (GameManager.Instant == null)
        {
            return 0;
        }

        var count = Mathf.Max(0, GameManager.Instant.CurrentPlayedCardCount);
        if (count > 0)
        {
            return count;
        }

        var waiting = GameManager.Instant.LastWaitingContext;
        if (waiting != null)
        {
            count = Mathf.Max(count, waiting.playedCardCount);
        }

        var liar = GameManager.Instant.LastLiarPanelContext;
        if (liar != null)
        {
            var liarCount = liar.playedCardCount;
            if (liarCount <= 0 && liar.previewCards != null && liar.previewCards.Count > 0)
            {
                liarCount = liar.previewCards.Count;
            }
            count = Mathf.Max(count, liarCount);
        }

        return Mathf.Max(count, GameManager.Instant.LastPlayCardCount);
    }

    private void SetCardFieldCount(GameObject root, GameObject[] cardBackObjects, int cardCount, string panelName, List<int> revealCards = null)
    {
        if (root != null)
        {
            root.SetActive(cardCount > 0);
        }

        if (cardBackObjects == null || cardBackObjects.Length == 0)
        {
            return;
        }

        var clamped = Mathf.Max(0, cardCount);
        if (clamped > cardBackObjects.Length)
        {
            Debug.LogWarning("[UIGameTable] " + panelName + " card field khong du object de hien thi. Yeu cau=" + clamped + ", hien co=" + cardBackObjects.Length);
        }

        for (int i = 0; i < cardBackObjects.Length; i++)
        {
            var cardObject = cardBackObjects[i];
            if (cardObject == null)
            {
                continue;
            }

            var cardVisual = cardObject.GetComponent<CardVisual>();
            if (cardVisual == null)
            {
                cardVisual = cardObject.GetComponentInChildren<CardVisual>(true);
            }

            var isVisible = i < clamped;
            cardObject.SetActive(isVisible);

            if (!isVisible)
            {
                if (cardVisual != null)
                {
                    cardVisual.ResetToDefaultVisual();
                }
                continue;
            }

            if (revealCards == null || i >= revealCards.Count)
            {
                if (cardVisual != null)
                {
                    cardVisual.ResetToDefaultVisual();
                }
                continue;
            }

            if (cardVisual != null)
            {
                cardVisual.SetCardValue(revealCards[i]);
            }
            else
            {
                Debug.LogWarning("[UIGameTable] " + panelName + " card object khong co CardVisual de lat bai index=" + i);
            }
        }
    }

    private string ResolveLiarPanelActorName(ShowLiarPanelEvent liarContext)
    {
        if (liarContext != null && !string.IsNullOrEmpty(liarContext.actorPlayerName))
        {
            return liarContext.actorPlayerName;
        }

        if (GameManager.Instant != null && !string.IsNullOrEmpty(GameManager.Instant.CurrentTurnPlayerName))
        {
            return GameManager.Instant.CurrentTurnPlayerName;
        }

        return string.IsNullOrEmpty(liarPanelFallbackActorName) ? "Opponent" : liarPanelFallbackActorName;
    }

    private bool IsPlayerDead(string playerName)
    {
        if (GameManager.Instant == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(GameManager.Instant.LocalPlayerId) &&
            !string.IsNullOrEmpty(GameManager.Instant.Nickname) &&
            string.Equals(playerName, GameManager.Instant.Nickname, System.StringComparison.OrdinalIgnoreCase))
        {
            return GameManager.Instant.IsLocalPlayerDead;
        }

        return GameManager.Instant.IsPlayerDeadByName(playerName);
    }

    private static string ResolvePunishedPlayerId(ResolveResultEvent resolve)
    {
        if (resolve == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(resolve.punishedPlayerId))
        {
            return resolve.punishedPlayerId.Trim();
        }

        return resolve.liar ? (resolve.accusedPlayerId ?? string.Empty) : (resolve.accuserPlayerId ?? string.Empty);
    }
}
