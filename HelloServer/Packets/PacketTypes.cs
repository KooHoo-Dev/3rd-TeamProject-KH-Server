namespace HelloServer;

public static class PacketTypes
{
    public const string Hello = "hello";
    public const string Welcome = "welcome";
    public const string Join = "join";
    public const string Leave = "leave";
    public const string LegacyMove = "move";
    public const string LegacyChat = "chat";
    public const string LegacyState = "state";

    public const string PlayerMove = "player.move";
    public const string ChatSend = "chat.send";

    // refactor/server-sync-foundation 클라이언트 계약과 동일한 Type 값입니다.
    public const string MapSession = "map_session";
    public const string TerrainExcavationRequest = "terrain_excavate";
    public const string TerrainCollapseStartRequest = "terrain_collapse_start";
    public const string TerrainCollapseStarted = "terrain_collapse_started";
    public const string TerrainCollapseCancelled = "terrain_collapse_cancelled";
    public const string TerrainCollapsePlacementRequest = "terrain_collapse_place";
    public const string TerrainChangeBatch = "terrain_batch";

    public const string WorldItemSpawned = "world_item.spawned";
    public const string WorldItemRemoved = "world_item.removed";
    public const string WorldItemPickup = "world_item.pickup";
    public const string WorldItemDrop = "world_item.drop";
    public const string WorldItemSnapshot = "world_item.snapshot";
    public const string InventorySnapshot = "inventory.snapshot";
    public const string TerrainDeathLootRequest = "terrain_death_loot";
    public const string Error = "error";
}
