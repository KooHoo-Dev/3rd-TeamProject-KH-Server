namespace HelloServer;

public sealed class UsableItemUseRequest : PacketHeader
{
    public UsableItemUseRequest() => Type = PacketTypes.UsableItemUse;
    public int ItemID { get; set; }
    public bool IsValid() => ItemID > 0;
}

public sealed class UsableItemUsedMessage : PacketHeader
{
    public UsableItemUsedMessage() => Type = PacketTypes.UsableItemUsed;
    public int ItemID { get; set; }
    public GridCoord[] DetectedCells { get; set; } = Array.Empty<GridCoord>();
}
