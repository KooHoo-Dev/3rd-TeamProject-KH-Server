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
                out int earnedGold,
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
        if (earnedGold > 0)
        {
            await context.SendAsync(new ChatSystemMessage
            {
                ElapsedSeconds = context.GameSession.GetElapsedGameSeconds(),
                Text = $"자원을 판매하여 {earnedGold:N0} 골드를 획득했습니다.",
            });
        }
        await context.BroadcastAsync(context.GameSession.CreateGoldRankingMessage());
        if (endedMessage != null)
            await context.BroadcastAsync(endedMessage);
    }
}
