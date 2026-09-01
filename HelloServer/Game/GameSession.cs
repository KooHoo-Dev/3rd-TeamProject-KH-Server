namespace HelloServer;

// Room의 연결 수명과 분리된 서버 권위형 경기 상태 접근 지점입니다.
public sealed class GameSession
{
    private const float MaximumExcavationDistance = 4f;
    private const float MaximumPickupDistance = 3f;
    private const int ServerDamagePerExcavation = 1;

    private readonly object stateGate = new();
    private readonly ServerTerrainCatalog terrainCatalog;
    private long lastDropID;

    public RoomState State { get; } = new();

    public GameSession(string roomCode)
    {
        string dataRoot = Path.Combine(AppContext.BaseDirectory, "Data", "Terrain");
        terrainCatalog = new ServerTerrainCatalog(dataRoot);
        int seed = Random.Shared.Next(1, int.MaxValue);
        ServerGeneratedTerrain generated =
            new ServerTerrainGenerator(terrainCatalog).Generate(roomCode, "Default", seed);
        SetGeneratedTerrain(generated);
    }

    public void AddPlayer(User user)
    {
        State.Players[user.Id] = new PlayerRoomState
        {
            Id = user.Id,
            NickName = user.NickName,
        };
        State.Inventory.Players.TryAdd(user.Id, new PlayerInventoryRoomState());
    }

    public void RemovePlayer(string playerId)
    {
        State.Players.TryRemove(playerId, out _);
        State.Exploration.Players.TryRemove(playerId, out _);
        State.Inventory.Players.TryRemove(playerId, out _);
    }

    public void MovePlayer(string playerId, float x, float y)
    {
        if (float.IsFinite(x) == false || float.IsFinite(y) == false) return;
        if (State.Players.TryGetValue(playerId, out PlayerRoomState player) == false)
            return;

        player.X = x;
        player.Y = y;
    }

    public PlayerState[] CreatePlayerStateSnapshot()
    {
        List<PlayerState> states = new();
        foreach (PlayerRoomState player in State.Players.Values)
        {
            states.Add(new PlayerState
            {
                Id = player.Id,
                X = player.X,
                Y = player.Y,
            });
        }

        return states.ToArray();
    }

    public bool TryCreateMapSessionMessage(out MapSessionMessage message)
    {
        MapSessionDescriptor descriptor = State.MapSession.Descriptor;
        if (descriptor == null)
        {
            message = null;
            return false;
        }

        message = new MapSessionMessage { Session = descriptor };
        return true;
    }

    public TerrainSnapshotMessage CreateTerrainSnapshotMessage(string requestId = null)
    {
        lock (stateGate)
        {
            return new TerrainSnapshotMessage
            {
                RequestId = requestId,
                Snapshot = new TerrainSnapshotDto
                {
                    MapSessionID = State.MapSession.Descriptor.MapSessionID,
                    Revision = State.Terrain.Revision,
                    MapWidth = State.Terrain.MapWidth,
                    MapHeight = State.Terrain.MapHeight,
                    CellSize = State.Terrain.CellSize,
                    OriginX = State.Terrain.OriginX,
                    OriginY = State.Terrain.OriginY,
                    Cells = State.Terrain.Cells
                        .OrderBy(pair => pair.Key.Y)
                        .ThenBy(pair => pair.Key.X)
                        .Select(pair => CreateCellChange(pair.Key, pair.Value))
                        .ToList()
                }
            };
        }
    }

    public WorldItemSnapshotMessage CreateWorldItemSnapshotMessage()
    {
        lock (stateGate)
        {
            return new WorldItemSnapshotMessage
            {
                Drops = State.WorldItems.Drops.Values
                    .OrderBy(value => value.DropID, StringComparer.Ordinal)
                    .Select(CloneDrop)
                    .ToArray(),
            };
        }
    }

    public InventorySnapshotMessage CreateInventorySnapshotMessage(
        string playerId,
        string requestId = null)
    {
        lock (stateGate)
        {
            return CreateInventorySnapshotUnsafe(playerId, requestId);
        }
    }

