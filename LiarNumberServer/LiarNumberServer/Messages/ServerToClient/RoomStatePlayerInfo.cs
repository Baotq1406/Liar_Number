namespace LiarNumberServer.Messages.ServerToClient
{
    public class RoomStatePlayerInfo
    {
        public string playerId { get; set; } = string.Empty;
        public string nickname { get; set; } = string.Empty;
        public string playerName { get; set; } = string.Empty;
        public int avatarId { get; set; }
        public int cardCount { get; set; }
        public bool isDead { get; set; }
    }
}
