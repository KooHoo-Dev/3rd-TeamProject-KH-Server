using System.Text.Json;

namespace HelloServer;

public sealed class PlayerHealPacketHandler : IPacketHandler
{
    private static readonly string[] SupportedTypes = { PacketTypes.PlayerHeal };
    public IReadOnlyCollection<string> Types => SupportedTypes;

    public async Task HandleAsync(PacketContext context, string json, CancellationToken token)
    {
        PlayerHealRequest request = JsonSerializer.Deserialize<PlayerHealRequest>(json);
        if (context.GameSession.TryApplyPlayerHealing(
                context.User.Id,
                request,
                out PlayerHealthChangedMessage changed,
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

        await context.BroadcastAsync(changed);
    }
}
