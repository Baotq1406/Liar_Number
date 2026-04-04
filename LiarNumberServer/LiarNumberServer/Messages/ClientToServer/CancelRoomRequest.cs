namespace LiarNumberServer.Messages.ClientToServer
{
    public class CancelRoomRequest
    {
        public string roomId { get; set; } = string.Empty;
        public string playerId { get; set; } = string.Empty;
    }
}
