namespace HelloServer;

/// <summary>게임 시작 전 대기방의 외부 공개 상태입니다.</summary>
public sealed class LobbyRoomInfo
{
    public string RoomCode { get; init; }
    public string HostClientID { get; init; }
    public bool IsStarted { get; init; }
    public int MaxPlayers { get; init; }
    public List<string> Players { get; init; } = new();
    public List<LobbyPlayerInfo> PlayerDetails { get; init; } = new(); // 로비 슬롯 렌더링용 서버 확정 입장 순서
}

public sealed class LobbyPlayerInfo
{
    public string ClientID { get; init; }
    public string NickName { get; init; }
    /// <summary>대기방 WebSocket이 연결된 상태인지 여부입니다. 재대기방에서는 false인 플레이어를 고스트 슬롯으로 표시합니다.</summary>
    public bool IsConnected { get; init; }
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

public sealed class LobbyKickRequest
{
    public string Type { get; set; } = "lobby.kick";
    public string HostToken { get; set; }
    public string TargetClientID { get; set; }
}

public sealed class LobbyKickedMessage
{
    public string Type { get; set; } = "lobby.kicked";
    public string Reason { get; set; } = "host_kicked";
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

public sealed class LobbyReturnMessage : LobbyJoinRequest
{
    public string Type { get; set; } = "lobby.return";
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

public sealed class LobbyChatSendMessage
{
    public string Type { get; set; } = "lobby.chat.send";
    public string Text { get; set; }
}

public sealed class LobbyChatMessage
{
    public string Type { get; set; } = "lobby.chat";
    public string ClientID { get; set; }
    public string NickName { get; set; }
    public string Text { get; set; }
}

public sealed class LobbySystemMessage
{
    public string Type { get; set; } = "lobby.system";
    public string Text { get; set; }
}