    public bool TryExcavate(
        string playerId,
        TerrainExcavationRequest request,
        out TerrainChangeBatchMessage terrainMessage,
        out WorldItemSpawnedMessage[] spawnedMessages,
        out string errorCode,
        out string errorMessage)
    {
        terrainMessage = null;
        spawnedMessages = Array.Empty<WorldItemSpawnedMessage>();
        errorCode = null;
        errorMessage = null;

        lock (stateGate)
        {
            if (request?.IsValid() != true)
                return Fail("terrain.invalid_request", "유효하지 않은 채굴 요청입니다.", out errorCode, out errorMessage);
            if (request.ExpectedTerrainRevision != State.Terrain.Revision)
                return Fail("terrain.revision_mismatch", "지형 Revision이 일치하지 않습니다.", out errorCode, out errorMessage);
            if (State.Players.TryGetValue(playerId, out PlayerRoomState player) == false)
                return Fail("player.not_found", "플레이어 상태를 찾을 수 없습니다.", out errorCode, out errorMessage);
            if (IsWithinDistance(player, request.TargetCell, MaximumExcavationDistance) == false)
                return Fail("terrain.out_of_range", "채굴 대상이 서버 허용 거리 밖입니다.", out errorCode, out errorMessage);
            if (State.Terrain.Cells.TryGetValue(request.TargetCell, out TerrainCellRoomState cell) == false)
                return Fail("terrain.empty_cell", "대상 셀에 지형이 없습니다.", out errorCode, out errorMessage);

            ServerTerrainTileType tileType = (ServerTerrainTileType)cell.TileTypeID;
            ServerTerrainCatalog.TileDefinition tileDefinition = terrainCatalog.GetTile(tileType);
            if (tileDefinition.IsMineable == false)
                return Fail("terrain.not_mineable", "채굴할 수 없는 지형입니다.", out errorCode, out errorMessage);

            uint baseRevision = State.Terrain.Revision;
            int remaining = Math.Max(0, cell.Durability - ServerDamagePerExcavation);
            TerrainCellChangeDto change;
            List<WorldItemSpawnedMessage> spawned = new();
            if (remaining > 0)
            {
                cell.Durability = remaining;
                change = CreateCellChange(request.TargetCell, cell);
            }
            else
            {
                State.Terrain.Cells.TryRemove(request.TargetCell, out _);
                change = new TerrainCellChangeDto
                {
                    Coord = request.TargetCell,
                    TileTypeID = (int)ServerTerrainTileType.Empty,
                    Durability = 0,
                    ResourceID = 0,
                    LootEntries = new List<TerrainLootEntryDto>(),
                };
                CreateDropsForDestroyedCell(request.TargetCell, cell, request.RequestId, spawned);
            }

            State.Terrain.Revision = baseRevision + 1;
            terrainMessage = new TerrainChangeBatchMessage
            {
                RequestId = request.RequestId,
                Batch = new TerrainChangeBatchDto
                {
                    MapSessionID = State.MapSession.Descriptor.MapSessionID,
                    CollapseID = request.ClientRequestID,
                    BaseRevision = baseRevision,
                    ResultRevision = State.Terrain.Revision,
                    Changes = new List<TerrainCellChangeDto> { change },
                },
            };
            spawnedMessages = spawned.ToArray();
            return true;
        }
    }

    public bool TryPickup(
        string playerId,
        WorldItemPickupRequest request,
        out WorldItemRemovedMessage removedMessage,
        out InventorySnapshotMessage inventoryMessage,
        out string errorCode,
        out string errorMessage)
    {
        removedMessage = null;
        inventoryMessage = null;
        errorCode = null;
        errorMessage = null;

        lock (stateGate)
        {
            if (string.IsNullOrWhiteSpace(request?.DropID))
                return Fail("item.invalid_request", "유효하지 않은 아이템 획득 요청입니다.", out errorCode, out errorMessage);
            if (State.Players.TryGetValue(playerId, out PlayerRoomState player) == false)
                return Fail("player.not_found", "플레이어 상태를 찾을 수 없습니다.", out errorCode, out errorMessage);
            if (State.WorldItems.Drops.TryGetValue(request.DropID, out WorldItemDropDto drop) == false)
                return Fail("item.not_found", "이미 획득되었거나 존재하지 않는 아이템입니다.", out errorCode, out errorMessage);

            float dx = player.X - drop.X;
            float dy = player.Y - drop.Y;
            if (dx * dx + dy * dy > MaximumPickupDistance * MaximumPickupDistance)
                return Fail("item.out_of_range", "아이템이 서버 허용 거리 밖입니다.", out errorCode, out errorMessage);
            if (State.Inventory.Players.TryGetValue(playerId, out PlayerInventoryRoomState inventory) == false)
                return Fail("inventory.not_found", "플레이어 인벤토리를 찾을 수 없습니다.", out errorCode, out errorMessage);

            int previous = inventory.Quantities.GetValueOrDefault(drop.ItemID);
            if (previous > int.MaxValue - drop.Quantity)
                return Fail("inventory.overflow", "아이템 수량 한도를 초과했습니다.", out errorCode, out errorMessage);

            inventory.Quantities[drop.ItemID] = previous + drop.Quantity;
            State.WorldItems.Drops.TryRemove(drop.DropID, out _);
            removedMessage = new WorldItemRemovedMessage
            {
                RequestId = request.RequestId,
                DropID = drop.DropID,
                CollectedByPlayerID = playerId,
            };
            inventoryMessage = CreateInventorySnapshotUnsafe(playerId, request.RequestId);
            return true;
        }
    }

