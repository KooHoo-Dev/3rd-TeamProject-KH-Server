using System.Text.Json;

namespace HelloServer;

public sealed class TerrainDeathLootPacketHandler : IPacketHandler
{
    public IReadOnlyCollection<string> Types => new[] { PacketTypes.TerrainDeathLootRequest };

    public async Task HandleAsync(PacketContext context, string json, CancellationToken token)
    {
        TerrainDeathLootRequest request = JsonSerializer.Deserialize<TerrainDeathLootRequest>(json);
        ErrorMessage error = null;
        InventorySnapshotMessage inventoryMessage = null;

        await context.ExecuteTerrainCommandAsync(() =>
        {
            if (context.GameSession.TryCreateDeathLoot(
                    context.User.Id,
                    request,
                    out TerrainChangeBatchMessage terrainMessage,
                    out InventorySnapshotMessage snapshot,
                    out string errorCode,
                    out string errorMessage) == false)
            {
                error = new ErrorMessage
                {
                    RequestId = request?.RequestId,
                    Code = errorCode,
                    Message = errorMessage,
                };
                return;
            }

            inventoryMessage = snapshot;
            context.EnqueueBroadcast(terrainMessage);
        });

        if (error != null) await context.SendAsync(error);
        else if (inventoryMessage != null) await context.SendAsync(inventoryMessage);
    }
}
