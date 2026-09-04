using System.Text.Json;

namespace HelloServer;

public sealed class PlayerDamagePacketHandler : IPacketHandler
{
    private static readonly string[] SupportedTypes = { PacketTypes.PlayerDamage };
    public IReadOnlyCollection<string> Types => SupportedTypes;

    public async Task HandleAsync(PacketContext context, string json, CancellationToken token)
    {
        PlayerDamageRequest request = JsonSerializer.Deserialize<PlayerDamageRequest>(json);
        if (context.GameSession.TryApplyPlayerDamage(
                context.User.Id,
                request,
                out PlayerHealthChangedMessage changed,
                out PlayerDiedMessage died,
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
        if (died != null)
            await context.BroadcastAsync(died);
    }
}
