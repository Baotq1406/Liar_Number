namespace LiarNumberServer.Messages.ServerToClient
{
    public class TurnUpdateEvent
    {
        public string roomId { get; set; } = string.Empty;
        public int destinyCard { get; set; }
        public string currentTurnPlayerId { get; set; } = string.Empty;
        public string currentTurnPlayerName { get; set; } = string.Empty;
        public int currentTurnIndex { get; set; }
        public List<string> turnOrderPlayerIds { get; set; } = new();
        public string phase { get; set; } = string.Empty;
    }
}
