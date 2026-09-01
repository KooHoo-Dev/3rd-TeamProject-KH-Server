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

        if (!context.GameSession.TryExcavate(
                context.User.Id,
                request,
                out TerrainChangeBatchMessage batch,
                out WorldItemSpawnedMessage[] spawnedMessages,
                out string errorCode,
                out string errorMessage))
        {
            await context.SendAsync(new ErrorMessage
            {
                RequestId = request?.RequestId,
                Code = errorCode,
                Message = errorMessage
            });

            if (errorCode == "terrain.revision_mismatch")
                await context.SendAsync(
                    context.GameSession.CreateTerrainSnapshotMessage(request?.RequestId));

            return;
        }

        await context.BroadcastAsync(batch);
        foreach (WorldItemSpawnedMessage spawnedMessage in spawnedMessages)
            await context.BroadcastAsync(spawnedMessage);
    }
}
