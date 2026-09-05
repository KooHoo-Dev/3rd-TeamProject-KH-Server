using System.Text.Json;

namespace HelloServer;

public sealed class InventoryDebugGoldPacketHandler : IPacketHandler
{
    private static readonly string[] SupportedTypes = { PacketTypes.InventoryDebugGold };

    public IReadOnlyCollection<string> Types => SupportedTypes;

    public async Task HandleAsync(PacketContext context, string json, CancellationToken token)
    {
        InventoryDebugGoldRequest request =
            JsonSerializer.Deserialize<InventoryDebugGoldRequest>(json);
        if (context.GameSession.TryGrantDebugGold(
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
