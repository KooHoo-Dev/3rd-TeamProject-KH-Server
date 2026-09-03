namespace HelloServer;

public class User
{
    public string Id { get; set; }
    public string NickName { get; set; }
}

public class HelloMessage : PacketHeader
{
    public string NickName { get; set; }
}

public class WelcomeMessage : PacketHeader
{
    public WelcomeMessage()
    {
        Type = PacketTypes.Welcome;
    }

    public string RoomCode { get; set; }
    public User User { get; set; }
    public User[] Users { get; set; }
}

public class JoinMessage : PacketHeader
{
    public JoinMessage()
    {
        Type = PacketTypes.Join;
    }

    public User User { get; set; }
}

public class LeaveMessage : PacketHeader
{
    public LeaveMessage()
    {
        Type = PacketTypes.Leave;
    }

    public string Id { get; set; }
}
