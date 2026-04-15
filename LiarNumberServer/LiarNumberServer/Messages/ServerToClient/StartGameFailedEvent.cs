namespace LiarNumberServer.Messages.ServerToClient
{
    public class StartGameFailedEvent
    {
        public string errorCode { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
    }
}
