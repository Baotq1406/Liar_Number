namespace LiarNumberServer.Models
{
    public class LastPlayContext
    {
        public int playId { get; set; }
        public string actorPlayerId { get; set; } = string.Empty;
        public int declaredNumber { get; set; }
        public int playedCardCount { get; set; }
        public List<int> cards { get; set; } = new();
    }
}
