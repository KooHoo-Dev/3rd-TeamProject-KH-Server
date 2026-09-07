using System.Text.Json;

namespace HelloServer;

public sealed class ShopPacketHandler : IPacketHandler
{
    private static readonly string[] SupportedTypes = { PacketTypes.ShopRequest };

    public IReadOnlyCollection<string> Types => SupportedTypes;

    public async Task HandleAsync(PacketContext context, string json, CancellationToken token)
    {
        ShopRequest request = JsonSerializer.Deserialize<ShopRequest>(json);
        if (context.GameSession.TryHandleShopRequest(
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
        if (request.Action != ShopActions.Begin)
            await context.BroadcastAsync(context.GameSession.CreateGoldRankingMessage());
        if (endedMessage != null)
            await context.BroadcastAsync(endedMessage);
    }
}
