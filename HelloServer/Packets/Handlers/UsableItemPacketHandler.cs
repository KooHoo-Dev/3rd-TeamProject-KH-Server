using System.Text.Json;

namespace HelloServer;

public sealed class UsableItemPacketHandler : IPacketHandler
{
    public IReadOnlyCollection<string> Types => new[] { PacketTypes.UsableItemUse };

    public async Task HandleAsync(PacketContext context, string json, CancellationToken token)
    {
        UsableItemUseRequest request = JsonSerializer.Deserialize<UsableItemUseRequest>(json);
        if (context.GameSession.TryUseUsableItem(context.User.Id, request,
                out UsableItemUsedMessage used, out InventorySnapshotMessage inventory,
                out PlayerHealthChangedMessage health, out string errorCode, out string errorMessage) == false)
        {
            await context.SendAsync(new ErrorMessage { RequestId = request?.RequestId, Code = errorCode, Message = errorMessage });
            return;
        }

        await context.SendAsync(used);
        if (health != null) await context.BroadcastAsync(health);
        await context.SendAsync(inventory);
    }
}
