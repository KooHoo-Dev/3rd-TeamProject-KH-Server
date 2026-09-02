using System.Collections.Concurrent;

namespace HelloServer;

// 한 방에서 서버가 확정하는 모든 게임 상태의 최상위 소유자입니다.
public sealed class RoomState
{
    public ConcurrentDictionary<string, PlayerRoomState> Players { get; } = new();
    public MapSessionRoomState MapSession { get; } = new();
    public TerrainRoomState Terrain { get; } = new();
    public ExplorationRoomState Exploration { get; } = new();

    // 다음 도메인 구현을 위한 상태 경계입니다. 실제 규칙은 아직 없습니다.
    public InventoryRoomState Inventory { get; } = new();
    public WorldItemRoomState WorldItems { get; } = new();
    public ShopRoomState Shop { get; } = new();
    public EconomyRoomState Economy { get; } = new();
}

public sealed class PlayerRoomState
{
    public string Id { get; init; }
    public string NickName { get; init; }
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
    public int SpawnAreaOriginX { get; internal set; }
    public int SpawnAreaOriginY { get; internal set; }
    public int SpawnAreaWidth { get; internal set; }
    public int SpawnAreaHeight { get; internal set; }
    public ConcurrentDictionary<GridCoord, TerrainCellRoomState> Cells { get; } = new();
    // GameSession.stateGate 안에서만 접근한다.
    public Dictionary<long, PendingCollapseState> PendingCollapses { get; } = new();
    public HashSet<GridCoord> ReservedCollapseCells { get; } = new();
}

public sealed class PendingCollapseState
{
    public long CollapseID { get; init; }
    public string OwnerPlayerID { get; init; }
    public uint StartedRevision { get; init; }
    public HashSet<GridCoord> SourceCells { get; init; } = new();
}

public sealed class TerrainCellRoomState
{
    public int TileTypeID { get; set; }
    public int Durability { get; set; }
    public int ResourceID { get; set; }
    public List<TerrainLootEntryDto> LootEntries { get; set; } = new();
}

public sealed class ExplorationRoomState
{
    public ConcurrentDictionary<string, PlayerExplorationRoomState> Players { get; } = new();
}

public sealed class PlayerExplorationRoomState
{
    public ConcurrentDictionary<int, byte> ExploredCellIndices { get; } = new();
}

public sealed class InventoryRoomState
{
    public ConcurrentDictionary<string, PlayerInventoryRoomState> Players { get; } = new();
}

public sealed class PlayerInventoryRoomState
{
    public ConcurrentDictionary<int, int> Quantities { get; } = new();
}

public sealed class ShopRoomState
{
}

public sealed class EconomyRoomState
{
}

public sealed class WorldItemRoomState
{
    public ConcurrentDictionary<string, WorldItemDropDto> Drops { get; } = new();
}
