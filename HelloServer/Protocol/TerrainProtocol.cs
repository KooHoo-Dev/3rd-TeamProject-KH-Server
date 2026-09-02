namespace HelloServer;

public sealed class TerrainExcavationRequest : PacketHeader
{
    public TerrainExcavationRequest()
    {
        Type = PacketTypes.TerrainExcavationRequest;
    }

    public long ClientRequestID { get; set; }
    public uint ExpectedTerrainRevision { get; set; }
    public GridCoord TargetCell { get; set; }
    public int ItemID { get; set; }
    public int DamageAmount { get; set; }

    public bool IsValid()
    {
        return ClientRequestID > 0 && DamageAmount > 0;
    }
}

public sealed class TerrainCollapsePlacementRequest : PacketHeader
{
    public TerrainCollapsePlacementRequest()
    {
        Type = PacketTypes.TerrainCollapsePlacementRequest;
    }

    public long CollapseID { get; set; }
    public uint ExpectedTerrainRevision { get; set; }
    public List<GridCoord> SourceCells { get; set; } = new();
    public List<TerrainCellChangeDto> Changes { get; set; } = new();

    public bool IsValid()
    {
        return CollapseID > 0 && SourceCells.Count > 0 &&
               SourceCells.Count == Changes.Count;
    }
}

public sealed class TerrainCollapseStartRequest : PacketHeader
{
    public TerrainCollapseStartRequest()
    {
        Type = PacketTypes.TerrainCollapseStartRequest;
    }

    public List<GridCoord> SourceCells { get; set; } = new();

    public bool IsValid() => SourceCells.Count > 0;
}

public sealed class TerrainCollapseStartedMessage : PacketHeader
{
    public TerrainCollapseStartedMessage()
    {
        Type = PacketTypes.TerrainCollapseStarted;
    }

    public long CollapseID { get; set; }
    public string OwnerPlayerID { get; set; }
    public uint StartedRevision { get; set; }
    public List<GridCoord> SourceCells { get; set; } = new();
}

public struct TerrainLootEntryDto
{
    public int ItemID { get; set; }
    public int Quantity { get; set; }

    public TerrainLootEntryDto(int itemID, int quantity)
    {
        ItemID = itemID;
        Quantity = quantity;
    }
}

public struct TerrainCellChangeDto
{
    public GridCoord Coord { get; set; }
    public int TileTypeID { get; set; }
    public int Durability { get; set; }
    public int ResourceID { get; set; }
    public List<TerrainLootEntryDto> LootEntries { get; set; }
}

public sealed class TerrainChangeBatchDto
{
    public string MapSessionID { get; set; }
    public long CollapseID { get; set; }
    public uint BaseRevision { get; set; }
    public uint ResultRevision { get; set; }
    public List<TerrainCellChangeDto> Changes { get; set; } = new();
}

public sealed class TerrainSnapshotDto
{
    public string MapSessionID { get; set; }
    public uint Revision { get; set; }
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }
    public float CellSize { get; set; }
    public float OriginX { get; set; }
    public float OriginY { get; set; }
    public int SpawnAreaOriginX { get; set; }
    public int SpawnAreaOriginY { get; set; }
    public int SpawnAreaWidth { get; set; }
    public int SpawnAreaHeight { get; set; }
    public List<TerrainCellChangeDto> Cells { get; set; } = new();
}

public sealed class TerrainChangeBatchMessage : PacketHeader
{
    public TerrainChangeBatchMessage()
    {
        Type = PacketTypes.TerrainChangeBatch;
    }

    public TerrainChangeBatchDto Batch { get; set; }
}

public sealed class TerrainSnapshotMessage : PacketHeader
{
    public TerrainSnapshotMessage()
    {
        Type = PacketTypes.TerrainSnapshot;
    }

    public TerrainSnapshotDto Snapshot { get; set; }
}

public sealed class ExplorationSnapshotDto
{
    public string MapSessionID { get; set; }
    public string PlayerID { get; set; }
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }
    public List<int> ExploredCellIndices { get; set; } = new();
}

public sealed class ExplorationSnapshotMessage : PacketHeader
{
    public ExplorationSnapshotMessage()
    {
        Type = PacketTypes.ExplorationSnapshot;
    }

    public ExplorationSnapshotDto Snapshot { get; set; }
}
