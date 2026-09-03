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
        await context.ExecuteTerrainCommandAsync(() =>
        {
            long mutationAt = ServerPerformanceMetrics.Timestamp();
            if (context.GameSession.TryPlaceCollapse(
                    context.User.Id,
                    request,
                    out TerrainChangeBatchMessage batch,
                    out TerrainCollapseCancelledMessage cancelled,
                    out string errorCode,
                    out string errorMessage) == false)
            {
                error = new ErrorMessage
                {
                    RequestId = request?.RequestId,
                    Code = errorCode,
                    Message = errorMessage,
                };

                // 거절이 곧 그 낙하의 끝입니다. 보낸 사람은 다시 시도하지 않습니다.
                //
                // 그래서 오류를 보낸 사람에게 돌려주는 것만으로는 모자랍니다.
                // 방에 있는 모두가 낙하가 시작될 때 자기 화면에서 원본 칸을 지웠으므로,
                // 되돌리라고 알려 주지 않으면 그 지형은 모두의 화면에서 사라진 채로 남습니다.
                //
                // 오류는 보낸 사람에게만, 취소는 방 전체에 갑니다. 받는 쪽이 다릅니다.
                if (cancelled != null) context.EnqueueBroadcast(cancelled);

                return;
            }

            context.EnqueueBroadcast(batch);
            ServerPerformanceMetrics.Write("TerrainMutation", mutationAt,
                $" BatchSize={batch.Batch.Changes.Count}");
        });
        if (error != null) await context.SendAsync(error);
    }
}
