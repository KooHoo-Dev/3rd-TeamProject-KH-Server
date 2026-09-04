using System.Text.Json;

namespace HelloServer;

public sealed class PlayerRespawnPacketHandler : IPacketHandler
{
    private static readonly string[] SupportedTypes = { PacketTypes.PlayerRespawnRequest };
    public IReadOnlyCollection<string> Types => SupportedTypes;

    public async Task HandleAsync(PacketContext context, string json, CancellationToken token)
    {
        PlayerRespawnRequest request = JsonSerializer.Deserialize<PlayerRespawnRequest>(json);
        if (context.GameSession.TryRespawnPlayer(
                context.User.Id,
                request,
                out PlayerRespawnedMessage respawned,
                out string errorCode,
                out string errorMessage) == false)
        {
            await context.SendAsync(new ErrorMessage
            {
                RequestId = request?.RequestId,
                Code = errorCode,
                Message = errorMessage,
            });
            return;
        }

        await context.BroadcastAsync(respawned);
    }
}
