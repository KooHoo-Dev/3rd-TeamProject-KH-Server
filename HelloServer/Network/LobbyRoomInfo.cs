namespace HelloServer;

/// <summary>게임 시작 전 대기방의 외부 공개 상태입니다.</summary>
public sealed class LobbyRoomInfo
{
    public string RoomCode { get; init; }
    public string HostClientID { get; init; }
    public bool IsStarted { get; init; }
    public int MaxPlayers { get; init; }
    public List<string> Players { get; init; } = new();
}

public class LobbyCreateRequest
{
    public string NickName { get; set; }
    public string ClientID { get; set; }
}

public class LobbyJoinRequest
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
    public string Type { get; set; } = "lobby.start";
    public string HostToken { get; set; }
}

public sealed class LobbyMessageHeader { public string Type { get; set; } }

public sealed class LobbyCreateMessage : LobbyCreateRequest
{
    public string Type { get; set; } = "lobby.create";
}

public sealed class LobbyJoinMessage : LobbyJoinRequest
{
    public string Type { get; set; } = "lobby.join";
    public string RoomCode { get; set; }
}

public sealed class LobbyStateMessage
{
    public string Type { get; set; } = "lobby.state";
    public LobbyRoomInfo Room { get; set; }
    public string HostToken { get; set; }
}

public sealed class LobbyStartedMessage
{
    public string Type { get; set; } = "lobby.started";
    public string RoomCode { get; set; }
}

public sealed class LobbyErrorMessage
{
    public string Type { get; set; } = "lobby.error";
    public string Code { get; set; }
}
