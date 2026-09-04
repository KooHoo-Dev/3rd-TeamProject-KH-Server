using System.Text.Json;

namespace HelloServer;

public sealed class DynamiteExplodePacketHandler : IPacketHandler
{
    private static readonly string[] SupportedTypes =
    {
        PacketTypes.DynamiteExplodeRequest,
    };

    public IReadOnlyCollection<string> Types => SupportedTypes;

    public async Task HandleAsync(
        PacketContext context,
        string json,
        CancellationToken token)
    {
        DynamiteExplodeRequest request =
            JsonSerializer.Deserialize<DynamiteExplodeRequest>(json);

        ErrorMessage error = null;

        // terrain_batch의 Revision 순서를 일반 채굴/붕괴 배치와 동일한 큐에서 확정
        await context.ExecuteTerrainCommandAsync(() =>
        {
            if (context.GameSession.TryAcceptDynamiteExplosion(
                    context.User.Id,
                    request,
                    out PendingDynamiteState projectile,
                    out string errorCode,
                    out string errorMessage) == false)
            {
                error = new ErrorMessage
                {
                    RequestId = request?.RequestId,
                    Code = errorCode,
                    Message = errorMessage,
                };
                return;
            }

            context.GameSession.TryExplodeDynamiteTerrain(
                projectile,
                request.X,
                request.Y,
                request.RequestId,
                out TerrainChangeBatchMessage terrainMessage,
                out WorldItemSpawnedMessage[] spawnedMessages);

            context.GameSession.ApplyDynamiteExplosionDamage(
                projectile,
                request.X,
                request.Y,
                request.RequestId,
                out PlayerHealthChangedMessage[] healthChangedMessages,
                out PlayerDiedMessage[] diedMessages);

            if (terrainMessage != null)
                context.EnqueueBroadcast(terrainMessage);

            foreach (WorldItemSpawnedMessage spawnedMessage in spawnedMessages)
                context.EnqueueBroadcast(spawnedMessage);

            foreach (PlayerHealthChangedMessage healthChanged in healthChangedMessages)
                context.EnqueueBroadcast(healthChanged);

            foreach (PlayerDiedMessage died in diedMessages)
                context.EnqueueBroadcast(died);

            context.EnqueueBroadcast(new DynamiteExplodedMessage
            {
                RequestId = request.RequestId,
                ProjectileID = projectile.ProjectileID,
                OwnerPlayerID = projectile.OwnerPlayerID,
                X = request.X,
                Y = request.Y,
                ExplosionRadius = projectile.ExplosionRadius,
            });
        });

        if (error != null)
            await context.SendAsync(error);
    }
}
