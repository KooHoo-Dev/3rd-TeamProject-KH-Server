namespace HelloServer;

public sealed partial class GameSession
{
    public bool TryCreateDeathLoot(
        string playerId,
        TerrainDeathLootRequest request,
        out TerrainChangeBatchMessage terrainMessage,
        out InventorySnapshotMessage inventoryMessage,
        out string errorCode,
        out string errorMessage)
    {
        terrainMessage = null;
        inventoryMessage = null;
        errorCode = null;
        errorMessage = null;

        lock (stateGate)
        {
            if (request == null)
                return Fail("terrain.invalid_death_loot", "유효하지 않은 사망 보관함 요청입니다.", out errorCode, out errorMessage);
            if (State.Players.TryGetValue(playerId, out PlayerRoomState player) == false)
                return Fail("player.not_found", "플레이어 상태를 찾을 수 없습니다.", out errorCode, out errorMessage);
            if (player.IsDead == false)
                return Fail("player.not_dead", "사망 상태에서만 사망 보관함을 만들 수 있습니다.", out errorCode, out errorMessage);
            if (State.Inventory.Players.TryGetValue(playerId, out PlayerInventoryRoomState inventory) == false)
                return Fail("inventory.not_found", "플레이어 인벤토리를 찾을 수 없습니다.", out errorCode, out errorMessage);

            GridCoord deathCell = new(
                (int)Math.Floor((player.X - State.Terrain.OriginX) / State.Terrain.CellSize),
                (int)Math.Floor((player.Y - State.Terrain.OriginY) / State.Terrain.CellSize));
            if (deathCell.X < 0 || deathCell.X >= State.Terrain.MapWidth ||
                deathCell.Y < 0 || deathCell.Y >= State.Terrain.MapHeight)
                return Fail("terrain.invalid_death_loot", "사망 위치가 맵 범위 밖입니다.", out errorCode, out errorMessage);
            if (State.Terrain.ReservedCollapseCells.Contains(deathCell))
                return Fail("terrain.collapse_pending", "낙하 중인 위치에는 사망 보관함을 만들 수 없습니다.", out errorCode, out errorMessage);
            if (State.Terrain.Cells.TryGetValue(deathCell, out TerrainCellRoomState existing) &&
                (existing.TileTypeID == (int)ServerTerrainTileType.Bedrock ||
                 existing.TileTypeID == (int)ServerTerrainTileType.DeathLoot))
                return Fail("terrain.invalid_death_loot", "사망 보관함을 만들 수 없는 지형입니다.", out errorCode, out errorMessage);

            TerrainLootEntryDto[] lootEntries = inventory.Quantities
                .Where(pair => pair.Key > 0 && pair.Value > 0)
                .OrderBy(pair => pair.Key)
                .Select(pair => new TerrainLootEntryDto(pair.Key, pair.Value))
                .ToArray();
            if (lootEntries.Length == 0)
                return Fail("inventory.empty", "보관할 아이템이 없습니다.", out errorCode, out errorMessage);

            ServerTerrainCatalog.TileDefinition deathLootTile =
                terrainCatalog.GetTile(ServerTerrainTileType.DeathLoot);
            TerrainCellRoomState deathLootCell = new()
            {
                TileTypeID = (int)ServerTerrainTileType.DeathLoot,
                Durability = deathLootTile.MaxDurability,
                ResourceID = 0,
                LootEntries = lootEntries,
            };
            State.Terrain.Cells[deathCell] = deathLootCell;
            inventory.Quantities.Clear();
            player.IsDead = true;
            player.DeathX = player.X;
            player.DeathY = player.Y;

            uint baseRevision = State.Terrain.Revision;
            State.Terrain.Revision = baseRevision + 1;
            terrainMessage = new TerrainChangeBatchMessage
            {
                RequestId = request.RequestId,
                Batch = new TerrainChangeBatchDto
                {
                    MapSessionID = State.MapSession.Descriptor.MapSessionID,
                    CollapseID = 0,
                    BaseRevision = baseRevision,
                    ResultRevision = State.Terrain.Revision,
                    Changes = new List<TerrainCellChangeDto>
                    {
                        CreateCellChange(deathCell, deathLootCell),
                    },
                },
            };
            inventoryMessage = CreateInventorySnapshotUnsafe(playerId, request.RequestId);
            return true;
        }
    }

