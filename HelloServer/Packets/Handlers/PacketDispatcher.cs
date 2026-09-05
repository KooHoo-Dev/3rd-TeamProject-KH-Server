using System.Text.Json;

namespace HelloServer;

public sealed class PacketDispatcher
{
    // 게임플레이 패킷 집합
    private static readonly HashSet<string> GameplayPacketTypes = new(
        StringComparer.Ordinal)
    {
        PacketTypes.PlayerDamage,
        PacketTypes.PlayerRespawnRequest,

        PacketTypes.TerrainExcavationRequest,
        PacketTypes.TerrainDeathLootRequest,
        PacketTypes.TerrainCollapseStartRequest,
        PacketTypes.TerrainCollapsePlacementRequest,

        PacketTypes.WorldItemPickup,
        PacketTypes.WorldItemDrop,

        PacketTypes.InventorySell,
        PacketTypes.InventoryDebugGold,

        PacketTypes.DynamiteThrow,
        PacketTypes.DynamiteExplodeRequest,
    };

    private readonly Dictionary<string, IPacketHandler> handlers =
        new Dictionary<string, IPacketHandler>(StringComparer.Ordinal);

    // 이미 한 번 알린 종류. 같은 것으로 콘솔을 채우지 않기 위해 기억해 둡니다.
    private readonly HashSet<string> reportedUnknownTypes = new(StringComparer.Ordinal);

    public PacketDispatcher(IEnumerable<IPacketHandler> packetHandlers)
    {
        foreach (IPacketHandler handler in packetHandlers)
        {
            foreach (string type in handler.Types)
            {
                if (handlers.TryAdd(type, handler) == false)
                    throw new InvalidOperationException($"패킷 Type이 중복 등록됨: {type}");
            }
        }
    }

    public Task DispatchAsync(
        PacketContext context,
        string json,
        CancellationToken token)
    {
        PacketHeader header = JsonSerializer.Deserialize<PacketHeader>(json);

        // 모르는 것을 조용히 버리지 않습니다.
        //
        // 예전에는 그냥 돌아갔습니다. 그래서 새 메시지 종류를 만들고
        // PacketHandlerRegistry 에 등록하는 것을 빠뜨리면
        // 서버가 받고도 아무 일도 안 하는데 아무 흔적도 안 남았습니다.
        // 종류마다 한 번만 찍습니다. 못된 클라이언트가 콘솔을 채우지 못하게.
        if (string.IsNullOrWhiteSpace(header?.Type))
        {
            Report(context, "(Type 없음)");
            return Task.CompletedTask;
        }

        if (handlers.TryGetValue(header.Type, out IPacketHandler handler) == false)
        {
            Report(context, header.Type);
            return Task.CompletedTask;
        }

        // 등록된 패킷인지 확인한 다음, 실제 핸들러를 실행하기 전에 차단
        if (GameplayPacketTypes.Contains(header.Type) &&
            context.GameSession.IsGameplayActive == false)
        {
            return context.SendAsync(new ErrorMessage
            {
                Code = "game.not_active",
                Message = "게임 카운트다운이 끝나기 전에는 사용할 수 없습니다."
            });
        }

        return handler.HandleAsync(context, json, token);
    }

    private void Report(PacketContext context, string type)
    {
        lock (reportedUnknownTypes)
        {
            if (reportedUnknownTypes.Add(type) == false) return;
        }

        Console.WriteLine(
            $"[{context.RoomCode}] 모르는 메시지 종류 : {type}" +
            "  (PacketHandlerRegistry 에 핸들러를 등록했는지 보세요)");
    }
}
