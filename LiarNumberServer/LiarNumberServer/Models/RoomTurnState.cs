namespace LiarNumberServer.Models
{
    public static class TurnPhase
    {
        public const string WaitingPlay = "WaitingPlay";
        public const string WaitingResponses = "WaitingResponses";
        public const string Resolving = "Resolving";
    }

    public class RoomTurnState
    {
        public List<string> turnOrderPlayerIds { get; set; } = new();
        public int currentTurnIndex { get; set; }
        public string currentTurnPlayerId { get; set; } = string.Empty;
        public int destinyCard { get; set; }
        public int nextPlayId { get; set; }
        public string phase { get; set; } = TurnPhase.WaitingPlay;
        public LastPlayContext? lastPlay { get; set; }
        public int? resolvingPlayId { get; set; }
        public int? lastRoundResetPlayId { get; set; }
        public HashSet<int> resolvedPlayIds { get; set; } = new();
        public HashSet<string> pendingResponderIds { get; set; } = new();
        public HashSet<string> respondedIds { get; set; } = new();
    }
}
