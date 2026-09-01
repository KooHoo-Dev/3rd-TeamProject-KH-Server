namespace HelloServer;

public class MoveMessage : PacketHeader
{
    public string Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
}

public class PlayerState
{
    public string Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
}

public class StateMessage : PacketHeader
{
    public StateMessage()
    {
        Type = PacketTypes.LegacyState;
    }

    public PlayerState[] States { get; set; }
}
