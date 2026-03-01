namespace LiarNumberServer.Messages
{
    // Base message wrapper cho tat ca message giua client-server
    // Cau truc: { "type": "MessageType", "payload": "{...}" }
    public class BaseMessage
    {
        // Loai message (vd: "JoinLobby", "LobbyJoined", "PlayCard"...)
        public string type { get; set; } = string.Empty;
        
        // Noi dung message da serialize thanh JSON string
        public string payload { get; set; } = string.Empty;
    }
}
