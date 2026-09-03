namespace HelloServer;

public class MoveMessage : PacketHeader
{
    public string Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
}

// 초당 10번, 방에 있는 사람 수만큼 만들어 곧바로 버리는 값입니다.
public readonly struct PlayerState
{
    public string Id { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
}

public class StateMessage : PacketHeader
{
    public StateMessage()
    {
        Type = PacketTypes.LegacyState;
    }

    public PlayerState[] States { get; set; }
}
