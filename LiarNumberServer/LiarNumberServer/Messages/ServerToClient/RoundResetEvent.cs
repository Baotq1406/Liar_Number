namespace LiarNumberServer.Messages.ServerToClient
{
    public class RoundResetHandInfo
    {
        public string playerId { get; set; } = string.Empty;
        public string playerName { get; set; } = string.Empty;
        public string nickname { get; set; } = string.Empty;
        public List<int> cards { get; set; } = new();
    }

    public class RoundResetEvent
    {
        public string roomId { get; set; } = string.Empty;
        public int? playId { get; set; }
        public int destinyCard { get; set; }
        public int cardsPerPlayer { get; set; }
        public List<RoundResetHandInfo> hands { get; set; } = new();
        public List<string> deadPlayerIds { get; set; } = new();
    }
}