    private List<GridCoord> GetCellsInExplosionRadius(float worldX, float worldY, float radius)
    {
        List<GridCoord> cells = new();

        int centerX = (int)Math.Floor((worldX - State.Terrain.OriginX) / State.Terrain.CellSize);
        int centerY = (int)Math.Floor((worldY - State.Terrain.OriginY) / State.Terrain.CellSize);
        int cellRadius = (int)Math.Ceiling(radius / State.Terrain.CellSize);
        float radiusSqr = radius * radius;

        for (int x = centerX - cellRadius; x <= centerX + cellRadius; x++)
        {
            for (int y = centerY - cellRadius; y <= centerY + cellRadius; y++)
            {
                if (x < 0 || x >= State.Terrain.MapWidth ||
                    y < 0 || y >= State.Terrain.MapHeight)
                    continue;

                float cellCenterX = State.Terrain.OriginX + (x + 0.5f) * State.Terrain.CellSize;
                float cellCenterY = State.Terrain.OriginY + (y + 0.5f) * State.Terrain.CellSize;

                float dx = cellCenterX - worldX;
                float dy = cellCenterY - worldY;

                if (dx * dx + dy * dy <= radiusSqr)
                    cells.Add(new GridCoord(x, y));
            }
        }

        // 기존 로컬 폭발 코드처럼 위쪽 타일부터 처리
        return cells
            .OrderByDescending(cell => cell.Y)
            .ThenBy(cell => cell.X)
            .ToList();
    }

    // 굴착은 칸 하나짜리 독립 연산이라 Revision 을 대조하지 않습니다.
    //
    // Revision 은 방에 하나뿐인 번호입니다. 남이 반대편 칸을 파도 올라갑니다.
    // 그것을 요청과 대조하면 동시에 파는 것 자체가 거절 사유가 됩니다.
    // 이 게임은 재접속도 중간참여도 없어 되맞출 일이 없습니다.
    // 순서는 Batch 의 BaseRevision/ResultRevision 이 이어서 지킵니다.
    public bool TryExcavate(string playerId, TerrainExcavationRequest request,
        out TerrainChangeBatchMessage terrainMessage, out WorldItemSpawnedMessage[] spawnedMessages,
        out string errorCode, out string errorMessage)
    {
        terrainMessage = null;
        spawnedMessages = Array.Empty<WorldItemSpawnedMessage>();
        errorCode = null;
        errorMessage = null;

        lock (stateGate)
        {
            if (request?.IsValid() != true)
                return Fail("terrain.invalid_request", "유효하지 않은 채굴 요청입니다.", out errorCode, out errorMessage);
            
            if (State.Terrain.ReservedCollapseCells.Contains(request.TargetCell))
                return Fail("terrain.collapse_pending", "낙하 중인 지형은 채굴할 수 없습니다.", out errorCode, out errorMessage);
            
            if (State.Players.TryGetValue(playerId, out PlayerRoomState player) == false)
                return Fail("player.not_found", "플레이어 상태를 찾을 수 없습니다.", out errorCode, out errorMessage);
            
            if (request.ItemID != player.EquippedPickaxeItemID ||
                false == itemCatalog.TryGetPickaxe(
                    player.EquippedPickaxeItemID,
                    out ServerItemCatalog.PickaxeDefinition pickaxe))
                return Fail("terrain.invalid_pickaxe", "서버에 장착된 곡괭이와 일치하지 않습니다.", out errorCode, out errorMessage);
            
            if (IsWithinDistance(player, request.TargetCell, pickaxe.Range) == false)
                return Fail("terrain.out_of_range", "채굴 대상이 서버 허용 거리 밖입니다.", out errorCode, out errorMessage);
            
            if (State.Terrain.Cells.TryGetValue(request.TargetCell, out TerrainCellRoomState cell) == false)
                return Fail("terrain.empty_cell", "대상 셀에 지형이 없습니다.", out errorCode, out errorMessage);

            ServerTerrainTileType tileType = (ServerTerrainTileType)cell.TileTypeID;
            ServerTerrainCatalog.TileDefinition tileDefinition = terrainCatalog.GetTile(tileType);
            
            if (tileDefinition.IsMineable == false)
                return Fail("terrain.not_mineable", "채굴할 수 없는 지형입니다.", out errorCode, out errorMessage);

            uint baseRevision = State.Terrain.Revision;
            int remaining = Math.Max(0, cell.Durability - pickaxe.DigPower);
            TerrainCellChangeDto change;
            List<WorldItemSpawnedMessage> spawned = new();
            
            if (remaining > 0)
            {
                cell.Durability = remaining;
                change = CreateCellChange(request.TargetCell, cell);
            }
            else
            {
                State.Terrain.Cells.Remove(request.TargetCell);
                change = new TerrainCellChangeDto
                {
                    Coord = request.TargetCell,
                    TileTypeID = (int)ServerTerrainTileType.Empty,
                    Durability = 0,
                    ResourceID = 0,
                    LootEntries = Array.Empty<TerrainLootEntryDto>(),
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
                    CollapseID = 0,
                    BaseRevision = baseRevision,
                    ResultRevision = State.Terrain.Revision,
                    Changes = new List<TerrainCellChangeDto> { change },
                },
            };
            spawnedMessages = spawned.ToArray();
            return true;
        }
    }

