namespace LiarNumberServer.Messages.ServerToClient
{
    public class RoomClosedEvent
    {
        public string roomId { get; set; } = string.Empty;
        public string reason { get; set; } = string.Empty;
        public string closedBy { get; set; } = string.Empty;
    }
}
