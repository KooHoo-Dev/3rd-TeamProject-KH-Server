namespace HelloServer;

public class MoveMessage : PacketHeader
{
    public string Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public bool IsGrounded { get; set; }
    public bool IsClimbing { get; set; }
    public bool IsBuried { get; set; }
}

// 초당 10번, 방에 있는 사람 수만큼 만들어 곧바로 버리는 값입니다.
public readonly struct PlayerState
{
    public string Id { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float VelocityX { get; init; }
    public float VelocityY { get; init; }
    public bool IsGrounded { get; init; }
    public bool IsClimbing { get; init; }
    public bool IsBuried { get; init; }
    public bool IsDead { get; init; }
}

public class StateMessage : PacketHeader
{
    public StateMessage()
    {
        Type = PacketTypes.LegacyState;
    }

    public PlayerState[] States { get; set; }
}

public sealed class PlayerActionMessage : PacketHeader
{
    public PlayerActionMessage()
    {
        Type = PacketTypes.PlayerAction;
    }

    public string PlayerID { get; set; }
    public string Action { get; set; }
    public float DirectionX { get; set; }
}
