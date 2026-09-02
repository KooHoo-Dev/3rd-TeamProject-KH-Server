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

        ErrorMessage error = null;
        bool needsSnapshot = false;
        await context.ExecuteTerrainCommandAsync(() =>
        {
            long mutationAt = ServerPerformanceMetrics.Timestamp();
            if (context.GameSession.TryPlaceCollapse(
                    context.User.Id,
                    request,
                    out TerrainChangeBatchMessage batch,
                    out string errorCode,
                    out string errorMessage) == false)
            {
                error = new ErrorMessage
                {
                    RequestId = request?.RequestId,
                    Code = errorCode,
                    Message = errorMessage,
                };

                if (RequiresTerrainSnapshot(errorCode))
                    needsSnapshot = true;

                return;
            }

            context.EnqueueBroadcast(batch);
            ServerPerformanceMetrics.Write("TerrainMutation", mutationAt,
                $" BatchSize={batch.Batch.Changes.Count}");
        });
        if (error != null) await context.SendAsync(error);
        if (needsSnapshot) await context.SendAsync(context.GameSession.CreateTerrainSnapshotMessage(request?.RequestId));
    }

    private static bool RequiresTerrainSnapshot(string errorCode)
    {
        return errorCode switch
        {
            // 이후 Batch 적용 불가능이 명확한 Revision 불일치 오류
            "terrain.revision_mismatch" => true,
            _ => false
        };
    }
}
