namespace HelloServer;

public sealed class PlayerReadyMessage : PacketHeader
{
    public PlayerReadyMessage()
    {
        Type = PacketTypes.PlayerReady;
    }
}

public sealed class GameStartedMessage : PacketHeader
{
    public GameStartedMessage()
    {
        Type = PacketTypes.GameStarted;
    }

    public long StartedAtUnixMilliseconds { get; set; }
    public long EndsAtUnixMilliseconds { get; set; }
    public int GameDurationSeconds { get; set; }
    public int VictoryGold { get; set; }
}

public sealed class GameResultPlayerDto
{
    public string PlayerID { get; set; }
    public string NickName { get; set; }
    public int Gold { get; set; }
}

public sealed class GameEndedMessage : PacketHeader
{
    public GameEndedMessage()
    {
        Type = PacketTypes.GameEnded;
    }

    public string Reason { get; set; }
    public string[] WinnerPlayerIDs { get; set; } = Array.Empty<string>();
    public long EndedAtUnixMilliseconds { get; set; }
    public GameResultPlayerDto[] Players { get; set; } = Array.Empty<GameResultPlayerDto>();
}
