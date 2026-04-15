namespace LiarNumberServer.Messages.ClientToServer
{
    public class StartGameRequest
    {
        public string roomId { get; set; } = string.Empty;
        public string playerId { get; set; } = string.Empty;
        public int initialCardCount { get; set; } = 6;
    }
}
