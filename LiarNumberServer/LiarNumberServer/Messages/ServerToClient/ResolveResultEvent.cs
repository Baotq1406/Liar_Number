namespace LiarNumberServer.Messages.ServerToClient
{
    public class ResolveRouletteResult
    {
        public int stageBefore { get; set; }
        public int stageAfter { get; set; }
        public bool hit { get; set; }
        public bool isDead { get; set; }
    }

    public class ResolveResultEvent
    {
        public string roomId { get; set; } = string.Empty;
        public int playId { get; set; }
        public string accuserPlayerId { get; set; } = string.Empty;
        public string accusedPlayerId { get; set; } = string.Empty;
        public string punishedPlayerId { get; set; } = string.Empty;
        public bool liar { get; set; }
        public string reason { get; set; } = string.Empty;
        public int destinyCard { get; set; }
        public List<int> revealedCards { get; set; } = new();
        public ResolveRouletteResult roulette { get; set; } = new();
    }
}