    /// <summary>
    /// 서버가 승인한 다이너마이트 폭발을 지형 변경으로 확정한다.
    /// 폭발 범위의 모든 변경은 Revision 하나와 terrain_batch 하나로 묶는다.
    /// </summary>
    public bool TryExplodeDynamiteTerrain(
        PendingDynamiteState projectile,
        float worldX,
        float worldY,
        string requestId,
        out TerrainChangeBatchMessage terrainMessage,
        out WorldItemSpawnedMessage[] spawnedMessages)
    {
        terrainMessage = null;
        spawnedMessages = Array.Empty<WorldItemSpawnedMessage>();

        if (projectile == null ||
            float.IsFinite(worldX) == false ||
            float.IsFinite(worldY) == false)
            return false;

        lock (stateGate)
        {
            List<GridCoord> targetCells = GetCellsInExplosionRadiusUnsafe(
                worldX,
                worldY,
                projectile.ExplosionRadius);
            List<TerrainCellChangeDto> changes = new();
            List<WorldItemSpawnedMessage> spawned = new();

            foreach (GridCoord coord in targetCells)
            {
                // 낙하 청크로 예약된 칸은 해당 청크의 배치 요청만 변경할 수 있다.
                if (State.Terrain.ReservedCollapseCells.Contains(coord))
                    continue;

                if (State.Terrain.Cells.TryGetValue(
                        coord,
                        out TerrainCellRoomState cell) == false)
                    continue;

                ServerTerrainTileType tileType =
                    (ServerTerrainTileType)cell.TileTypeID;
                ServerTerrainCatalog.TileDefinition tileDefinition =
                    terrainCatalog.GetTile(tileType);

                if (tileDefinition.IsMineable == false)
                    continue;

                int remaining = Math.Max(0, cell.Durability - projectile.ExplosionPower);

                if (remaining > 0)
                {
                    cell.Durability = remaining;
                    changes.Add(CreateCellChange(coord, cell));
                    continue;
                }

                State.Terrain.Cells.Remove(coord);
                changes.Add(new TerrainCellChangeDto
                {
                    Coord = coord,
                    TileTypeID = (int)ServerTerrainTileType.Empty,
                    Durability = 0,
                    ResourceID = 0,
                    LootEntries = Array.Empty<TerrainLootEntryDto>(),
                });
                CreateDropsForDestroyedCell(coord, cell, requestId, spawned);
            }

            if (changes.Count > 0)
            {
                uint baseRevision = State.Terrain.Revision;
                State.Terrain.Revision = baseRevision + 1;
                terrainMessage = new TerrainChangeBatchMessage
                {
                    RequestId = requestId,
                    Batch = new TerrainChangeBatchDto
                    {
                        MapSessionID = State.MapSession.Descriptor.MapSessionID,
                        CollapseID = 0,
                        BaseRevision = baseRevision,
                        ResultRevision = State.Terrain.Revision,
                        Changes = changes,
                    },
                };
            }

            spawnedMessages = spawned.ToArray();
            return true;
        }
    }

