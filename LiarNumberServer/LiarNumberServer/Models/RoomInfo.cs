namespace LiarNumberServer.Models
{
    public class RoomInfo
    {
        public string RoomId { get; set; } = string.Empty;
        public string HostPlayerId { get; set; } = string.Empty;
        public string HostNickname { get; set; } = string.Empty;
        public bool IsGameStarted { get; set; }
        public int MaxPlayers { get; set; } = 4;
        public DateTime CreatedAt { get; set; }
        public RoomTurnState? TurnState { get; set; }
        public Dictionary<string, RouletteState> RouletteStates { get; } = new(StringComparer.Ordinal);
        public List<RoomPlayerInfo> Players { get; } = new();
    }
}
