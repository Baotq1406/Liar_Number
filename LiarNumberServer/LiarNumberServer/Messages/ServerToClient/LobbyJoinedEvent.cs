namespace LiarNumberServer.Messages.ServerToClient
{
    // Event gui ve client khi join lobby thanh cong
    public class LobbyJoinedEvent
    {
        // ID unique cua nguoi choi
        public string playerId { get; set; } = string.Empty;
        
        // Ten hien thi (echo lai tu request)
        public string nickname { get; set; } = string.Empty;

        // Avatar duoc server cap (server authority)
        public int avatarId { get; set; }
        
        // ID phong ma nguoi choi dang o (null neu chua vao phong nao)
        public string? roomId { get; set; } = null;
    }
}
