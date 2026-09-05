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

                return;
            }
            PlayerActionMessage action = context.GameSession.CreateMiningAction(
                context.User.Id,
                request.TargetCell);
            if (action != null) context.EnqueueBroadcast(action);
            context.EnqueueBroadcast(batch);

            foreach (WorldItemSpawnedMessage spawnedMessage in spawnedMessages)
                context.EnqueueBroadcast(spawnedMessage);
            ServerPerformanceMetrics.Write("TerrainMutation", mutationAt,
                $" BatchSize={batch.Batch.Changes.Count}");
        });

        if (error != null) await context.SendAsync(error);
    }
}
