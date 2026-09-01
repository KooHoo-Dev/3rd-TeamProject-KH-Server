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
    public ShopRoomState Shop { get; } = new();
    public EconomyRoomState Economy { get; } = new();
}

public sealed class PlayerRoomState
{
    public string Id { get; init; }
    public string NickName { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
}

public sealed class MapSessionRoomState
{
    public MapSessionDescriptor Descriptor { get; internal set; }
}

public sealed class TerrainRoomState
{
    public uint Revision { get; internal set; }
    public ConcurrentDictionary<GridCoord, TerrainCellRoomState> Cells { get; } = new();
}

public sealed class TerrainCellRoomState
{
    public int TileTypeID { get; set; }
    public int Durability { get; set; }
    public int ResourceID { get; set; }
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
}

public sealed class ShopRoomState
{
}

public sealed class EconomyRoomState
{
}
