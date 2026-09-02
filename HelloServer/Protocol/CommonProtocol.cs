using System.Text.Json.Serialization;

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
    public const string TerrainExcavate = "terrain.excavate";
    public const string TerrainChangeBatch = "terrain_batch";
    public const string TerrainSnapshot = "terrain_snapshot";

    public const string WorldItemSpawned = "world_item.spawned";
    public const string WorldItemRemoved = "world_item.removed";
    public const string WorldItemPickup = "world_item.pickup";
    public const string WorldItemSnapshot = "world_item.snapshot";
    public const string InventorySnapshot = "inventory.snapshot";
    public const string Error = "error";
}

public sealed class ErrorMessage : PacketHeader
{
    public ErrorMessage()
    {
        Type = PacketTypes.Error;
    }

    public string Code { get; set; }
    public string Message { get; set; }
}

// 모든 메시지에서 Type을 먼저 읽고 선택적으로 요청을 연결할 수 있는 공통 헤더입니다.
public class PacketHeader
{
    public string Type { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string RequestId { get; set; }
}

public class User
{
    public string Id { get; set; }
    public string NickName { get; set; }
}

public class HelloMessage : PacketHeader
{
    public string NickName { get; set; }
}

public class WelcomeMessage : PacketHeader
{
    public WelcomeMessage()
    {
        Type = PacketTypes.Welcome;
    }

    public string RoomCode { get; set; }
    public User User { get; set; }
    public User[] Users { get; set; }
}

public class JoinMessage : PacketHeader
{
    public JoinMessage()
    {
        Type = PacketTypes.Join;
    }

    public User User { get; set; }
}

public class LeaveMessage : PacketHeader
{
    public LeaveMessage()
    {
        Type = PacketTypes.Leave;
    }

    public string Id { get; set; }
}
