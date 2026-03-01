using UnityEngine;

public class GameManager : Singleton<GameManager>
{
    // Thong tin nguoi choi
    public string PlayerId { get; private set; }
    public string Nickname { get; private set; }
    public string RoomId { get; private set; }

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

        Debug.Log($"[GameManager] Da luu thong tin nguoi choi: ID={PlayerId}, Name={Nickname}");
    }

    // Ham luu roomId khi nguoi choi tao/join room
    public void SetRoomId(string roomId)
    {
        RoomId = roomId;
        Debug.Log($"[GameManager] Da join room: {RoomId}");
    }

    // Ham xoa thong tin khi dang xuat
    public void ClearPlayerInfo()
    {
        PlayerId = string.Empty;
        Nickname = string.Empty;
        RoomId = string.Empty;

        Debug.Log("[GameManager] Da xoa thong tin nguoi choi");
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
