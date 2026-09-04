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
}