    /// <summary>
    /// 서버 좌표로 폭발 반경 안의 플레이어 체력을 확정한다.
    /// 지형과 달리 체력은 각 플레이어마다 별도 메시지로 전달한다.
    /// </summary>
    public void ApplyDynamiteExplosionDamage(
        PendingDynamiteState projectile,
        float worldX,
        float worldY,
        string requestId,
        out PlayerHealthChangedMessage[] healthChangedMessages,
        out PlayerDiedMessage[] diedMessages)
    {
        healthChangedMessages = Array.Empty<PlayerHealthChangedMessage>();
        diedMessages = Array.Empty<PlayerDiedMessage>();

        if (projectile == null || projectile.ExplosionPower <= 0)
            return;

        lock (stateGate)
        {
            float radiusSqr = projectile.ExplosionRadius * projectile.ExplosionRadius;
            List<PlayerHealthChangedMessage> changed = new();
            List<PlayerDiedMessage> died = new();

            foreach (PlayerRoomState player in State.Players.Values)
            {
                if (player.IsDead || IsInSpawnAreaUnsafe(player.X, player.Y))
                    continue;

                float deltaX = player.X - worldX;
                float deltaY = player.Y - worldY;
                if (deltaX * deltaX + deltaY * deltaY > radiusSqr)
                    continue;

                player.CurrentHealth = Math.Max(
                    0,
                    player.CurrentHealth - projectile.ExplosionPower);

                PlayerHealthStateDto state = CreatePlayerHealthState(player);
                changed.Add(new PlayerHealthChangedMessage
                {
                    RequestId = requestId,
                    Player = state,
                    DamageType = "Explosion",
                });

                if (player.CurrentHealth > 0)
                    continue;

                player.IsDead = true;
                player.DeathX = player.X;
                player.DeathY = player.Y;

                // 사망 상태를 바꾼 뒤의 값을 죽음 메시지에도 담는다.
                died.Add(new PlayerDiedMessage
                {
                    RequestId = requestId,
                    Player = CreatePlayerHealthState(player),
                });
            }

            healthChangedMessages = changed.ToArray();
            diedMessages = died.ToArray();
        }
    }

    // stateGate 안에서만 호출
    private List<GridCoord> GetCellsInExplosionRadiusUnsafe(
        float worldX,
        float worldY,
        float radius)
    {
        int centerX = (int)Math.Floor(
            (worldX - State.Terrain.OriginX) / State.Terrain.CellSize);
        int centerY = (int)Math.Floor(
            (worldY - State.Terrain.OriginY) / State.Terrain.CellSize);
        int cellRadius = (int)Math.Ceiling(
            radius / State.Terrain.CellSize);
        float radiusSqr = radius * radius;
        List<GridCoord> cells = new();

        for (int x = centerX - cellRadius; x <= centerX + cellRadius; x++)
        {
            for (int y = centerY - cellRadius; y <= centerY + cellRadius; y++)
            {
                if (x < 0 || x >= State.Terrain.MapWidth ||
                    y < 0 || y >= State.Terrain.MapHeight)
                    continue;

                float cellCenterX = State.Terrain.OriginX +
                                    (x + 0.5f) * State.Terrain.CellSize;
                float cellCenterY = State.Terrain.OriginY +
                                    (y + 0.5f) * State.Terrain.CellSize;
                float deltaX = cellCenterX - worldX;
                float deltaY = cellCenterY - worldY;

                if (deltaX * deltaX + deltaY * deltaY <= radiusSqr)
                    cells.Add(new GridCoord(x, y));
            }
        }

        return cells
            .OrderByDescending(cell => cell.Y)
            .ThenBy(cell => cell.X)
            .ToList();
    }

