using System.Text.Json;

namespace HelloServer;

public sealed class InventorySellPacketHandler : IPacketHandler
{
    private static readonly string[] SupportedTypes = { PacketTypes.InventorySell };

    public IReadOnlyCollection<string> Types => SupportedTypes;

    public async Task HandleAsync(PacketContext context, string json, CancellationToken token)
    {
        InventorySellRequest request = JsonSerializer.Deserialize<InventorySellRequest>(json);
        if (context.GameSession.TrySellInventory(
                context.User.Id,
                request,
                out InventorySnapshotMessage inventoryMessage,
                out GameEndedMessage endedMessage,
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

        await context.SendAsync(inventoryMessage);
        if (endedMessage != null)
            await context.BroadcastAsync(endedMessage);
    }
}
