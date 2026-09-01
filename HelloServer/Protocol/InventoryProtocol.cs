namespace HelloServer;

public sealed class InventoryItemDto
{
    public int ItemID { get; set; }
    public int Quantity { get; set; }
}

public sealed class InventorySnapshotMessage : PacketHeader
{
    public InventorySnapshotMessage()
    {
        Type = PacketTypes.InventorySnapshot;
    }

    public string PlayerID { get; set; }
    public InventoryItemDto[] Items { get; set; }
}

public sealed class WorldItemDropDto
{
    public string DropID { get; set; }
    public int ItemID { get; set; }
    public int Quantity { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
}

public sealed class WorldItemSpawnedMessage : PacketHeader
{
    public WorldItemSpawnedMessage()
    {
        Type = PacketTypes.WorldItemSpawned;
    }

    public WorldItemDropDto Drop { get; set; }
}

public sealed class WorldItemRemovedMessage : PacketHeader
{
    public WorldItemRemovedMessage()
    {
        Type = PacketTypes.WorldItemRemoved;
    }

    public string DropID { get; set; }
    public string CollectedByPlayerID { get; set; }
}

public sealed class WorldItemPickupRequest : PacketHeader
{
    public WorldItemPickupRequest()
    {
        Type = PacketTypes.WorldItemPickup;
    }

    public string DropID { get; set; }
}

public sealed class WorldItemSnapshotMessage : PacketHeader
{
    public WorldItemSnapshotMessage()
    {
        Type = PacketTypes.WorldItemSnapshot;
    }

    public WorldItemDropDto[] Drops { get; set; }
}
