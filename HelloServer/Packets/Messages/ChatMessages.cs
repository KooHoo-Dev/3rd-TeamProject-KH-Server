namespace HelloServer;

public class ChatMessage : PacketHeader
{
    public ChatMessage()
    {
        Type = PacketTypes.LegacyChat;
    }

    public string Id { get; set; }
    public string NickName { get; set; }
    public string Text { get; set; }
}

public sealed class ChatBroadcastMessage : PacketHeader
{
    public ChatBroadcastMessage() { Type = PacketTypes.ChatMessage; }
    public string PlayerID { get; set; }
    public string ClientID { get; set; }
    public string NickName { get; set; }
    public int ElapsedSeconds { get; set; }
    public string Text { get; set; }
}

public sealed class ChatSystemMessage : PacketHeader
{
    public ChatSystemMessage() { Type = PacketTypes.ChatSystem; }
    public int ElapsedSeconds { get; set; }
    public string Text { get; set; }
}
