namespace LiarNumberServer.Messages.ServerToClient
{
    public class ShowWaitingEvent
    {
        public string roomId { get; set; } = string.Empty;
        public int playId { get; set; }
        public string message { get; set; } = string.Empty;
        public string actorPlayerId { get; set; } = string.Empty;
        public int playedCardCount { get; set; }
        public string phase { get; set; } = string.Empty;
    }
}
