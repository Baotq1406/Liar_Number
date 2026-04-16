namespace LiarNumberServer.Messages.ServerToClient
{
    public class ErrorEvent
    {
        public string code { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
    }
}
