using System.Text.Json;

namespace HelloServer;

public sealed class WorldItemDropPacketHandler : IPacketHandler
{
    public IReadOnlyCollection<string> Types => new[] { PacketTypes.WorldItemDrop };

    public async Task HandleAsync(PacketContext context, string json, CancellationToken token)
    {
        WorldItemDropRequest request = JsonSerializer.Deserialize<WorldItemDropRequest>(json);
        if (context.GameSession.TryDropWorldItem(
                context.User.Id,
                request,
                out WorldItemSpawnedMessage spawnedMessage,
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

        await context.BroadcastAsync(spawnedMessage);
        await context.SendAsync(inventoryMessage);
    }
}
