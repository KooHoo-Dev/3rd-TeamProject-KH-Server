using System.Text.Json;

namespace HelloServer;

public sealed class TerrainExcavationPacketHandler : IPacketHandler
{
    public IReadOnlyCollection<string> Types =>
        new[] { PacketTypes.TerrainExcavationRequest };

    public async Task HandleAsync(
        PacketContext context,
        string json,
        CancellationToken token)
    {
        TerrainExcavationRequest request =
            JsonSerializer.Deserialize<TerrainExcavationRequest>(json);

        ErrorMessage error = null;
        bool needsSnapshot = false;
        await context.ExecuteTerrainCommandAsync(() =>
        {
            long mutationAt = ServerPerformanceMetrics.Timestamp();
            if (context.GameSession.TryExcavate(
                    context.User.Id,
                    request,
                    out TerrainChangeBatchMessage batch,
                    out WorldItemSpawnedMessage[] spawnedMessages,
                    out string errorCode,
                    out string errorMessage) == false)
            {
                error = new ErrorMessage
                {
                    RequestId = request?.RequestId,
                    Code = errorCode,
                    Message = errorMessage
                };

                if (errorCode == "terrain.revision_mismatch")
                    needsSnapshot = true;

                return;
            }
            context.EnqueueBroadcast(batch);

            foreach (WorldItemSpawnedMessage spawnedMessage in spawnedMessages)
                context.EnqueueBroadcast(spawnedMessage);
            ServerPerformanceMetrics.Write("TerrainMutation", mutationAt,
                $" BatchSize={batch.Batch.Changes.Count}");
        });

        if (error != null) await context.SendAsync(error);
        if (needsSnapshot) await context.SendAsync(context.GameSession.CreateTerrainSnapshotMessage(request?.RequestId));
    }
}
