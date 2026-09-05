using System.Text.Json;

namespace HelloServer;

public sealed class ChatSendPacketHandler : IPacketHandler
{
    private const int MaximumTextLength = 200;
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
        ChatMessage request = JsonSerializer.Deserialize<ChatMessage>(json);
        string text = request?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > MaximumTextLength)
        {
            await context.SendAsync(new ErrorMessage
            {
                Code = "chat.too_long",
                Message = $"채팅은 {MaximumTextLength}자까지 입력할 수 있습니다."
            });
            return;
        }

        ChatBroadcastMessage message = new()
        {
            PlayerID = context.User.Id,
            ClientID = context.User.ClientID,
            NickName = context.User.NickName,
            ElapsedSeconds = context.GameSession.GetElapsedGameSeconds(),
            Text = text,
        };
        Console.WriteLine($"[{context.RoomCode}] [{message.ElapsedSeconds}] {message.NickName} : {text}");
        await context.BroadcastAsync(message);
    }
}
