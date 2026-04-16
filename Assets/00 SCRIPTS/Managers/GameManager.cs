using UnityEngine;
using System;
using System.Collections.Generic;

public class GameManager : Singleton<GameManager>
{
    // Thong tin nguoi choi
    public string PlayerId { get; private set; }
    public string Nickname { get; private set; }
    public string RoomId { get; private set; }
    public string HostId { get; private set; }
    public string HostNickname { get; private set; }
    public int SelectedAvatarId { get; private set; }
    public IReadOnlyList<string> RoomPlayers => _roomPlayers;
    public IReadOnlyList<string> GamePlayers => _gamePlayers;
    public IReadOnlyList<int> LocalHandCards => _localHandCards;
    public int DestinyCardValue { get; private set; } = -1;
    public string CurrentTurnPlayerId { get; private set; }
    public string CurrentTurnPlayerName { get; private set; }
    public int CurrentTurnIndex { get; private set; } = -1;
    public string CurrentPhase { get; private set; } = string.Empty;
    public int CurrentPlayId { get; private set; }
    public int CurrentPlayedCardCount { get; private set; }
    public string CurrentActorPlayerId { get; private set; } = string.Empty;
    public string CurrentRoomId => RoomId;
    public string LocalPlayerId => PlayerId;
    public IReadOnlyList<string> TurnOrderPlayerIds => _turnOrderPlayerIds;
    public int LastPlayCardCount { get; private set; }
    public ShowLiarPanelEvent LastLiarPanelContext { get; private set; }
    public ShowWaitingEvent LastWaitingContext { get; private set; }
    public RevealPlayedCardsEvent LastRevealPlayedCards { get; private set; }
    public ResolveResultEvent LastResolveResult { get; private set; }
    public RoundResetEvent LastRoundReset { get; private set; }
    public ErrorEvent LastError { get; private set; }
    public bool IsWaitingForResponses { get; private set; }
    public bool IsLiarPanelVisible { get; private set; }
    public bool IsLocalPlayerDead => IsPlayerDeadById(PlayerId) || IsPlayerDeadByName(Nickname);
    public int LatestRoundResetPlayId => _latestRoundResetPlayId;

    public static event Action RoomInfoChanged;
    public static event Action GameInfoChanged;
    public static event Action TurnInfoChanged;

    private readonly List<string> _roomPlayers = new List<string>();
    private readonly List<string> _gamePlayers = new List<string>();
    private readonly List<int> _localHandCards = new List<int>();
    private readonly List<string> _turnOrderPlayerIds = new List<string>();
    private readonly Dictionary<string, int> _playerCardCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _playerAvatarIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _playerGunShotsById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _playerGunShotsByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _playerDeadById = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _playerDeadByName = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _playerNamesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private int _latestLiarPanelPlayId;
    private int _latestResolvedPlayId;
    private int _latestRoundResetPlayId;

    private void Start()
    {
        // Chi khoi tao neu day la instance chinh
        if (Instant != this) return;

        DontDestroyOnLoad(gameObject);

        Debug.Log("[GameManager] Da khoi tao");
    }

    // Ham luu thong tin nguoi choi sau khi dang nhap thanh cong (CHUA CO ROOM)
    public void SetPlayerInfo(string playerId, string nickname, int avatarId)
    {
        PlayerId = playerId;
        Nickname = nickname;
        SelectedAvatarId = Mathf.Max(0, avatarId);

        if (!string.IsNullOrEmpty(PlayerId) && !string.IsNullOrEmpty(Nickname))
        {
            _playerNamesById[PlayerId] = Nickname;
        }

        if (!string.IsNullOrEmpty(PlayerId))
        {
            _playerDeadById[PlayerId] = false;
            _playerGunShotsById[PlayerId] = 0;
        }

        if (!string.IsNullOrEmpty(Nickname))
        {
            _playerDeadByName[Nickname] = false;
            _playerGunShotsByName[Nickname] = 0;
        }

        if (!string.IsNullOrEmpty(Nickname))
        {
            _playerAvatarIds[Nickname] = SelectedAvatarId;
        }

        Debug.Log($"[GameManager] Da luu thong tin nguoi choi: ID={PlayerId}, Name={Nickname}, AvatarId={SelectedAvatarId}");
    }

    public void SetPlayerInfo(string playerId, string nickname)
    {
        SetPlayerInfo(playerId, nickname, SelectedAvatarId);
    }

    public void SetSelectedAvatarId(int avatarId)
    {
        SelectedAvatarId = Mathf.Max(0, avatarId);

        if (!string.IsNullOrEmpty(Nickname))
        {
            _playerAvatarIds[Nickname] = SelectedAvatarId;
        }

        RoomInfoChanged?.Invoke();
    }

