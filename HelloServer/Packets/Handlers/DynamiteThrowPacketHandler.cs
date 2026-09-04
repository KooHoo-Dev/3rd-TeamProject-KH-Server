using System.Text.Json;

namespace HelloServer;

public sealed class DynamiteThrowPacketHandler : IPacketHandler
{
    private static readonly string[] SupportedTypes =
    {
        PacketTypes.DynamiteThrow,
    };

    public IReadOnlyCollection<string> Types => SupportedTypes;

    public async Task HandleAsync(
        PacketContext context,
        string json,
        CancellationToken token)
    {
        DynamiteThrowRequest request =
            JsonSerializer.Deserialize<DynamiteThrowRequest>(json);

        if (context.GameSession.TryThrowDynamite(
                context.User.Id,
                request,
                out DynamiteThrownMessage thrownMessage,
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

        // 사용자를 포함한 모두가 서버 승인 뒤에만 투사체를 생성
        await context.BroadcastAsync(thrownMessage);

        // 사용자는 서버가 확정한 수량을 스냅샷으로 반영
        await context.SendAsync(inventoryMessage);
    }
}
