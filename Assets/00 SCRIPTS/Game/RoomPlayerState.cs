using System;

[Serializable]
public class RoomPlayerState
{
    public string playerId;
    public string nickname;
    public string playerName;
    public int avatarId;
    public int cardCount = -1;
    public bool isDead;
}