    // Ham luu roomId khi nguoi choi tao/join room
    public void SetRoomId(string roomId)
    {
        RoomId = roomId;
        Debug.Log($"[GameManager] Da join room: {RoomId}");
        RoomInfoChanged?.Invoke();
    }

    public bool TryStartGame(int initialCardCount, out string errorMessage)
    {
        return TryStartGame(initialCardCount, null, out errorMessage);
    }

    public bool TryStartGame(int initialCardCount, IReadOnlyList<string> authoritativePlayers, out string errorMessage)
    {
        errorMessage = string.Empty;

        var roomPlayers = BuildOrderedRoomPlayers(authoritativePlayers);
        if (roomPlayers.Count < 2 || roomPlayers.Count > 4)
        {
            errorMessage = "So nguoi choi phai tu 2 den 4";
            return false;
        }

        if (initialCardCount < 0)
        {
            initialCardCount = 0;
        }

        _gamePlayers.Clear();
        _gamePlayers.AddRange(roomPlayers);

        _localHandCards.Clear();
        ResetTurnState();

        DestinyCardValue = -1;
        LastRoundReset = null;
        _playerGunShotsById.Clear();
        _playerGunShotsByName.Clear();

        _playerCardCounts.Clear();
        for (int i = 0; i < _gamePlayers.Count; i++)
        {
            var nickname = NormalizePlayerKey(_gamePlayers[i]);
            if (!string.IsNullOrEmpty(nickname))
            {
                _playerCardCounts[nickname] = initialCardCount;
            }
        }

        GameInfoChanged?.Invoke();
        return true;
    }

