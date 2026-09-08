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
    public DynamiteRoomState Dynamites { get; } = new();
    public TorchRoomState Torches { get; } = new();
    public GameFlowRoomState GameFlow { get; } = new();
}

public sealed class GameFlowRoomState
{
    public int ExpectedPlayerCount { get; internal set; }
    public HashSet<string> ReadyPlayerIDs { get; } = new();
    public bool IsStarted { get; internal set; }
    public long StartedAtUnixMilliseconds { get; internal set; }
    public long EndsAtUnixMilliseconds { get; internal set; }
    public bool IsEnded { get; internal set; }
    public long EndedAtUnixMilliseconds { get; internal set; }
    public string EndReason { get; internal set; }
    public string[] WinnerPlayerIDs { get; internal set; } = Array.Empty<string>();
}

public sealed class PlayerRoomState
{
    public string Id { get; init; }
    public string NickName { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public bool IsGrounded { get; set; }
    public bool IsClimbing { get; set; }
    public bool IsBuried { get; set; }
    public int CurrentHealth { get; set; }
    public int MaxHealth { get; set; }
    public bool IsDead { get; set; }
    public bool IsDebugMode { get; init; }
    public float DeathX { get; set; }
    public float DeathY { get; set; }
    public int AssignedSpawnCellX { get; set; }
    public int EquippedPickaxeItemID { get; set; }
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
    // playerconfig.json에서 접속 시점에 확정됩니다.
    public int MaxWeight { get; init; }
    public Dictionary<int, int> Quantities { get; } = new();
    public Stack<ShopPurchaseRecord> ShopPurchaseHistory { get; } = new();
}

public readonly record struct ShopPurchaseRecord(int ItemID, int Quantity, int PaidGold);

public sealed class WorldItemRoomState
{
    public Dictionary<string, WorldItemDropDto> Drops { get; } = new();
}

public sealed class DynamiteRoomState
{
    // GameSession.stateGate 안에서만 접근
    public Dictionary<string, PendingDynamiteState> Projectiles { get; } = new();
}

public sealed class TorchRoomState
{
    // GameSession.stateGate 안에서만 접근
    public HashSet<GridCoord> Cells { get; } = new();
}

public sealed class PendingDynamiteState
{
    public string ProjectileID { get; init; }
    public string OwnerPlayerID { get; init; }
    public int ItemID { get; init; }

    public float StartX { get; init; }
    public float StartY { get; init; }
    public float DirectionX { get; init; }
    public float DirectionY { get; init; }

    public long StartedAtUnixMilliseconds { get; init; }

    public float ThrowSpeed { get; init; }
    public float FuseTime { get; init; }
    public float ExplosionRadius { get; init; }
    public int ExplosionPower { get; init; }
    public bool IsMine { get; init; }
    public GridCoord Cell { get; init; }
    public float ArmDelay { get; init; }
    public float DetectionRadius { get; init; }
}
