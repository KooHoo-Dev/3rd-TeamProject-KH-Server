namespace HelloServer;

/// <summary>게임 시작 전 대기방의 외부 공개 상태입니다.</summary>
public sealed class LobbyRoomInfo
{
    public string RoomCode { get; init; }
    public bool IsStarted { get; init; }
    public int MaxPlayers { get; init; }
    public List<string> Players { get; init; } = new();
}

public sealed class LobbyCreateRequest
{
    public string NickName { get; set; }
    public string ClientID { get; set; }
}

public sealed class LobbyJoinRequest
{
    public string NickName { get; set; }
    public string ClientID { get; set; }
}

public sealed class LobbyCreateResponse
{
    public LobbyRoomInfo Room { get; init; }
    public string HostToken { get; init; }
}

public sealed class LobbyStartRequest
{
    public string HostToken { get; set; }
}
