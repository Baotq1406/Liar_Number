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

    public static event Action RoomInfoChanged;
    public static event Action GameInfoChanged;

    private readonly List<string> _roomPlayers = new List<string>();
    private readonly List<string> _gamePlayers = new List<string>();
    private readonly Dictionary<string, int> _playerCardCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _playerAvatarIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private void Start()
    {
        // Chi khoi tao neu day la instance chinh
        if (Instant != this) return;

        DontDestroyOnLoad(gameObject);

        Debug.Log("[GameManager] Da khoi tao");
    }

    // Ham luu thong tin nguoi choi sau khi dang nhap thanh cong (CHUA CO ROOM)
    public void SetPlayerInfo(string playerId, string nickname)
    {
        PlayerId = playerId;
        Nickname = nickname;

        if (!string.IsNullOrEmpty(Nickname) && !_playerAvatarIds.ContainsKey(Nickname))
        {
            _playerAvatarIds[Nickname] = Mathf.Max(0, SelectedAvatarId);
        }

        Debug.Log($"[GameManager] Da luu thong tin nguoi choi: ID={PlayerId}, Name={Nickname}");
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
        errorMessage = string.Empty;

        var roomPlayers = BuildOrderedRoomPlayers();
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

        _playerCardCounts.Clear();
        for (int i = 0; i < _gamePlayers.Count; i++)
        {
            var nickname = _gamePlayers[i];
            if (!string.IsNullOrEmpty(nickname))
            {
                _playerCardCounts[nickname] = initialCardCount;
            }
        }

        GameInfoChanged?.Invoke();
        return true;
    }

    public int GetPlayerCardCount(string nickname)
    {
        if (string.IsNullOrEmpty(nickname))
        {
            return 0;
        }

        if (_playerCardCounts.TryGetValue(nickname, out var cardCount))
        {
            return cardCount;
        }

        return 0;
    }

    public void SetPlayerCardCount(string nickname, int cardCount)
    {
        if (string.IsNullOrEmpty(nickname))
        {
            return;
        }

        _playerCardCounts[nickname] = Mathf.Max(0, cardCount);
        GameInfoChanged?.Invoke();
    }

    public void ClearGameInfo()
    {
        _gamePlayers.Clear();
        _playerCardCounts.Clear();
        GameInfoChanged?.Invoke();
    }

    private List<string> BuildOrderedRoomPlayers()
    {
        var result = new List<string>();

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
                    playerName = playerName,
                    avatarId = avatarId
                });
            }
        }

        OnRoomState(states);
    }

    public void OnPlayerJoined(RoomPlayerState player)
    {
        if (player == null || string.IsNullOrEmpty(player.playerName))
        {
            return;
        }

        var avatarId = Mathf.Max(0, player.avatarId);
        _playerAvatarIds[player.playerName] = avatarId;

        if (!ContainsPlayerName(_roomPlayers, player.playerName))
        {
            _roomPlayers.Add(player.playerName);
        }

        RoomInfoChanged?.Invoke();
    }

    public void OnRoomState(List<RoomPlayerState> players)
    {
        _roomPlayers.Clear();

        var keepLocalName = Nickname;
        var keepLocalAvatarId = Mathf.Max(0, SelectedAvatarId);
        _playerAvatarIds.Clear();

        if (!string.IsNullOrEmpty(keepLocalName))
        {
            _playerAvatarIds[keepLocalName] = keepLocalAvatarId;
        }

        if (players != null)
        {
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || string.IsNullOrEmpty(player.playerName))
                {
                    continue;
                }

                if (!ContainsPlayerName(_roomPlayers, player.playerName))
                {
                    _roomPlayers.Add(player.playerName);
                }

                _playerAvatarIds[player.playerName] = Mathf.Max(0, player.avatarId);
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
