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

        await context.ExecuteTerrainCommandAsync(async () =>
        {
            if (context.GameSession.TryPlaceCollapse(
                    context.User.Id,
                    request,
                    out TerrainChangeBatchMessage batch,
                    out string errorCode,
                    out string errorMessage) == false)
            {
                await context.SendAsync(new ErrorMessage
                {
                    RequestId = request?.RequestId,
                    Code = errorCode,
                    Message = errorMessage,
                });

                if (RequiresTerrainSnapshot(errorCode))
                    await context.SendAsync(
                        context.GameSession.CreateTerrainSnapshotMessage(request?.RequestId));

                return;
            }

            await context.BroadcastAsync(batch);
        });
    }

    private static bool RequiresTerrainSnapshot(string errorCode)
    {
        return errorCode switch
        {
            // 현재 클라이언트 Revision으로 다음 Batch를 해석할 수 없음
            "terrain.revision_mismatch" => true,
            // 이외의 구조적 오류는 로컬 물리 표현 복구가 필요할 수 있으므로 유지
            "terrain.collapse_invalid" => true,
            "terrain.collapse_out_of_bounds" => true,
            // 다른 요청이 이미 같은 결과를 확정했을 가능성이 높음
            // 서버 Batch를 기다리면 되므로 스냅샷을 보내지 않음
            "terrain.collapse_conflict" => false,
            _ => false
        };
    }
}
