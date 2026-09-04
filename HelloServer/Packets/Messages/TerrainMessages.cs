namespace HelloServer;

public sealed class TerrainExcavationRequest : PacketHeader
{
    public TerrainExcavationRequest()
    {
        Type = PacketTypes.TerrainExcavationRequest;
    }

    public GridCoord TargetCell { get; set; }
    public int ItemID { get; set; }
    // 서버 곡괭이 DigPower 사용에 따른 DamageAmount 계약 제거
    public bool IsValid() => ItemID > 0;
}

public sealed class TerrainDeathLootRequest : PacketHeader
{
    public TerrainDeathLootRequest()
    {
        Type = PacketTypes.TerrainDeathLootRequest;
    }
}

public sealed class TerrainCollapsePlacementRequest : PacketHeader
{
    public TerrainCollapsePlacementRequest()
    {
        Type = PacketTypes.TerrainCollapsePlacementRequest;
    }

    public long CollapseID { get; set; }
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

public readonly struct TerrainLootEntryDto
{
    public int ItemID { get; init; }
    public int Quantity { get; init; }

    public TerrainLootEntryDto(int itemID, int quantity)
    {
        ItemID = itemID;
        Quantity = quantity;
    }
}

public readonly struct TerrainCellChangeDto
{
    public GridCoord Coord { get; init; }
    public int TileTypeID { get; init; }
    public int Durability { get; init; }
    public int ResourceID { get; init; }
    public TerrainLootEntryDto[] LootEntries { get; init; }
}

public sealed class TerrainChangeBatchDto
{
    public string MapSessionID { get; set; }
    public long CollapseID { get; set; }
    public uint BaseRevision { get; set; }
    public uint ResultRevision { get; set; }
    public List<TerrainCellChangeDto> Changes { get; set; } = new();
}

// 지형 생성기가 내놓는 결과입니다. 더 이상 클라이언트로 나가지 않습니다.
// 클라이언트는 같은 시드로 같은 지형을 직접 만듭니다.
public sealed class TerrainSnapshotDto
{
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

// 잡아 둔 낙하를 무르라고 알립니다.
//
// 예약을 푸는 것은 지형을 바꾸지 않습니다. 칸은 그대로 있습니다.
// 그래서 지형을 다시 보낼 일이 없고, 멈출 덩어리의 번호만 있으면 됩니다.
// 예전에는 이 자리에서 지형 전체(482KB)를 뿌렸습니다. 이제 55바이트입니다.
public sealed class TerrainCollapseCancelledMessage : PacketHeader
{
    public TerrainCollapseCancelledMessage()
    {
        Type = PacketTypes.TerrainCollapseCancelled;
    }

    public List<long> CollapseIDs { get; set; } = new();

    // 되돌릴 칸을 함께 보냅니다. 번호만으로는 모자랍니다.
    //
    // 클라이언트는 낙하가 시작될 때 원본 칸을 자기 화면에서 지우고
    // 그 내용을 떨어지는 덩어리가 들고 있습니다.
    // 덩어리가 아직 떠 있으면 그것으로 되돌릴 수 있지만,
    // 착지하면 덩어리는 곧바로 지워져서 되돌릴 밑천이 사라집니다.
    // 확정 거절이 오는 시점이 바로 그 뒤입니다.
    //
    // 서버에는 그 칸이 그대로 남아 있으므로 서버가 실어 보냅니다.
    public List<TerrainCellChangeDto> RestoreCells { get; set; } = new();
}
