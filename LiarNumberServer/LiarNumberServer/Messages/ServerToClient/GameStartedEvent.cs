namespace LiarNumberServer.Messages.ServerToClient
{
    public class GameStartedEvent
    {
        public string roomId { get; set; } = string.Empty;
        public List<string> players { get; set; } = new();
        public int initialCardCount { get; set; }
    }
}