    private void SetGeneratedTerrain(ServerGeneratedTerrain generated)
    {
        State.MapSession.Descriptor = generated.Session;
        State.Terrain.Revision = generated.Snapshot.Revision;
        State.Terrain.MapWidth = generated.Snapshot.MapWidth;
        State.Terrain.MapHeight = generated.Snapshot.MapHeight;
        State.Terrain.CellSize = generated.Snapshot.CellSize;
        State.Terrain.OriginX = generated.Snapshot.OriginX;
        State.Terrain.OriginY = generated.Snapshot.OriginY;
        foreach (TerrainCellChangeDto cell in generated.Snapshot.Cells)
        {
            State.Terrain.Cells[cell.Coord] = new TerrainCellRoomState
            {
                TileTypeID = cell.TileTypeID,
                Durability = cell.Durability,
                ResourceID = cell.ResourceID,
                LootEntries = cell.LootEntries?.ToList() ?? new List<TerrainLootEntryDto>(),
            };
        }
    }

    private TerrainSnapshotDto CreateTerrainSnapshot()
    {
        return new TerrainSnapshotDto
        {
            MapSessionID = State.MapSession.Descriptor.MapSessionID,
            Revision = State.Terrain.Revision,
            MapWidth = State.Terrain.MapWidth,
            MapHeight = State.Terrain.MapHeight,
            CellSize = State.Terrain.CellSize,
            OriginX = State.Terrain.OriginX,
            OriginY = State.Terrain.OriginY,
            Cells = State.Terrain.Cells
                .Select(pair => CreateCellChange(pair.Key, pair.Value))
                .OrderBy(value => value.Coord)
                .ToList(),
        };
    }

    private InventorySnapshotMessage CreateInventorySnapshotUnsafe(
        string playerId,
        string requestId)
    {
        State.Inventory.Players.TryGetValue(playerId, out PlayerInventoryRoomState inventory);
        InventoryItemDto[] items = inventory == null
            ? Array.Empty<InventoryItemDto>()
            : inventory.Quantities
                .OrderBy(pair => pair.Key)
                .Select(pair => new InventoryItemDto
                {
                    ItemID = pair.Key,
                    Quantity = pair.Value,
                })
                .ToArray();
        return new InventorySnapshotMessage
        {
            RequestId = requestId,
            PlayerID = playerId,
            Items = items,
        };
    }

    private void CreateDropsForDestroyedCell(
        GridCoord coord,
        TerrainCellRoomState cell,
        string requestId,
        List<WorldItemSpawnedMessage> spawned)
    {
        if (cell.ResourceID > 0 &&
            terrainCatalog.TryGetResource(
                cell.ResourceID,
                out ServerTerrainCatalog.ResourceDefinition resource))
            spawned.Add(CreateDrop(coord, resource.DropItemID, resource.DropCount, requestId));

        foreach (TerrainLootEntryDto loot in cell.LootEntries)
        {
            if (loot.ItemID <= 0 || loot.Quantity <= 0) continue;
            spawned.Add(CreateDrop(coord, loot.ItemID, loot.Quantity, requestId));
        }
    }

    private WorldItemSpawnedMessage CreateDrop(
        GridCoord coord,
        int itemID,
        int quantity,
        string requestId)
    {
        WorldItemDropDto drop = new()
        {
            DropID = $"d{Interlocked.Increment(ref lastDropID)}",
            ItemID = itemID,
            Quantity = quantity,
            X = State.Terrain.OriginX + (coord.X + 0.5f) * State.Terrain.CellSize,
            Y = State.Terrain.OriginY + (coord.Y + 0.5f) * State.Terrain.CellSize,
        };
        State.WorldItems.Drops[drop.DropID] = drop;
        return new WorldItemSpawnedMessage
        {
            RequestId = requestId,
            Drop = CloneDrop(drop),
        };
    }

    private bool IsWithinDistance(
        PlayerRoomState player,
        GridCoord coord,
        float maximumDistance)
    {
        float x = State.Terrain.OriginX + (coord.X + 0.5f) * State.Terrain.CellSize;
        float y = State.Terrain.OriginY + (coord.Y + 0.5f) * State.Terrain.CellSize;
        float dx = player.X - x;
        float dy = player.Y - y;
        return dx * dx + dy * dy <= maximumDistance * maximumDistance;
    }

    private static TerrainCellChangeDto CreateCellChange(
        GridCoord coord,
        TerrainCellRoomState cell)
    {
        return new TerrainCellChangeDto
        {
            Coord = coord,
            TileTypeID = cell.TileTypeID,
            Durability = cell.Durability,
            ResourceID = cell.ResourceID,
            LootEntries = cell.LootEntries?.ToList() ?? new List<TerrainLootEntryDto>(),
        };
    }

    private static WorldItemDropDto CloneDrop(WorldItemDropDto drop)
    {
        return new WorldItemDropDto
        {
            DropID = drop.DropID,
            ItemID = drop.ItemID,
            Quantity = drop.Quantity,
            X = drop.X,
            Y = drop.Y,
        };
    }

    private static bool Fail(
        string code,
        string message,
        out string errorCode,
        out string errorMessage)
    {
        errorCode = code;
        errorMessage = message;
        return false;
    }
}
