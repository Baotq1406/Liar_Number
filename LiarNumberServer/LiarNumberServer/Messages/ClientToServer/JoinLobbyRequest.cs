namespace LiarNumberServer.Messages.ClientToServer
{
    // Request tu client de join lobby (login)
    public class JoinLobbyRequest
    {
        // Ten hien thi cua nguoi choi (3-20 ky tu)
        public string nickname { get; set; } = string.Empty;
    }
}
