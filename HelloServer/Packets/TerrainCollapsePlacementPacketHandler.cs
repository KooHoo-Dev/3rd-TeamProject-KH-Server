using System.Text.Json;

namespace HelloServer;

public sealed class TerrainCollapsePlacementPacketHandler : IPacketHandler
{
    public IReadOnlyCollection<string> Types =>
        new[] { PacketTypes.TerrainCollapsePlacementRequest };

    public async Task HandleAsync(PacketContext context, string json, CancellationToken token)
    {
        TerrainCollapsePlacementRequest request =
            JsonSerializer.Deserialize<TerrainCollapsePlacementRequest>(json);

        if (!context.GameSession.TryPlaceCollapse(
                request,
                out TerrainChangeBatchMessage batch,
                out string errorCode,
                out string errorMessage))
        {
            await context.SendAsync(new ErrorMessage
            {
                RequestId = request?.RequestId,
                Code = errorCode,
                Message = errorMessage,
            });

            // 클라이언트는 물리 Chunk를 이미 제거했으므로 거절 시 원본 지형으로 복구한다.
            await context.SendAsync(
                context.GameSession.CreateTerrainSnapshotMessage(request?.RequestId));
            return;
        }

        await context.BroadcastAsync(batch);
    }
}
