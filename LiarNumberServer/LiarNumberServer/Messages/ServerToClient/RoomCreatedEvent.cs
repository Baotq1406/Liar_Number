namespace LiarNumberServer.Messages.ServerToClient
{
    public class RoomCreatedEvent
    {
        public string roomId { get; set; } = string.Empty;
        public string hostId { get; set; } = string.Empty;
        public string hostNickname { get; set; } = string.Empty;
        public int hostAvatarId { get; set; }
    }
}
