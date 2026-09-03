using System.Text.Json;

namespace HelloServer;

public sealed class ChatSendPacketHandler : IPacketHandler
{
    private static readonly string[] SupportedTypes =
    {
        PacketTypes.LegacyChat,
        PacketTypes.ChatSend,
    };

    public IReadOnlyCollection<string> Types => SupportedTypes;

    public async Task HandleAsync(
        PacketContext context,
        string json,
        CancellationToken token)
    {
        ChatMessage chat = JsonSerializer.Deserialize<ChatMessage>(json);
        string said = chat.Text?.Trim();

        Console.WriteLine($"[{context.RoomCode}] {chat.NickName} : {said}");
        await context.BroadcastAsync(chat);
    }
}
