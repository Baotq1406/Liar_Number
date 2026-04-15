namespace LiarNumberServer.Messages.ServerToClient
{
    public class RoomStatePlayerInfo
    {
        public string playerName { get; set; } = string.Empty;
        public int avatarId { get; set; }
    }
}
