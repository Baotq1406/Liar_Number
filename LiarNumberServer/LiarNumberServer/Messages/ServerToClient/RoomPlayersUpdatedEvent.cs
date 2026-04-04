namespace LiarNumberServer.Messages.ServerToClient
{
    public class RoomPlayersUpdatedEvent
    {
        public string roomId { get; set; } = string.Empty;
        public List<string> players { get; set; } = new();
    }
}
