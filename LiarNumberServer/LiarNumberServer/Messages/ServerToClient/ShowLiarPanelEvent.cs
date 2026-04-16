namespace LiarNumberServer.Messages.ServerToClient
{
    public class ShowLiarPanelEvent
    {
        public string roomId { get; set; } = string.Empty;
        public int playId { get; set; }
        public string actorPlayerId { get; set; } = string.Empty;
        public string actorPlayerName { get; set; } = string.Empty;
        public int declaredNumber { get; set; }
        public int playedCardCount { get; set; }
        public List<int> previewCards { get; set; } = new();
        public string phase { get; set; } = string.Empty;
    }
}
