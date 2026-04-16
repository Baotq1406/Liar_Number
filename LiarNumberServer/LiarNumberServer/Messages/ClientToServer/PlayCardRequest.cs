namespace LiarNumberServer.Messages.ClientToServer
{
    public class PlayCardRequest
    {
        public string roomId { get; set; } = string.Empty;
        public string playerId { get; set; } = string.Empty;
        public List<int> cards { get; set; } = new();
        public int declaredNumber { get; set; }
    }
}
