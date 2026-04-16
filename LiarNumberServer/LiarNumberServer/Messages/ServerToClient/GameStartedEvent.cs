namespace LiarNumberServer.Messages.ServerToClient
{
    public class GameStartedHandInfo
    {
        public string playerName { get; set; } = string.Empty;
        public List<int> cards { get; set; } = new();
    }

    public class GameStartedEvent
    {
        public string roomId { get; set; } = string.Empty;
        public List<string> players { get; set; } = new();
        public int initialCardCount { get; set; }
        public int destinyCard { get; set; }
        public List<GameStartedHandInfo> hands { get; set; } = new();
    }
}
