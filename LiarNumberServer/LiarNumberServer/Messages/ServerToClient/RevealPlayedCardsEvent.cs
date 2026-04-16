namespace LiarNumberServer.Messages.ServerToClient
{
    public class RevealPlayedCardsEvent
    {
        public string roomId { get; set; } = string.Empty;
        public int playId { get; set; }
        public string actorPlayerId { get; set; } = string.Empty;
        public string actorPlayerName { get; set; } = string.Empty;
        public List<int> cards { get; set; } = new();
    }
}
