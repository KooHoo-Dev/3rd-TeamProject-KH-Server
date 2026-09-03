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
