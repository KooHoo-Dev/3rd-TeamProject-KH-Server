using System.Collections.Concurrent;

namespace HelloServer;

// 한 방에서 서버가 확정하는 모든 게임 상태의 최상위 소유자입니다.
public sealed class RoomState
{
    public ConcurrentDictionary<string, PlayerRoomState> Players { get; } = new();
    public MapSessionRoomState MapSession { get; } = new();
    public TerrainRoomState Terrain { get; } = new();
    public InventoryRoomState Inventory { get; } = new();
    public WorldItemRoomState WorldItems { get; } = new();
}

public sealed class PlayerRoomState
{
    public string Id { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
    public int EquippedPickaxeItemID { get; set; } = 2; // ID 2: 개발용 곡괭이
}

public sealed class MapSessionRoomState
{
    public MapSessionDescriptor Descriptor { get; internal set; }
}

public sealed class TerrainRoomState
{
    public uint Revision { get; internal set; }
    public int MapWidth { get; internal set; }
    public int MapHeight { get; internal set; }
    public float CellSize { get; internal set; } = 1f;
    public float OriginX { get; internal set; }
    public float OriginY { get; internal set; }
    // GameSession.stateGate 안에서만 접근한다.
    public Dictionary<GridCoord, TerrainCellRoomState> Cells { get; } = new();
    public Dictionary<long, PendingCollapseState> PendingCollapses { get; } = new();
    public HashSet<GridCoord> ReservedCollapseCells { get; } = new();
}

public sealed class PendingCollapseState
{
    public long CollapseID { get; init; }
    public string OwnerPlayerID { get; init; }
    public uint StartedRevision { get; init; }

    // 시간 초과로 거두기 위한 값입니다. Environment.TickCount64 를 그대로 담습니다.
    public long StartedAtMilliseconds { get; init; }
    public HashSet<GridCoord> SourceCells { get; init; } = new();
}

public sealed class TerrainCellRoomState
{
    public int TileTypeID { get; set; }
    public int Durability { get; set; }
    public int ResourceID { get; set; }
    public TerrainLootEntryDto[] LootEntries { get; set; } = Array.Empty<TerrainLootEntryDto>();
}

public sealed class InventoryRoomState
{
    public Dictionary<string, PlayerInventoryRoomState> Players { get; } = new();
}

public sealed class PlayerInventoryRoomState
{
    public Dictionary<int, int> Quantities { get; } = new();
}

public sealed class WorldItemRoomState
{
    public Dictionary<string, WorldItemDropDto> Drops { get; } = new();
}
