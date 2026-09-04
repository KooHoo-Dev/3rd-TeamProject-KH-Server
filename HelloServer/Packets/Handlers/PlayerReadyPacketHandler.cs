namespace HelloServer;

public sealed class PlayerReadyPacketHandler : IPacketHandler
{
    private static readonly string[] SupportedTypes = { PacketTypes.PlayerReady };

    public IReadOnlyCollection<string> Types => SupportedTypes;

    public async Task HandleAsync(
        PacketContext context,
        string json,
        CancellationToken token)
    {
        if (context.GameSession.MarkPlayerReady(
                context.User.Id,
                out GameStartedMessage startedMessage,
                out bool newlyStarted) == false)
            return;

        if (startedMessage == null || newlyStarted == false) return;

        context.EnqueueBroadcast(startedMessage);

        Console.WriteLine($"[{context.RoomCode}] 모든 플레이어 준비 완료. 게임 시작");
    }
}
