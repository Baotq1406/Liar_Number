namespace LiarNumberServer.Messages.ServerToClient
{
    public class RoomStateEvent
    {
        public string roomId { get; set; } = string.Empty;
        public List<RoomStatePlayerInfo> players { get; set; } = new();
    }
}