    private bool IsInSpawnAreaUnsafe(float worldX, float worldY)
    {
        if (State.Terrain.SpawnAreaWidth <= 0 ||
            State.Terrain.SpawnAreaHeight <= 0 ||
            State.Terrain.CellSize <= 0f)
            return false;

        int cellX = (int)Math.Floor(
            (worldX - State.Terrain.OriginX) / State.Terrain.CellSize);
        int cellY = (int)Math.Floor(
            (worldY - State.Terrain.OriginY) / State.Terrain.CellSize);

        return cellX >= State.Terrain.SpawnAreaOriginX &&
               cellX < State.Terrain.SpawnAreaOriginX + State.Terrain.SpawnAreaWidth &&
               cellY >= State.Terrain.SpawnAreaOriginY &&
               cellY < State.Terrain.SpawnAreaOriginY + State.Terrain.SpawnAreaHeight;
    }

    public bool TryStartCollapse(
        string playerId,
        TerrainCollapseStartRequest request,
        out TerrainCollapseStartedMessage started,
        out string errorCode,
        out string errorMessage)
    {
        started = null;
        errorCode = null;
        errorMessage = null;

        if (request?.IsValid() != true)
            return Fail("terrain.collapse_invalid", "유효하지 않은 낙하 지형 시작 요청입니다.", out errorCode, out errorMessage);

        // 서버 상태와 무관한 요청 좌표 중복 검사와 정렬
        HashSet<GridCoord> sourceCells = request.SourceCells.ToHashSet();
        if (sourceCells.Count != request.SourceCells.Count)
            return Fail("terrain.collapse_invalid", "낙하 지형 좌표가 중복되었습니다.", out errorCode, out errorMessage);

        List<GridCoord> orderedSourceCells = sourceCells.OrderBy(cell => cell).ToList();

        lock (stateGate)
        {
            foreach (GridCoord sourceCell in sourceCells)
            {
                if (State.Terrain.ReservedCollapseCells.Contains(sourceCell))
                    return Fail("terrain.collapse_pending", "이미 낙하 중인 지형입니다.", out errorCode, out errorMessage);
                if (State.Terrain.Cells.TryGetValue(sourceCell, out TerrainCellRoomState cell) == false)
                    return Fail("terrain.collapse_conflict", "낙하 지형 원본 셀이 없습니다.", out errorCode, out errorMessage);
                if (cell.TileTypeID == (int)ServerTerrainTileType.Bedrock)
                    return Fail("terrain.collapse_invalid", "기반암은 낙하할 수 없습니다.", out errorCode, out errorMessage);
            }

            long collapseID = Interlocked.Increment(ref lastCollapseID);
            PendingCollapseState pending = new()
            {
                CollapseID = collapseID,
                OwnerPlayerID = playerId,
                StartedRevision = State.Terrain.Revision,
                StartedAtMilliseconds = Environment.TickCount64,
                SourceCells = sourceCells,
            };
            State.Terrain.PendingCollapses.Add(collapseID, pending);
            State.Terrain.ReservedCollapseCells.UnionWith(sourceCells);

            started = new TerrainCollapseStartedMessage
            {
                RequestId = request.RequestId,
                CollapseID = collapseID,
                OwnerPlayerID = playerId,
                StartedRevision = pending.StartedRevision,
                SourceCells = orderedSourceCells,
            };
            return true;
        }
    }

