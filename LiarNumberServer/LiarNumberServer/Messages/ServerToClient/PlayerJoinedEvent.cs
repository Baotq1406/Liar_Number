namespace LiarNumberServer.Messages.ServerToClient
{
    public class PlayerJoinedEvent
    {
        public string roomId { get; set; } = string.Empty;
        public string playerName { get; set; } = string.Empty;
        public int avatarId { get; set; }
    }
}
