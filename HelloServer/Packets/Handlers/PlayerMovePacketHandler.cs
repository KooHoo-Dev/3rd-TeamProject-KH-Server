using System.Text.Json;

namespace HelloServer;

public sealed class PlayerMovePacketHandler : IPacketHandler
{
    private static readonly string[] SupportedTypes =
    {
        PacketTypes.LegacyMove,
        PacketTypes.PlayerMove,
    };

    public IReadOnlyCollection<string> Types => SupportedTypes;

    public Task HandleAsync(
        PacketContext context,
        string json,
        CancellationToken token)
    {
        MoveMessage move = JsonSerializer.Deserialize<MoveMessage>(json);
        context.GameSession.MovePlayer(context.User.Id, move);
        context.RecordMove(move);
        return Task.CompletedTask;
    }
}
