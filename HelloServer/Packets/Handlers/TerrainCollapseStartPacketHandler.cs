using System.Text.Json;

namespace HelloServer;

public sealed class TerrainCollapseStartPacketHandler : IPacketHandler
{
    public IReadOnlyCollection<string> Types =>
        new[] { PacketTypes.TerrainCollapseStartRequest };

    public async Task HandleAsync(
        PacketContext context,
        string json,
        CancellationToken token)
    {
        TerrainCollapseStartRequest request =
            JsonSerializer.Deserialize<TerrainCollapseStartRequest>(json);

        ErrorMessage error = null;
        await context.ExecuteTerrainCommandAsync(() =>
        {
            if (context.GameSession.TryStartCollapse(
                    context.User.Id,
                    request,
                    out TerrainCollapseStartedMessage started,
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

            context.EnqueueBroadcast(started);
        });
        if (error != null) await context.SendAsync(error);
    }
}