    public void SetLocalHandCards(List<int> cards)
    {
        _localHandCards.Clear();

        if (cards != null)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                _localHandCards.Add(NormalizeCardValue(cards[i]));
            }
        }

        if (!string.IsNullOrEmpty(Nickname))
        {
            _playerCardCounts[NormalizePlayerKey(Nickname)] = _localHandCards.Count;
        }

        GameInfoChanged?.Invoke();
    }

    public static int NormalizeCardValue(int cardValue)
    {
        if (cardValue == 0 || cardValue == 1 || cardValue == 2 || cardValue == 3)
        {
            return cardValue;
        }

        if (cardValue == 4)
        {
            Debug.LogWarning("[GameManager] Nhan card value = 4 (legacy special), map ve 3.");
            return 3;
        }

        Debug.LogWarning("[GameManager] Card value khong hop le: " + cardValue + ", fallback ve 0.");
        return 0;
    }

    private static string ResolveRoomPlayerName(RoomPlayerState player)
    {
        if (player == null)
        {
            return string.Empty;
        }

        var byNickname = NormalizePlayerKey(player.nickname);
        if (!string.IsNullOrEmpty(byNickname))
        {
            return byNickname;
        }

        return NormalizePlayerKey(player.playerName);
    }

    private static string ResolveRoundResetHandName(RoundResetHandEvent hand)
    {
        if (hand == null)
        {
            return string.Empty;
        }

        var byNickname = NormalizePlayerKey(hand.nickname);
        if (!string.IsNullOrEmpty(byNickname))
        {
            return byNickname;
        }

        return NormalizePlayerKey(hand.playerName);
    }

    public int GetPlayerGunShotCount(string nickname)
    {
        var playerKey = NormalizePlayerKey(nickname);
        if (string.IsNullOrEmpty(playerKey))
        {
            return 0;
        }

        if (_playerGunShotsByName.TryGetValue(playerKey, out var shotCount))
        {
            return Mathf.Clamp(shotCount, 0, 6);
        }

        return 0;
    }

    public int GetPlayerCardCount(string nickname)
    {
        var playerKey = NormalizePlayerKey(nickname);
        if (string.IsNullOrEmpty(playerKey))
        {
            return 0;
        }

        if (_playerCardCounts.TryGetValue(playerKey, out var cardCount))
        {
            return cardCount;
        }

        return 0;
    }

    public void SetPlayerCardCount(string nickname, int cardCount)
    {
        var playerKey = NormalizePlayerKey(nickname);
        if (string.IsNullOrEmpty(playerKey))
        {
            return;
        }

        _playerCardCounts[playerKey] = Mathf.Max(0, cardCount);
        GameInfoChanged?.Invoke();
    }

    public void ClearGameInfo()
    {
        _gamePlayers.Clear();
        _localHandCards.Clear();
        ResetTurnState();
        LastRoundReset = null;
        _playerCardCounts.Clear();
        _playerGunShotsById.Clear();
        _playerGunShotsByName.Clear();
        DestinyCardValue = -1;
        GameInfoChanged?.Invoke();
    }

    public void SetDestinyCardValue(int destinyCardValue)
    {
        if (destinyCardValue < 0 || destinyCardValue > 2)
        {
            Debug.LogWarning("[GameManager] destinyCardValue khong hop le: " + destinyCardValue + ", fallback ve 0.");
            destinyCardValue = 0;
        }

        DestinyCardValue = destinyCardValue;
        GameInfoChanged?.Invoke();
    }

    private List<string> BuildOrderedRoomPlayers(IReadOnlyList<string> authoritativePlayers = null)
    {
        var result = new List<string>();

        if (authoritativePlayers != null && authoritativePlayers.Count > 0)
        {
            for (int i = 0; i < authoritativePlayers.Count; i++)
            {
                var player = authoritativePlayers[i];
                if (string.IsNullOrEmpty(player) || ContainsPlayerName(result, player))
                {
                    continue;
                }

                result.Add(player);
            }

            return result;
        }

        if (!string.IsNullOrEmpty(HostNickname))
        {
            result.Add(HostNickname);
        }

        for (int i = 0; i < _roomPlayers.Count; i++)
        {
            var player = _roomPlayers[i];
            if (string.IsNullOrEmpty(player) || ContainsPlayerName(result, player))
            {
                continue;
            }

            result.Add(player);
        }

        if (!string.IsNullOrEmpty(Nickname) && !ContainsPlayerName(result, Nickname))
        {
            result.Add(Nickname);
        }

        return result;
    }

    public void SetRoomHostInfo(string hostId, string hostNickname)
    {
        HostId = hostId;
        HostNickname = hostNickname;
        RoomInfoChanged?.Invoke();
    }

    public void SetTurnUpdate(TurnUpdateEvent payload)
    {
        if (payload == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(payload.roomId))
        {
            RoomId = payload.roomId;
        }

        CurrentTurnPlayerId = payload.currentTurnPlayerId ?? string.Empty;
        CurrentTurnPlayerName = payload.currentTurnPlayerName ?? string.Empty;
        CurrentTurnIndex = payload.currentTurnIndex;
        CurrentPhase = payload.phase ?? string.Empty;

        if (!string.IsNullOrEmpty(CurrentTurnPlayerId) && !string.IsNullOrEmpty(CurrentTurnPlayerName))
        {
            _playerNamesById[CurrentTurnPlayerId] = CurrentTurnPlayerName;
        }

        if (payload.destinyCard >= 0)
        {
            SetDestinyCardValue(payload.destinyCard);
        }

        _turnOrderPlayerIds.Clear();
        if (payload.turnOrderPlayerIds != null)
        {
            for (int i = 0; i < payload.turnOrderPlayerIds.Count; i++)
            {
                var playerId = payload.turnOrderPlayerIds[i];
                if (string.IsNullOrEmpty(playerId))
                {
                    continue;
                }

                _turnOrderPlayerIds.Add(playerId);
            }
        }

        IsWaitingForResponses = false;
        IsLiarPanelVisible = false;
        LastPlayCardCount = 0;
        CurrentPlayId = 0;
        CurrentPlayedCardCount = 0;
        CurrentActorPlayerId = string.Empty;
        _latestLiarPanelPlayId = 0;
        LastWaitingContext = null;
        LastLiarPanelContext = null;
        LastRevealPlayedCards = null;
        LastResolveResult = null;

        TurnInfoChanged?.Invoke();
        GameInfoChanged?.Invoke();
    }

    public void SetRoundReset(RoundResetEvent payload)
    {
        if (payload == null)
        {
            return;
        }

        if (payload.playId > 0 && _latestRoundResetPlayId > 0 && payload.playId <= _latestRoundResetPlayId)
        {
            Debug.LogWarning("[GameManager] Bo qua ROUND_RESET duplicate/cu: playId=" + payload.playId + ", latestRoundResetPlayId=" + _latestRoundResetPlayId);
            return;
        }

        LastRoundReset = payload;
        if (payload.playId > 0)
        {
            _latestRoundResetPlayId = payload.playId;
            if (payload.playId > _latestResolvedPlayId)
            {
                _latestResolvedPlayId = payload.playId;
            }
        }

        if (payload.destinyCard >= 0)
        {
            SetDestinyCardValue(payload.destinyCard);
        }

        var deadIds = payload.deadPlayerIds != null
            ? new HashSet<string>(payload.deadPlayerIds, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var localHandApplied = false;
        if (payload.hands != null)
        {
            for (int i = 0; i < payload.hands.Count; i++)
            {
                var hand = payload.hands[i];
                if (hand == null)
                {
                    continue;
                }

                var handPlayerName = ResolveRoundResetHandName(hand);
                var handPlayerId = string.IsNullOrWhiteSpace(hand.playerId) ? string.Empty : hand.playerId.Trim();
                var cards = hand.cards ?? new List<int>();

                if (!string.IsNullOrEmpty(handPlayerId) && !string.IsNullOrEmpty(handPlayerName))
                {
                    _playerNamesById[handPlayerId] = handPlayerName;
                }

                if (!string.IsNullOrEmpty(handPlayerName))
                {
                    _playerCardCounts[handPlayerName] = Mathf.Max(0, cards.Count);
                }

                var isDead = !string.IsNullOrEmpty(handPlayerId)
                    ? deadIds.Contains(handPlayerId)
                    : IsPlayerDeadByName(handPlayerName);
                SetPlayerDeadState(handPlayerId, handPlayerName, isDead, false);

                var isLocal = (!string.IsNullOrEmpty(PlayerId) && !string.IsNullOrEmpty(handPlayerId) && string.Equals(PlayerId, handPlayerId, StringComparison.OrdinalIgnoreCase))
                              || (!string.IsNullOrEmpty(Nickname) && !string.IsNullOrEmpty(handPlayerName) && string.Equals(Nickname, handPlayerName, StringComparison.OrdinalIgnoreCase));

                if (isLocal)
                {
                    SetLocalHandCards(cards);
                    localHandApplied = true;
                }
            }
        }

        foreach (var deadId in deadIds)
        {
            var deadPlayerId = string.IsNullOrWhiteSpace(deadId) ? string.Empty : deadId.Trim();
            if (string.IsNullOrEmpty(deadPlayerId))
            {
                continue;
            }

            var deadName = GetPlayerNameById(deadPlayerId);
            SetPlayerDeadState(deadPlayerId, deadName, true, false);
            SetPlayerCardCountById(deadPlayerId, 0, false);
        }

        if (!localHandApplied && IsLocalPlayerDead)
        {
            SetLocalHandCards(new List<int>());
        }

        TurnInfoChanged?.Invoke();
        GameInfoChanged?.Invoke();
    }

    public void SetShowWaiting(ShowWaitingEvent payload)
    {
        if (payload != null && payload.playId > 0 && CurrentPlayId > 0 && payload.playId <= CurrentPlayId)
        {
            Debug.LogWarning("[GameManager] Bo qua SHOW_WAITING cu: playId=" + payload.playId + ", currentPlayId=" + CurrentPlayId);
            return;
        }

        if (payload != null && payload.playId > 0)
        {
            _latestLiarPanelPlayId = payload.playId;
        }

        LastWaitingContext = payload;
        IsWaitingForResponses = payload != null;
        IsLiarPanelVisible = false;
        LastLiarPanelContext = null;
        LastRevealPlayedCards = null;
        if (payload != null)
        {
            CurrentPhase = string.IsNullOrEmpty(payload.phase) ? "WaitingResponses" : payload.phase;
            CurrentPlayId = Mathf.Max(0, payload.playId);
            CurrentPlayedCardCount = Mathf.Max(0, payload.playedCardCount);
            CurrentActorPlayerId = payload.actorPlayerId ?? string.Empty;
            LastPlayCardCount = CurrentPlayedCardCount;

            if (!string.IsNullOrEmpty(CurrentActorPlayerId) && !string.IsNullOrEmpty(CurrentTurnPlayerName))
            {
                _playerNamesById[CurrentActorPlayerId] = CurrentTurnPlayerName;
            }
        }
        if (payload == null)
        {
            LastPlayCardCount = 0;
            CurrentPlayId = 0;
            CurrentPlayedCardCount = 0;
            CurrentActorPlayerId = string.Empty;
        }

        TurnInfoChanged?.Invoke();
        GameInfoChanged?.Invoke();
    }

    public void SetShowLiarPanel(ShowLiarPanelEvent payload)
    {
        if (payload != null && payload.playId > 0 && CurrentPlayId > 0 && payload.playId <= CurrentPlayId)
        {
            Debug.LogWarning("[GameManager] Bo qua SHOW_LIAR_PANEL cu: playId=" + payload.playId + ", currentPlayId=" + CurrentPlayId);
            return;
        }

        if (payload != null && payload.playId > 0)
        {
            _latestLiarPanelPlayId = payload.playId;
        }

        LastLiarPanelContext = payload;
        IsLiarPanelVisible = payload != null;
        IsWaitingForResponses = false;
        LastWaitingContext = null;
        LastRevealPlayedCards = null;

        if (payload != null)
        {
            var playedCardCount = Mathf.Max(0, payload.playedCardCount);

            if (playedCardCount <= 0 && payload.previewCards != null && payload.previewCards.Count > 0)
            {
                playedCardCount = Mathf.Max(0, payload.previewCards.Count);
            }

            if (payload.previewCards != null && payload.previewCards.Count > 0 && payload.playedCardCount != payload.previewCards.Count)
            {
                Debug.LogWarning("[GameManager] SHOW_LIAR_PANEL count mismatch: playedCardCount=" + payload.playedCardCount + ", previewCards.Count=" + payload.previewCards.Count + ". Su dung previewCards.Count.");
            }

            LastPlayCardCount = playedCardCount;
            CurrentPlayId = Mathf.Max(0, payload.playId);
            CurrentPlayedCardCount = playedCardCount;
            CurrentActorPlayerId = payload.actorPlayerId ?? string.Empty;

            if (!string.IsNullOrEmpty(CurrentActorPlayerId) && !string.IsNullOrEmpty(payload.actorPlayerName))
            {
                _playerNamesById[CurrentActorPlayerId] = payload.actorPlayerName;
            }

            var actorName = NormalizePlayerKey(payload.actorPlayerName);
            if (string.IsNullOrEmpty(actorName))
            {
                actorName = NormalizePlayerKey(CurrentTurnPlayerName);
            }

            if (!string.IsNullOrEmpty(actorName))
            {
                var currentCount = GetPlayerCardCount(actorName);
                _playerCardCounts[actorName] = Mathf.Max(0, currentCount - playedCardCount);
            }
        }
        else
        {
            LastPlayCardCount = 0;
            CurrentPlayId = 0;
            CurrentPlayedCardCount = 0;
            CurrentActorPlayerId = string.Empty;
        }

        if (payload != null && !string.IsNullOrEmpty(payload.phase))
        {
            CurrentPhase = payload.phase;
        }

        TurnInfoChanged?.Invoke();
        GameInfoChanged?.Invoke();
    }

    public void SetRevealPlayedCards(RevealPlayedCardsEvent payload)
    {
        if (payload == null)
        {
            return;
        }

        if (payload.playId > 0 && LastRevealPlayedCards != null && payload.playId == LastRevealPlayedCards.playId)
        {
            Debug.LogWarning("[GameManager] Bo qua REVEAL_PLAYED_CARDS duplicate: playId=" + payload.playId);
            return;
        }

        if (payload.playId > 0 && CurrentPlayId > 0 && payload.playId < CurrentPlayId)
        {
            Debug.LogWarning("[GameManager] Bo qua REVEAL_PLAYED_CARDS cu: playId=" + payload.playId + ", currentPlayId=" + CurrentPlayId);
            return;
        }

        if (payload.playId > 0 && _latestResolvedPlayId > 0 && payload.playId <= _latestResolvedPlayId)
        {
            Debug.LogWarning("[GameManager] Bo qua REVEAL_PLAYED_CARDS da resolve: playId=" + payload.playId + ", latestResolvedPlayId=" + _latestResolvedPlayId);
            return;
        }

        LastRevealPlayedCards = payload;

        if (payload.playId > 0)
        {
            CurrentPlayId = payload.playId;
            _latestLiarPanelPlayId = payload.playId;
        }

        CurrentActorPlayerId = payload.actorPlayerId ?? string.Empty;

        if (!string.IsNullOrEmpty(payload.actorPlayerId) && !string.IsNullOrEmpty(payload.actorPlayerName))
        {
            _playerNamesById[payload.actorPlayerId] = payload.actorPlayerName;
        }

        var revealedCardCount = payload.cards != null ? payload.cards.Count : 0;
        CurrentPlayedCardCount = Mathf.Max(0, revealedCardCount);
        LastPlayCardCount = CurrentPlayedCardCount;

        TurnInfoChanged?.Invoke();
        GameInfoChanged?.Invoke();
    }

    public void SetResolveResult(ResolveResultEvent payload)
    {
        if (payload == null)
        {
            return;
        }

        if (payload.playId > 0 && _latestResolvedPlayId > 0 && payload.playId <= _latestResolvedPlayId)
        {
            Debug.LogWarning("[GameManager] Bo qua RESOLVE_RESULT duplicate/cu: playId=" + payload.playId + ", latestResolvedPlayId=" + _latestResolvedPlayId);
            return;
        }

        LastResolveResult = payload;

        if (payload.playId > 0)
        {
            _latestResolvedPlayId = payload.playId;
        }

        if (payload.destinyCard >= 0)
        {
            SetDestinyCardValue(payload.destinyCard);
        }

        var punishedId = ResolvePunishedPlayerId(payload);
        if (!string.IsNullOrEmpty(punishedId) && payload.roulette != null)
        {
            var punishedName = GetPlayerNameById(punishedId);
            SetPlayerGunShotCountById(punishedId, CalculateGunShotCount(payload.roulette), false);
            SetPlayerDeadState(punishedId, punishedName, payload.roulette.isDead, false);
            if (payload.roulette.isDead)
            {
                SetPlayerCardCountById(punishedId, 0, false);
            }
        }

        IsWaitingForResponses = false;
        IsLiarPanelVisible = false;
        LastWaitingContext = null;
        LastLiarPanelContext = null;
        LastRevealPlayedCards = null;
        LastPlayCardCount = 0;
        CurrentPlayId = 0;
        CurrentPlayedCardCount = 0;
        CurrentActorPlayerId = string.Empty;
        _latestLiarPanelPlayId = 0;

        TurnInfoChanged?.Invoke();
        GameInfoChanged?.Invoke();
    }

    public void SetError(ErrorEvent payload)
    {
        LastError = payload;

        if (payload != null &&
            (string.Equals(payload.code, "NOT_YOUR_TURN", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(payload.code, "INVALID_PHASE", StringComparison.OrdinalIgnoreCase)))
        {
            IsWaitingForResponses = false;
            IsLiarPanelVisible = false;
            LastWaitingContext = null;
            LastLiarPanelContext = null;
            LastRevealPlayedCards = null;
            LastPlayCardCount = 0;
            CurrentPlayId = 0;
            CurrentPlayedCardCount = 0;
            CurrentActorPlayerId = string.Empty;
        }

        TurnInfoChanged?.Invoke();
    }

    public void SetLocalLastPlayCardCount(int count)
    {
        LastPlayCardCount = Mathf.Max(0, count);
        TurnInfoChanged?.Invoke();
    }

    public bool CanLocalPlay()
    {
        if (IsLocalPlayerDead)
        {
            return false;
        }

        if (!string.Equals(CurrentPhase, "WaitingPlay", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(PlayerId) && !string.IsNullOrEmpty(CurrentTurnPlayerId))
        {
            return string.Equals(PlayerId, CurrentTurnPlayerId, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrEmpty(Nickname) && !string.IsNullOrEmpty(CurrentTurnPlayerName))
        {
            return string.Equals(Nickname, CurrentTurnPlayerName, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public bool IsPlayerDeadById(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return false;
        }

        return _playerDeadById.TryGetValue(playerId.Trim(), out var isDead) && isDead;
    }

    public bool IsPlayerDeadByName(string playerName)
    {
        var key = NormalizePlayerKey(playerName);
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        return _playerDeadByName.TryGetValue(key, out var isDead) && isDead;
    }

    private string ResolvePunishedPlayerId(ResolveResultEvent payload)
    {
        if (payload == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(payload.punishedPlayerId))
        {
            return payload.punishedPlayerId.Trim();
        }

        return payload.liar ? (payload.accusedPlayerId ?? string.Empty) : (payload.accuserPlayerId ?? string.Empty);
    }

    private string GetPlayerNameById(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return string.Empty;
        }

        if (_playerNamesById.TryGetValue(playerId.Trim(), out var playerName))
        {
            return playerName;
        }

        return string.Empty;
    }

    private void SetPlayerDeadState(string playerId, string playerName, bool isDead, bool invokeGameInfoChanged = true)
    {
        if (!string.IsNullOrWhiteSpace(playerId))
        {
            _playerDeadById[playerId.Trim()] = isDead;
        }

        var normalizedName = NormalizePlayerKey(playerName);
        if (!string.IsNullOrEmpty(normalizedName))
        {
            _playerDeadByName[normalizedName] = isDead;
        }

        if (invokeGameInfoChanged)
        {
            GameInfoChanged?.Invoke();
        }
    }

    private int CalculateGunShotCount(RouletteResultEvent roulette)
    {
        if (roulette == null)
        {
            return 0;
        }

        var stageBefore = Mathf.Max(0, roulette.stageBefore);
        var stageAfter = Mathf.Max(0, roulette.stageAfter);

        if (roulette.hit)
        {
            if (stageBefore > 0)
            {
                return Mathf.Clamp(stageBefore, 0, 6);
            }

            return Mathf.Clamp(stageAfter, 0, 6);
        }

        if (stageAfter > 0)
        {
            return Mathf.Clamp(stageAfter - 1, 0, 6);
        }

        return Mathf.Clamp(stageBefore, 0, 6);
    }

    private void SetPlayerGunShotCountById(string playerId, int shotCount, bool invokeGameInfoChanged = true)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        var normalizedId = playerId.Trim();
        var clamped = Mathf.Clamp(shotCount, 0, 6);
        _playerGunShotsById[normalizedId] = clamped;

        var playerName = GetPlayerNameById(normalizedId);
        var key = NormalizePlayerKey(playerName);
        if (!string.IsNullOrEmpty(key))
        {
            _playerGunShotsByName[key] = clamped;
        }

        if (invokeGameInfoChanged)
        {
            GameInfoChanged?.Invoke();
        }
    }

    private void SetPlayerCardCountById(string playerId, int cardCount, bool invokeGameInfoChanged = true)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        var playerName = GetPlayerNameById(playerId);
        var key = NormalizePlayerKey(playerName);
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        _playerCardCounts[key] = Mathf.Max(0, cardCount);
        if (invokeGameInfoChanged)
        {
            GameInfoChanged?.Invoke();
        }
    }

    private static string NormalizePlayerKey(string playerName)
    {
        return string.IsNullOrWhiteSpace(playerName) ? string.Empty : playerName.Trim();
    }

    private void ResetTurnState()
    {
        CurrentTurnPlayerId = string.Empty;
        CurrentTurnPlayerName = string.Empty;
        CurrentTurnIndex = -1;
        CurrentPhase = string.Empty;
        _turnOrderPlayerIds.Clear();
        LastLiarPanelContext = null;
        LastWaitingContext = null;
        LastRevealPlayedCards = null;
        LastResolveResult = null;
        LastError = null;
        IsWaitingForResponses = false;
        IsLiarPanelVisible = false;
        LastPlayCardCount = 0;
        CurrentPlayId = 0;
        CurrentPlayedCardCount = 0;
        CurrentActorPlayerId = string.Empty;
        _latestLiarPanelPlayId = 0;
        _latestResolvedPlayId = 0;
        _latestRoundResetPlayId = 0;
        TurnInfoChanged?.Invoke();
    }

    public void SetRoomPlayers(List<string> players)
    {
        var states = new List<RoomPlayerState>();

        if (players != null)
        {
            for (int i = 0; i < players.Count; i++)
            {
                var playerName = players[i];
                if (string.IsNullOrEmpty(playerName))
                {
                    continue;
                }

                int avatarId = 0;
                if (_playerAvatarIds.TryGetValue(playerName, out var existingAvatarId))
                {
                    avatarId = existingAvatarId;
                }
                else if (!string.IsNullOrEmpty(Nickname) && string.Equals(playerName, Nickname, StringComparison.OrdinalIgnoreCase))
                {
                    avatarId = SelectedAvatarId;
                }

                states.Add(new RoomPlayerState
                {
                    playerId = string.Empty,
                    playerName = playerName,
                    avatarId = avatarId,
                    cardCount = GetPlayerCardCount(playerName),
                    isDead = IsPlayerDeadByName(playerName)
                });
            }
        }

        OnRoomState(states);
    }

    public void OnPlayerJoined(RoomPlayerState player)
    {
        var resolvedName = ResolveRoomPlayerName(player);
        if (player == null || string.IsNullOrEmpty(resolvedName))
        {
            return;
        }

        var avatarId = Mathf.Max(0, player.avatarId);
        _playerAvatarIds[resolvedName] = avatarId;

        if (!string.IsNullOrWhiteSpace(player.playerId))
        {
            _playerNamesById[player.playerId.Trim()] = resolvedName;
            _playerDeadById[player.playerId.Trim()] = player.isDead;

            if (!_playerGunShotsById.ContainsKey(player.playerId.Trim()))
            {
                _playerGunShotsById[player.playerId.Trim()] = 0;
            }
        }

        _playerDeadByName[resolvedName] = player.isDead;

        var playerNameKey = NormalizePlayerKey(resolvedName);
        if (!string.IsNullOrEmpty(playerNameKey) && !_playerGunShotsByName.ContainsKey(playerNameKey))
        {
            _playerGunShotsByName[playerNameKey] = 0;
        }

        if (player.cardCount >= 0)
        {
            _playerCardCounts[playerNameKey] = Mathf.Max(0, player.cardCount);
        }

        if (!ContainsPlayerName(_roomPlayers, resolvedName))
        {
            _roomPlayers.Add(resolvedName);
        }

        RoomInfoChanged?.Invoke();
    }

    public void OnRoomState(List<RoomPlayerState> players)
    {
        var previousGunShotsById = new Dictionary<string, int>(_playerGunShotsById, StringComparer.OrdinalIgnoreCase);
        var previousGunShotsByName = new Dictionary<string, int>(_playerGunShotsByName, StringComparer.OrdinalIgnoreCase);

        _roomPlayers.Clear();

        var keepLocalName = Nickname;
        var keepLocalAvatarId = Mathf.Max(0, SelectedAvatarId);
        _playerAvatarIds.Clear();

        if (!string.IsNullOrEmpty(keepLocalName))
        {
            _playerAvatarIds[keepLocalName] = keepLocalAvatarId;
        }

        _playerDeadByName.Clear();
        _playerDeadById.Clear();
        _playerNamesById.Clear();
        _playerGunShotsByName.Clear();
        _playerGunShotsById.Clear();

        if (!string.IsNullOrEmpty(PlayerId) && !string.IsNullOrEmpty(keepLocalName))
        {
            _playerNamesById[PlayerId] = keepLocalName;

            if (previousGunShotsById.TryGetValue(PlayerId, out var localShotsById))
            {
                _playerGunShotsById[PlayerId] = Mathf.Clamp(localShotsById, 0, 6);
            }

            var localKey = NormalizePlayerKey(keepLocalName);
            if (!string.IsNullOrEmpty(localKey) && previousGunShotsByName.TryGetValue(localKey, out var localShotsByName))
            {
                _playerGunShotsByName[localKey] = Mathf.Clamp(localShotsByName, 0, 6);
            }
        }

        if (players != null)
        {
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                var resolvedName = ResolveRoomPlayerName(player);
                if (player == null || string.IsNullOrEmpty(resolvedName))
                {
                    continue;
                }

                if (!ContainsPlayerName(_roomPlayers, resolvedName))
                {
                    _roomPlayers.Add(resolvedName);
                }

                _playerAvatarIds[resolvedName] = Mathf.Max(0, player.avatarId);

                if (!string.IsNullOrWhiteSpace(player.playerId))
                {
                    var normalizedId = player.playerId.Trim();
                    _playerNamesById[normalizedId] = resolvedName;
                    _playerDeadById[normalizedId] = player.isDead;

                    if (previousGunShotsById.TryGetValue(normalizedId, out var previousShotsById))
                    {
                        _playerGunShotsById[normalizedId] = Mathf.Clamp(previousShotsById, 0, 6);
                    }
                    else
                    {
                        _playerGunShotsById[normalizedId] = 0;
                    }
                }

                _playerDeadByName[resolvedName] = player.isDead;
                var normalizedName = NormalizePlayerKey(resolvedName);
                if (!string.IsNullOrEmpty(normalizedName) && previousGunShotsByName.TryGetValue(normalizedName, out var previousShotsByName))
                {
                    _playerGunShotsByName[normalizedName] = Mathf.Clamp(previousShotsByName, 0, 6);
                }
                else if (!string.IsNullOrEmpty(normalizedName))
                {
                    _playerGunShotsByName[normalizedName] = 0;
                }

                if (player.cardCount >= 0)
                {
                    _playerCardCounts[normalizedName] = Mathf.Max(0, player.cardCount);
                }
            }
        }

        RoomInfoChanged?.Invoke();
    }

    public int GetPlayerAvatarId(string playerName)
    {
        if (string.IsNullOrEmpty(playerName))
        {
            return 0;
        }

        if (_playerAvatarIds.TryGetValue(playerName, out var avatarId))
        {
            return Mathf.Max(0, avatarId);
        }

        if (!string.IsNullOrEmpty(Nickname) && string.Equals(playerName, Nickname, StringComparison.OrdinalIgnoreCase))
        {
            return Mathf.Max(0, SelectedAvatarId);
        }

        return 0;
    }

    private static bool ContainsPlayerName(List<string> source, string playerName)
    {
        if (source == null || source.Count == 0 || string.IsNullOrEmpty(playerName))
        {
            return false;
        }

        for (int i = 0; i < source.Count; i++)
        {
            if (string.Equals(source[i], playerName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public void ClearRoomId()
    {
        RoomId = string.Empty;
        HostId = string.Empty;
        HostNickname = string.Empty;
        _roomPlayers.Clear();
        ClearGameInfo();
        Debug.Log("[GameManager] Da roi room");
        RoomInfoChanged?.Invoke();
    }

    // Ham xoa thong tin khi dang xuat
    public void ClearPlayerInfo()
    {
        PlayerId = string.Empty;
        Nickname = string.Empty;
        RoomId = string.Empty;
        HostId = string.Empty;
        HostNickname = string.Empty;
        SelectedAvatarId = 0;
        _roomPlayers.Clear();
        _playerAvatarIds.Clear();
        _playerDeadById.Clear();
        _playerDeadByName.Clear();
        _playerNamesById.Clear();
        _playerGunShotsById.Clear();
        _playerGunShotsByName.Clear();
        ClearGameInfo();

        Debug.Log("[GameManager] Da xoa thong tin nguoi choi");
        RoomInfoChanged?.Invoke();
    }

    // Kiem tra da dang nhap chua
    public bool IsLoggedIn()
    {
        return !string.IsNullOrEmpty(PlayerId);
    }

    // Kiem tra da o trong room chua
    public bool IsInRoom()
    {
        return !string.IsNullOrEmpty(RoomId);
    }
}
