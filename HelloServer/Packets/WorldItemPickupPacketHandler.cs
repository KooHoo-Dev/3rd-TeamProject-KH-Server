using System.Text.Json;

namespace HelloServer;

public sealed class WorldItemPickupPacketHandler : IPacketHandler
{
    private static readonly string[] SupportedTypes =
    {
        PacketTypes.WorldItemPickup,
    };

    public IReadOnlyCollection<string> Types => SupportedTypes;

    public async Task HandleAsync(
        PacketContext context,
        string json,
        CancellationToken token)
    {
        WorldItemPickupRequest request =
            JsonSerializer.Deserialize<WorldItemPickupRequest>(json);
        if (context.GameSession.TryPickup(
                context.User.Id,
                request,
                out WorldItemRemovedMessage removedMessage,
                out InventorySnapshotMessage inventoryMessage,
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

        await context.BroadcastAsync(removedMessage);
        await context.SendAsync(inventoryMessage);
    }
}
