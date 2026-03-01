namespace LiarNumberServer.Messages.ServerToClient
{
    // Event gui ve client khi join lobby that bai
    public class LobbyFailedEvent
    {
        // Ly do that bai (vd: "Nickname already taken", "Invalid nickname")
        public string reason { get; set; } = string.Empty;
    }
}
