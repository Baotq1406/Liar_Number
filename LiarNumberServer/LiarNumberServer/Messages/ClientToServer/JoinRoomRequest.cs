namespace LiarNumberServer.Messages.ClientToServer
{
    public class JoinRoomRequest
    {
        public string roomId { get; set; } = string.Empty;
        public string playerId { get; set; } = string.Empty;
        public string nickname { get; set; } = string.Empty;
        public int? avatarId { get; set; }
    }
}