    // 확정에 실패하면 cancelledCollapseID 에 그 낙하 번호가 담깁니다.
    // 호출한 쪽은 그 번호를 방 전체에 알려 주어야 합니다.
    public bool TryPlaceCollapse(
        string playerId,
        TerrainCollapsePlacementRequest request,
        out TerrainChangeBatchMessage terrainMessage,
        out TerrainCollapseCancelledMessage cancelledMessage,
        out string errorCode,
        out string errorMessage)
    {
        terrainMessage = null;
        cancelledMessage = null;
        errorCode = null;
        errorMessage = null;

        lock (stateGate)
        {
            if (request?.IsValid() != true)
            {
                return Fail(
                    "terrain.collapse_invalid",
                    "유효하지 않은 낙하 지형 배치 요청입니다.",
                    out errorCode,
                    out errorMessage);
            }


            if (State.Terrain.PendingCollapses.TryGetValue(
                    request.CollapseID,
                    out PendingCollapseState pending) == false)
                return Fail("terrain.collapse_not_found", "진행 중인 낙하 지형을 찾을 수 없습니다.", out errorCode, out errorMessage);
            if (pending.OwnerPlayerID != playerId)
                return Fail("terrain.collapse_not_owner", "낙하 지형 확정 권한이 없습니다.", out errorCode, out errorMessage);

            HashSet<GridCoord> sourceCells = request.SourceCells.ToHashSet();
            HashSet<GridCoord> targetCells = request.Changes
                .Select(change => change.Coord)
                .ToHashSet();

            if (sourceCells.Count != request.SourceCells.Count ||
                targetCells.Count != request.Changes.Count)
            {
                return FailAndRelease(
                    request.CollapseID,
                    pending,
                    "terrain.collapse_invalid",
                    "낙하 지형 좌표가 중복되었습니다.",
                    out cancelledMessage,
                    out errorCode,
                    out errorMessage);
            }

            if (pending.SourceCells.SetEquals(sourceCells) == false)
            {
                return FailAndRelease(
                    request.CollapseID,
                    pending,
                    "terrain.collapse_invalid",
                    "낙하 지형 원본 셀이 일치하지 않습니다.",
                    out cancelledMessage,
                    out errorCode,
                    out errorMessage);
            }

            // 서버에 원본 지형이 아직 존재하는지 확인한다.
            foreach (GridCoord sourceCell in sourceCells)
            {
                if (!State.Terrain.Cells.TryGetValue(
                        sourceCell,
                        out TerrainCellRoomState sourceState))
                {
                    return FailAndRelease(
                        request.CollapseID,
                        pending,
                        "terrain.collapse_conflict",
                        $"낙하 지형 원본 셀이 없습니다: ({sourceCell.X}, {sourceCell.Y})",
                        out cancelledMessage,
                        out errorCode,
                        out errorMessage);
                }

                if (sourceState.TileTypeID ==
                    (int)ServerTerrainTileType.Bedrock)
                {
                    return FailAndRelease(
                        request.CollapseID,
                        pending,
                        "terrain.collapse_invalid",
                        "기반암은 낙하 지형으로 이동할 수 없습니다.",
                        out cancelledMessage,
                        out errorCode,
                        out errorMessage);
                }
            }

            // 최종 좌표가 맵 내부에 있고 기존 고정 지형과 겹치지 않는지 확인한다.
            // 원본 영역과 겹치는 것은 제자리 또는 부분 이동일 수 있으므로 허용한다.
            foreach (TerrainCellChangeDto change in request.Changes)
            {
                bool isOutsideMap =
                    change.Coord.X < 0 ||
                    change.Coord.X >= State.Terrain.MapWidth ||
                    change.Coord.Y < 0 ||
                    change.Coord.Y >= State.Terrain.MapHeight;

                if (isOutsideMap)
                {
                    return FailAndRelease(
                        request.CollapseID,
                        pending,
                        "terrain.collapse_out_of_bounds",
                        $"낙하 지형 배치 좌표가 맵 밖입니다: " +
                        $"({change.Coord.X}, {change.Coord.Y})",
                        out cancelledMessage,
                        out errorCode,
                        out errorMessage);
                }

                if (change.TileTypeID ==
                        (int)ServerTerrainTileType.Empty ||
                    change.TileTypeID ==
                        (int)ServerTerrainTileType.Bedrock)
                {
                    return FailAndRelease(
                        request.CollapseID,
                        pending,
                        "terrain.collapse_invalid",
                        "낙하 지형에 허용되지 않는 타일이 포함되어 있습니다.",
                        out cancelledMessage,
                        out errorCode,
                        out errorMessage);
                }

                bool overlapsStaticTerrain =
                    !sourceCells.Contains(change.Coord) &&
                    State.Terrain.Cells.ContainsKey(change.Coord);

                if (overlapsStaticTerrain)
                {
                    return FailAndRelease(
                        request.CollapseID,
                        pending,
                        "terrain.collapse_conflict",
                        $"낙하 지형이 기존 지형과 겹칩니다: " +
                        $"({change.Coord.X}, {change.Coord.Y})",
                        out cancelledMessage,
                        out errorCode,
                        out errorMessage);
                }
            }

            uint baseRevision = State.Terrain.Revision;

            // 같은 좌표가 원본 제거와 최종 배치에 모두 포함될 수 있으므로
            // 좌표별 Dictionary로 최종 Batch를 구성한다.
            Dictionary<GridCoord, TerrainCellChangeDto> finalChanges = new();

            // 서버의 원본 static 지형을 제거한다.
            foreach (GridCoord sourceCell in sourceCells)
            {
                State.Terrain.Cells.Remove(sourceCell);

                finalChanges[sourceCell] = new TerrainCellChangeDto
                {
                    Coord = sourceCell,
                    TileTypeID = (int)ServerTerrainTileType.Empty,
                    Durability = 0,
                    ResourceID = 0,
                    LootEntries = Array.Empty<TerrainLootEntryDto>(),
                };
            }

            // 클라이언트에서 계산한 낙하 완료 위치에 static 지형을 배치한다.
            foreach (TerrainCellChangeDto change in request.Changes)
            {
                TerrainCellRoomState placedCell = new()
                {
                    TileTypeID = change.TileTypeID,
                    Durability = change.Durability,
                    ResourceID = change.ResourceID,
                    LootEntries =
                        change.LootEntries ?? Array.Empty<TerrainLootEntryDto>(),
                };

                State.Terrain.Cells[change.Coord] = placedCell;
                finalChanges[change.Coord] =
                    CreateCellChange(change.Coord, placedCell);
            }

            State.Terrain.Revision = baseRevision + 1;
            State.Terrain.PendingCollapses.Remove(request.CollapseID);
            State.Terrain.ReservedCollapseCells.ExceptWith(pending.SourceCells);

            terrainMessage = new TerrainChangeBatchMessage
            {
                RequestId = request.RequestId,
                Batch = new TerrainChangeBatchDto
                {
                    MapSessionID =
                        State.MapSession.Descriptor.MapSessionID,
                    CollapseID = request.CollapseID,
                    BaseRevision = baseRevision,
                    ResultRevision = State.Terrain.Revision,
                    Changes = finalChanges.Values
                        .OrderBy(change => change.Coord.Y)
                        .ThenBy(change => change.Coord.X)
                        .ToList(),
                },
            };

            return true;
        }
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
            LootEntries = cell.LootEntries ?? Array.Empty<TerrainLootEntryDto>(),
        };
    }

    // 클라이언트가 떨어지는 덩어리를 들고 있는 시간입니다.
    // FallingChunk.maximumLifetime 이 15초이므로 그보다 넉넉히 잡습니다.
    // 짧게 잡으면 아직 정상으로 떨어지는 중인 낙하를 서버가 무릅니다.
    private const long CollapseTimeoutMilliseconds = 30_000;

    // 확정도 취소도 오지 않은 낙하 예약을 시간으로 거둡니다. 없으면 null 입니다.
    //
    // 왜 시간으로 거두는가
    // : 여기 걸리는 경우는 하나같이 서버에 아무것도 오지 않습니다.
    //  굴착 한 번이 낙하를 둘 만들면 클라이언트가 두 번째 확정 요청을 스스로 버리고,
    //  떨어지던 덩어리가 수명을 넘겨 만료돼도 아무 요청을 보내지 않습니다.
    //  클라이언트가 얼어붙거나 소켓이 끊기며 메시지가 사라져도 마찬가지입니다.
    //
    //  올 것이 없는 고장은 받아서 고칠 수 없습니다.
    //  그래서 서버가 시간이 지난 것을 스스로 알아채는 이 길이 필요합니다.
    //
    // 거두지 않으면 그 칸은 방이 사라질 때까지 예약된 채로 남고,
    // 클라이언트는 낙하가 시작될 때 이미 지웠으므로 그 지형이 모두의 화면에서 사라집니다.
    public TerrainCollapseCancelledMessage SweepExpiredCollapses()
    {
        lock (stateGate)
        {
            if (State.Terrain.PendingCollapses.Count == 0) return null;

            long now = Environment.TickCount64;
            List<long> expired = null;

            foreach (KeyValuePair<long, PendingCollapseState> pair in
                     State.Terrain.PendingCollapses)
            {
                if (now - pair.Value.StartedAtMilliseconds < CollapseTimeoutMilliseconds)
                    continue;

                expired ??= new List<long>();
                expired.Add(pair.Key);
            }

            if (expired == null) return null;

            expired.Sort();

            List<GridCoord> sourceCells = new();
            foreach (long collapseID in expired)
            {
                PendingCollapseState pending = State.Terrain.PendingCollapses[collapseID];
                State.Terrain.PendingCollapses.Remove(collapseID);
                State.Terrain.ReservedCollapseCells.ExceptWith(pending.SourceCells);
                sourceCells.AddRange(pending.SourceCells);

                Console.WriteLine(
                    $"낙하 {collapseID} 를 시간 초과로 거뒀습니다. " +
                    $"({pending.SourceCells.Count}칸, 주인 {pending.OwnerPlayerID})");
            }

            return CreateCollapseCancelledMessageUnsafe(expired, sourceCells);
        }
    }

    // 확정에 실패한 낙하의 예약을 풀고 실패를 돌려줍니다.
    //
    // 실패할 때도 풀어야 하는 이유
    // : 클라이언트는 낙하를 시작하는 순간 이미 자기 화면에서 원본 칸을 지웁니다.
    //  거절을 받아도 다시 시도하지 않고, 떨어지던 덩어리도 이미 지운 뒤입니다.
    //  그래서 예약을 남겨 두면 아무도 확정할 수 없는 칸이 방이 사라질 때까지 남고,
    //  그 자리에 나중에 떨어지는 지형까지 terrain.collapse_conflict 로 거절되어
    //  같은 일이 옆으로 번집니다.
    //
    // 소유자 확인을 통과한 뒤에만 부릅니다.
    // 남의 낙하 번호를 적어 보낸 요청이 남의 예약을 푸는 일이 없어야 합니다.
    private bool FailAndRelease(
        long collapseID,
        PendingCollapseState pending,
        string code,
        string message,
        out TerrainCollapseCancelledMessage cancelledMessage,
        out string errorCode,
        out string errorMessage)
    {
        State.Terrain.PendingCollapses.Remove(collapseID);
        State.Terrain.ReservedCollapseCells.ExceptWith(pending.SourceCells);

        // 되돌리라고 알려 주어야 각자 화면에서 지웠던 칸이 살아납니다.
        // 나간 사람을 치울 때(RemovePlayer)와 같은 이유입니다.
        cancelledMessage = CreateCollapseCancelledMessageUnsafe(
            new List<long> { collapseID },
            pending.SourceCells);

        return Fail(code, message, out errorCode, out errorMessage);
    }

    // 예약을 풀 때 방 전체에 보낼 취소 메시지를 만듭니다.
    //
    // 원본 칸은 서버에서 지워진 적이 없습니다. 낙하 시작은 예약만 했고,
    // 지우는 것은 확정이 성공할 때뿐입니다.
    // 그래서 지금 서버에 있는 그대로를 담아 보내면 각자의 화면이 서버와 다시 같아집니다.
    //
    // stateGate 를 이미 쥔 채로 부릅니다.
    private TerrainCollapseCancelledMessage CreateCollapseCancelledMessageUnsafe(
        List<long> collapseIDs,
        IEnumerable<GridCoord> sourceCells)
    {
        List<TerrainCellChangeDto> restoreCells = new();
        foreach (GridCoord coord in sourceCells)
        {
            if (State.Terrain.Cells.TryGetValue(coord, out TerrainCellRoomState cell))
                restoreCells.Add(CreateCellChange(coord, cell));
        }

        return new TerrainCollapseCancelledMessage
        {
            CollapseIDs = collapseIDs,
            RestoreCells = restoreCells,
        };
    }
}
