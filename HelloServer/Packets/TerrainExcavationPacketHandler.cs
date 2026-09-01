using System.Text.Json;

namespace HelloServer;

public sealed class TerrainExcavationPacketHandler : IPacketHandler
{
    private static readonly string[] SupportedTypes =
    {
        PacketTypes.TerrainExcavationRequest,
        PacketTypes.TerrainExcavate,
    };

    public IReadOnlyCollection<string> Types => SupportedTypes;

    public async Task HandleAsync(
        PacketContext context,
        string json,
        CancellationToken token)
    {
        TerrainExcavationRequest request =
            JsonSerializer.Deserialize<TerrainExcavationRequest>(json);
        if (context.GameSession.TryExcavate(
                context.User.Id,
                request,
                out TerrainChangeBatchMessage terrainMessage,
                out WorldItemSpawnedMessage[] spawnedMessages,
                out string errorCode,
                out string errorMessage) == false)
        {
            await context.SendAsync(new ErrorMessage
            {
                RequestId = request?.RequestId,
                Code = errorCode,
                Message = errorMessage,
            });
            if (errorCode == "terrain.revision_mismatch")
                await context.SendAsync(
                    context.GameSession.CreateTerrainSnapshotMessage(request?.RequestId));
            return;
        }

        await context.BroadcastAsync(terrainMessage);
        foreach (WorldItemSpawnedMessage spawnedMessage in spawnedMessages)
            await context.BroadcastAsync(spawnedMessage);
    }
}
