namespace HelloServer;

// Room의 연결 수명과 분리된 서버 권위형 경기 상태 접근 지점입니다.
public sealed partial class GameSession
{
    private const float MaximumPickupDistance = 3f;
    private const float MaximumDropDistance = 3f;
    private const float MaximumReportedDamageDistance = 3f;
    private const int MaximumReportedDamage = 500;
    private const long MinimumDamageRequestIntervalMilliseconds = 100;
    private const long ManualDropPickupDelayMilliseconds = 2_000;
    private const float MaximumDynamiteStartDistance = 1.5f;
    private const int DebugGoldItemID = 100;
    private const int DebugDynamiteItemID = 1;
    private const int DebugItemQuantity = 1_000;

    // 지형과 아이템 표는 방마다 새로 읽을 이유가 없습니다.
    // 한 번만 읽고 모든 방이 나눠 씁니다. 읽은 뒤로는 아무도 바꾸지 않습니다.
    //
    // 방마다 읽으면 파일 7개를 열고 표 전체를 다시 만드는 데 2ms 남짓 듭니다.
    // 그 시간이 방을 만드는 자물쇠 안에서 흘렀습니다.
    private static readonly ServerTerrainCatalog terrainCatalog =
        new(Path.Combine(AppContext.BaseDirectory, "Data", "Terrain"));

    private static readonly ServerItemCatalog itemCatalog =
        new(Path.Combine(AppContext.BaseDirectory, "Data", "Item", "Items.tsv"));

    private static readonly ServerPlayerConfig playerConfig =
        ServerPlayerConfig.Load(Path.Combine(
            AppContext.BaseDirectory, "Data", "Player", "playerconfig.json"));

    private readonly object stateGate = new();
    private long lastDropID;
    private long lastCollapseID;
    private long lastDynamiteProjectileID;
    private readonly Dictionary<string, long> lastDamageRequestAtMilliseconds = new();

    public RoomState State { get; } = new();

    public GameSession(string roomCode)
    {
        int seed = Random.Shared.Next(1, int.MaxValue);
        ServerGeneratedTerrain generated =
            new ServerTerrainGenerator(terrainCatalog).Generate(roomCode, "Default", seed);
        SetGeneratedTerrain(generated);
    }

    public void AddPlayer(User user, bool debugMode)
    {
        State.Players[user.Id] = new PlayerRoomState
        {
            Id = user.Id,
            CurrentHealth = playerConfig.InitialHealth,
            MaxHealth = playerConfig.MaxHealth,
            IsDebugMode = debugMode,
        };
        lock (stateGate)
        {
            PlayerInventoryRoomState inventory = new();
            if (debugMode)
            {
                inventory.Quantities[DebugGoldItemID] = DebugItemQuantity;
                inventory.Quantities[DebugDynamiteItemID] = DebugItemQuantity;
            }

            State.Inventory.Players.TryAdd(user.Id, inventory);
        }
    }

    public PlayerHealthSnapshotMessage CreatePlayerHealthSnapshotMessage()
    {
        lock (stateGate)
        {
            return new PlayerHealthSnapshotMessage
            {
                Players = State.Players.Values
                    .OrderBy(player => player.Id, StringComparer.Ordinal)
                    .Select(CreatePlayerHealthState)
                    .ToArray(),
            };
        }
    }

    private static PlayerHealthStateDto CreatePlayerHealthState(PlayerRoomState player)
    {
        return new PlayerHealthStateDto
        {
            PlayerID = player.Id,
            CurrentHealth = player.CurrentHealth,
            MaxHealth = player.MaxHealth,
            IsDead = player.IsDead,
        };
    }

    private static bool IsClientReportedDamageType(string damageType)
    {
        return damageType is "Fall" or "FallingChunk" or "Burial" or "Debug";
    }

    // stateGate 안에서만 호출한다.
    private bool TryApplyPlayerDamageUnsafe(
        PlayerRoomState player,
        int amount,
        string damageType,
        string requestId,
        out PlayerHealthChangedMessage changedMessage,
        out PlayerDiedMessage diedMessage)
    {
        changedMessage = null;
        diedMessage = null;
        if (player.IsDead ||
            (damageType != "Debug" && IsInSpawnAreaUnsafe(player.X, player.Y)))
            return false;

        player.CurrentHealth = Math.Max(0, player.CurrentHealth - amount);
        changedMessage = new PlayerHealthChangedMessage
        {
            RequestId = requestId,
            Player = CreatePlayerHealthState(player),
            DamageType = damageType,
        };

        if (player.CurrentHealth > 0)
            return true;

        player.IsDead = true;
        player.DeathX = player.X;
        player.DeathY = player.Y;
        diedMessage = new PlayerDiedMessage
        {
            RequestId = requestId,
            Player = CreatePlayerHealthState(player),
        };
        return true;
    }

    // 나간 사람의 상태를 지우고, 그 사람이 잡아 둔 낙하 예약도 함께 풉니다.
    //
    // 취소 메시지를 돌려주는 이유
    // : 다른 사람 화면에서는 그 칸이 이미 지워져 있습니다.
    //  낙하가 시작될 때 각자 지웠고, 되살리는 것은 서버의 확정뿐인데
    //  확정할 사람이 나가 버렸기 때문입니다.
    //  알려 주지 않으면 그 지형은 모두의 화면에서 사라진 채로 남습니다.
    //
    // 풀 것이 없으면 null 을 돌려줍니다.
    public TerrainCollapseCancelledMessage RemovePlayer(string playerId)
    {
        lock (stateGate)
        {
            State.Players.TryRemove(playerId, out _);
            State.Inventory.Players.Remove(playerId);
            lastDamageRequestAtMilliseconds.Remove(playerId);

            List<long> owned = State.Terrain.PendingCollapses
                .Where(pair => pair.Value.OwnerPlayerID == playerId)
                .Select(pair => pair.Key)
                .OrderBy(value => value)
                .ToList();
            if (owned.Count == 0) return null;

            List<GridCoord> sourceCells = new();
            foreach (long collapseID in owned)
            {
                PendingCollapseState pending = State.Terrain.PendingCollapses[collapseID];
                State.Terrain.PendingCollapses.Remove(collapseID);
                State.Terrain.ReservedCollapseCells.ExceptWith(pending.SourceCells);
                sourceCells.AddRange(pending.SourceCells);
            }

            return CreateCollapseCancelledMessageUnsafe(owned, sourceCells);
        }
    }

    public void MovePlayer(string playerId, float x, float y)
    {
        if (float.IsFinite(x) == false || float.IsFinite(y) == false) return;
        if (State.Players.TryGetValue(playerId, out PlayerRoomState player) == false)
            return;

        player.X = x;
        player.Y = y;
    }

    public bool TryApplyPlayerDamage(
        string playerId,
        PlayerDamageRequest request,
        out PlayerHealthChangedMessage changedMessage,
        out PlayerDiedMessage diedMessage,
        out string errorCode,
        out string errorMessage)
    {
        changedMessage = null;
        diedMessage = null;
        errorCode = null;
        errorMessage = null;

        if (request?.IsValid() != true ||
            IsClientReportedDamageType(request?.DamageType) == false ||
            (request.DamageType != "Debug" && request.Amount > MaximumReportedDamage))
            return Fail("player.invalid_damage", "유효하지 않은 피해 요청입니다.", out errorCode, out errorMessage);

        lock (stateGate)
        {
            if (State.Players.TryGetValue(playerId, out PlayerRoomState player) == false)
                return Fail("player.not_found", "플레이어 상태를 찾을 수 없습니다.", out errorCode, out errorMessage);
            if (request.DamageType == "Debug" && player.IsDebugMode == false)
                return Fail("player.debug_not_enabled", "개발 피해는 DebugMode에서만 사용할 수 있습니다.", out errorCode, out errorMessage);

            long now = Environment.TickCount64;
            if (lastDamageRequestAtMilliseconds.TryGetValue(playerId, out long previous) &&
                now - previous < MinimumDamageRequestIntervalMilliseconds)
                return Fail("player.damage_rate_limited", "피해 요청이 너무 빠릅니다.", out errorCode, out errorMessage);
            lastDamageRequestAtMilliseconds[playerId] = now;

            float deltaX = player.X - request.X;
            float deltaY = player.Y - request.Y;
            if (deltaX * deltaX + deltaY * deltaY >
                MaximumReportedDamageDistance * MaximumReportedDamageDistance)
                return Fail("player.invalid_damage_position", "피해 위치가 플레이어와 너무 멉니다.", out errorCode, out errorMessage);

            if (TryApplyPlayerDamageUnsafe(
                    player,
                    request.DamageType == "Debug" ? player.CurrentHealth : request.Amount,
                    request.DamageType,
                    request.RequestId,
                    out changedMessage,
                    out diedMessage) == false)
                return Fail("player.damage_ignored", "현재 플레이어는 피해를 받을 수 없습니다.", out errorCode, out errorMessage);

            return true;
        }
    }

    public bool TryRespawnPlayer(
        string playerId,
        PlayerRespawnRequest request,
        out PlayerRespawnedMessage respawnedMessage,
        out string errorCode,
        out string errorMessage)
    {
        respawnedMessage = null;
        errorCode = null;
        errorMessage = null;
        if (request?.IsValid() != true)
            return Fail("player.invalid_respawn", "유효하지 않은 리스폰 요청입니다.", out errorCode, out errorMessage);

        lock (stateGate)
        {
            if (State.Players.TryGetValue(playerId, out PlayerRoomState player) == false)
                return Fail("player.not_found", "플레이어 상태를 찾을 수 없습니다.", out errorCode, out errorMessage);
            if (player.IsDead == false)
                return Fail("player.not_dead", "사망 상태에서만 리스폰할 수 있습니다.", out errorCode, out errorMessage);

            (float spawnX, float spawnY) = GetSpawnPositionUnsafe();
            player.X = spawnX;
            player.Y = spawnY;
            player.CurrentHealth = player.MaxHealth;
            player.IsDead = false;
            respawnedMessage = new PlayerRespawnedMessage
            {
                RequestId = request.RequestId,
                Player = CreatePlayerHealthState(player),
                X = spawnX,
                Y = spawnY,
            };
            return true;
        }
    }

    // stateGate 안에서만 호출한다.
    private (float X, float Y) GetSpawnPositionUnsafe()
    {
        float cellSize = State.Terrain.CellSize;
        float cellX = State.Terrain.SpawnAreaOriginX +
                      State.Terrain.SpawnAreaWidth * 0.5f;
        float cellY = State.Terrain.SpawnAreaOriginY + 0.5f;
        return (
            State.Terrain.OriginX + cellX * cellSize,
            State.Terrain.OriginY + cellY * cellSize);
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
            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < drop.PickupAvailableAtUnixMilliseconds)
                return Fail("item.pickup_cooldown", "버린 아이템은 잠시 뒤에 획득할 수 있습니다.", out errorCode, out errorMessage);

            if (!float.IsFinite(request.X) || !float.IsFinite(request.Y))
                return Fail("item.invalid_position", "아이템 좌표가 유효하지 않습니다.", out errorCode, out errorMessage);

            float mapMinX = State.Terrain.OriginX;
            float mapMinY = State.Terrain.OriginY;
            float mapMaxX = mapMinX + State.Terrain.MapWidth * State.Terrain.CellSize;
            float mapMaxY = mapMinY + State.Terrain.MapHeight * State.Terrain.CellSize;
            if (request.X < mapMinX || request.X > mapMaxX ||
                request.Y < mapMinY || request.Y > mapMaxY)
                return Fail("item.invalid_position", "아이템 좌표가 맵 범위 밖입니다.", out errorCode, out errorMessage);

            // 현재 단계에서는 클라이언트가 Rigidbody/FallingChunk 물리로 계산한 좌표를 사용한다.
            float dx = player.X - request.X;
            float dy = player.Y - request.Y;
            if (dx * dx + dy * dy > MaximumPickupDistance * MaximumPickupDistance)
                return Fail("item.out_of_range", "아이템이 서버 허용 거리 밖입니다.", out errorCode, out errorMessage);
            if (State.Inventory.Players.TryGetValue(playerId, out PlayerInventoryRoomState inventory) == false)
                return Fail("inventory.not_found", "플레이어 인벤토리를 찾을 수 없습니다.", out errorCode, out errorMessage);

            int previous = inventory.Quantities.GetValueOrDefault(drop.ItemID);
            if (previous > int.MaxValue - drop.Quantity)
                return Fail("inventory.overflow", "아이템 수량 한도를 초과했습니다.", out errorCode, out errorMessage);
            if (CanAddInventoryWeight(inventory, drop.ItemID, drop.Quantity) == false)
                return Fail("inventory.overweight", "인벤토리 무게 한도를 초과했습니다.", out errorCode, out errorMessage);

            inventory.Quantities[drop.ItemID] = previous + drop.Quantity;
            drop.X = request.X;
            drop.Y = request.Y;
            State.WorldItems.Drops.Remove(drop.DropID);
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

    public bool TryDropWorldItem(
        string playerId,
        WorldItemDropRequest request,
        out WorldItemSpawnedMessage spawnedMessage,
        out InventorySnapshotMessage inventoryMessage,
        out string errorCode,
        out string errorMessage)
    {
        spawnedMessage = null;
        inventoryMessage = null;
        errorCode = null;
        errorMessage = null;

        lock (stateGate)
        {
            if (request?.IsValid() != true)
                return Fail("item.invalid_request", "유효하지 않은 아이템 버리기 요청입니다.", out errorCode, out errorMessage);
            if (State.Players.TryGetValue(playerId, out PlayerRoomState player) == false)
                return Fail("player.not_found", "플레이어 상태를 찾을 수 없습니다.", out errorCode, out errorMessage);
            if (player.IsDead)
                return Fail("player.dead", "사망 상태에서는 아이템을 버릴 수 없습니다.", out errorCode, out errorMessage);
            if (State.Inventory.Players.TryGetValue(playerId, out PlayerInventoryRoomState inventory) == false)
                return Fail("inventory.not_found", "플레이어 인벤토리를 찾을 수 없습니다.", out errorCode, out errorMessage);
            if (IsInsideMap(request.X, request.Y) == false)
                return Fail("item.invalid_position", "아이템을 버릴 위치가 맵 범위 밖입니다.", out errorCode, out errorMessage);

            float dx = player.X - request.X;
            float dy = player.Y - request.Y;
            if (dx * dx + dy * dy > MaximumDropDistance * MaximumDropDistance)
                return Fail("item.out_of_range", "아이템을 버릴 위치가 서버 허용 거리 밖입니다.", out errorCode, out errorMessage);

            int ownedQuantity = inventory.Quantities.GetValueOrDefault(request.ItemID);
            if (ownedQuantity < request.Quantity)
                return Fail("inventory.insufficient", "버릴 아이템 수량이 부족합니다.", out errorCode, out errorMessage);

            int remainingQuantity = ownedQuantity - request.Quantity;
            if (remainingQuantity == 0) inventory.Quantities.Remove(request.ItemID);
            else inventory.Quantities[request.ItemID] = remainingQuantity;

            spawnedMessage = CreateDrop(
                request.ItemID,
                request.Quantity,
                request.X,
                request.Y,
                request.RequestId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
                ManualDropPickupDelayMilliseconds);
            inventoryMessage = CreateInventorySnapshotUnsafe(playerId, request.RequestId);
            return true;
        }
    }

    public bool TryThrowDynamite(
        string playerID,
        DynamiteThrowRequest request,
        out DynamiteThrownMessage thrownMessage,
        out InventorySnapshotMessage inventoryMessage,
        out string errorCode,
        out string errorMessage)
    {
        thrownMessage = null;
        inventoryMessage = null;
        errorCode = null;
        errorMessage = null;

        lock (stateGate)
        {
            if (request?.IsValid() != true)
            {
                return Fail(
                    "dynamite.invalid_request",
                    "유효하지 않은 다이너마이트 사용 요청입니다.",
                    out errorCode,
                    out errorMessage);
            }

            if (State.Players.TryGetValue(playerID, out PlayerRoomState player) == false)
            {
                return Fail(
                    "player.not_found",
                    "플레이어 상태를 찾을 수 없습니다.",
                    out errorCode,
                    out errorMessage);
            }

            if (player.IsDead)
            {
                return Fail(
                    "player.dead",
                    "사망 상태에서는 다이너마이트를 사용할 수 없습니다.",
                    out errorCode,
                    out errorMessage);
            }

            if (ServerDynamiteCatalog.TryGet(
                    request.ItemID,
                    out ServerDynamiteCatalog.DynamiteDefinition dynamite) == false)
            {
                return Fail(
                    "dynamite.invalid_item",
                    "서버에 등록되지 않은 다이너마이트입니다.",
                    out errorCode,
                    out errorMessage);
            }

            if (State.Inventory.Players.TryGetValue(
                    playerID,
                    out PlayerInventoryRoomState inventory) == false)
            {
                return Fail(
                    "inventory.not_found",
                    "플레이어 인벤토리를 찾을 수 없습니다.",
                    out errorCode,
                    out errorMessage);
            }

            int ownedQuantity = inventory.Quantities.GetValueOrDefault(request.ItemID);
            if (ownedQuantity <= 0)
            {
                return Fail(
                    "inventory.insufficient",
                    "다이너마이트 수량이 부족합니다.",
                    out errorCode,
                    out errorMessage);
            }

            float startDeltaX = request.StartX - player.X;
            float startDeltaY = request.StartY - player.Y;

            if (startDeltaX * startDeltaX + startDeltaY * startDeltaY >
                MaximumDynamiteStartDistance * MaximumDynamiteStartDistance)
            {
                return Fail(
                    "dynamite.invalid_start",
                    "다이너마이트 시작 위치가 플레이어와 너무 멉니다.",
                    out errorCode,
                    out errorMessage);
            }

            float directionLengthSqr =
                request.DirectionX * request.DirectionX +
                request.DirectionY * request.DirectionY;

            if (directionLengthSqr < 0.0001f)
            {
                return Fail(
                    "dynamite.invalid_direction",
                    "다이너마이트 방향이 올바르지 않습니다.",
                    out errorCode,
                    out errorMessage);
            }

            float directionLength = MathF.Sqrt(directionLengthSqr);
            float directionX = request.DirectionX / directionLength;
            float directionY = request.DirectionY / directionLength;

            int remainingQuantity = ownedQuantity - 1;

            if (remainingQuantity == 0) inventory.Quantities.Remove(request.ItemID);
            else inventory.Quantities[request.ItemID] = remainingQuantity;

            string projectileID = $"dynamite-{Interlocked.Increment(ref lastDynamiteProjectileID)}";

            long startedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            PendingDynamiteState projectile = new()
            {
                ProjectileID = projectileID,
                OwnerPlayerID = playerID,
                ItemID = request.ItemID,
                StartX = request.StartX,
                StartY = request.StartY,
                DirectionX = directionX,
                DirectionY = directionY,
                StartedAtUnixMilliseconds = startedAtUnixMilliseconds,
                ThrowSpeed = dynamite.ThrowSpeed,
                FuseTime = dynamite.FuseTime,
                ExplosionRadius = dynamite.ExplosionRadius,
                ExplosionPower = dynamite.ExplosionPower,
            };

            State.Dynamites.Projectiles.Add(projectileID, projectile);

            thrownMessage = new DynamiteThrownMessage
            {
                RequestId = request.RequestId,
                ProjectileID = projectileID,
                OwnerPlayerID = playerID,
                ItemID = request.ItemID,
                StartX = projectile.StartX,
                StartY = projectile.StartY,
                DirectionX = projectile.DirectionX,
                DirectionY = projectile.DirectionY,
                StartedAtUnixMilliseconds = projectile.StartedAtUnixMilliseconds,
                ThrowSpeed = dynamite.ThrowSpeed,
                FuseTime = dynamite.FuseTime,
                ExplosionRadius = dynamite.ExplosionRadius,
            };

            inventoryMessage = CreateInventorySnapshotUnsafe(
                playerID,
                request.RequestId);

            return true;
        }
    }

    public bool TryAcceptDynamiteExplosion(
        string playerID,
        DynamiteExplodeRequest request,
        out PendingDynamiteState projectile,
        out string errorCode,
        out string errorMessage)
    {
        projectile = null;
        errorCode = null;
        errorMessage = null;

        lock (stateGate)
        {
            if (request?.IsValid() != true)
            {
                return Fail(
                    "dynamite.invalid_explode_request",
                    "유효하지 않은 다이너마이트 폭발 요청입니다.",
                    out errorCode,
                    out errorMessage);
            }

            if (State.Dynamites.Projectiles.TryGetValue(
                    request.ProjectileID,
                    out PendingDynamiteState pending) == false)
            {
                return Fail(
                    "dynamite.not_found",
                    "진행 중인 다이너마이트를 찾을 수 없습니다.",
                    out errorCode,
                    out errorMessage);
            }

            if (pending.OwnerPlayerID != playerID)
            {
                return Fail(
                    "dynamite.not_owner",
                    "다른 플레이어의 다이너마이트는 폭발시킬 수 없습니다.",
                    out errorCode,
                    out errorMessage);
            }

            long elapsedMilliseconds =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() -
                pending.StartedAtUnixMilliseconds;

            long minimumFuseMilliseconds =
                (long)(pending.FuseTime * 1000f) - 150;

            if (elapsedMilliseconds < minimumFuseMilliseconds)
            {
                return Fail(
                    "dynamite.fuse_pending",
                    "다이너마이트 퓨즈가 아직 끝나지 않았습니다.",
                    out errorCode,
                    out errorMessage);
            }

            float deltaX = request.X - pending.StartX;
            float deltaY = request.Y - pending.StartY;

            // 기존 물리의 튕김을 완전히 예측하지는 않지만 순간이동 수준의 비정상 좌표는 막음
            float maximumTravelDistance =
                pending.ThrowSpeed * pending.FuseTime * 2f;

            if (deltaX * deltaX + deltaY * deltaY >
                maximumTravelDistance * maximumTravelDistance)
            {
                return Fail(
                    "dynamite.invalid_position",
                    "다이너마이트 폭발 위치가 허용 범위를 벗어났습니다.",
                    out errorCode,
                    out errorMessage);
            }

            // 한 번 승인된 투사체는 다시 폭발 요청 불가
            State.Dynamites.Projectiles.Remove(request.ProjectileID);

            projectile = pending;
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
        State.Terrain.SpawnAreaOriginX = generated.Snapshot.SpawnAreaOriginX;
        State.Terrain.SpawnAreaOriginY = generated.Snapshot.SpawnAreaOriginY;
        State.Terrain.SpawnAreaWidth = generated.Snapshot.SpawnAreaWidth;
        State.Terrain.SpawnAreaHeight = generated.Snapshot.SpawnAreaHeight;
        foreach (TerrainCellChangeDto cell in generated.Snapshot.Cells)
        {
            State.Terrain.Cells[cell.Coord] = new TerrainCellRoomState
            {
                TileTypeID = cell.TileTypeID,
                Durability = cell.Durability,
                ResourceID = cell.ResourceID,
                LootEntries = cell.LootEntries ?? Array.Empty<TerrainLootEntryDto>(),
            };
        }
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
            CurrentWeight = GetInventoryWeight(inventory),
            MaxWeight = inventory?.MaxWeight ?? 0,
        };
    }

    private bool CanAddInventoryWeight(
        PlayerInventoryRoomState inventory,
        int itemID,
        int quantity)
    {
        if (quantity <= 0 || itemCatalog.TryGetItem(itemID, out ServerItemCatalog.ItemDefinition item) == false)
            return false;

        long total = (long)GetInventoryWeight(inventory) + (long)item.Weight * quantity;
        return total <= inventory.MaxWeight;
    }

    private int GetInventoryWeight(PlayerInventoryRoomState inventory)
    {
        if (inventory == null) return 0;

        long total = 0;
        foreach ((int itemID, int quantity) in inventory.Quantities)
        {
            if (quantity <= 0 || itemCatalog.TryGetItem(itemID, out ServerItemCatalog.ItemDefinition item) == false)
                continue;

            total += (long)item.Weight * quantity;
            if (total >= int.MaxValue) return int.MaxValue;
        }

        return (int)total;
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
        return CreateDrop(
            itemID,
            quantity,
            State.Terrain.OriginX + (coord.X + 0.5f) * State.Terrain.CellSize,
            State.Terrain.OriginY + (coord.Y + 0.5f) * State.Terrain.CellSize,
            requestId);
    }

    private WorldItemSpawnedMessage CreateDrop(
        int itemID,
        int quantity,
        float x,
        float y,
        string requestId,
        long pickupAvailableAtUnixMilliseconds = 0)
    {
        WorldItemDropDto drop = new()
        {
            DropID = $"d{Interlocked.Increment(ref lastDropID)}",
            ItemID = itemID,
            Quantity = quantity,
            X = x,
            Y = y,
            PickupAvailableAtUnixMilliseconds = pickupAvailableAtUnixMilliseconds,
        };
        State.WorldItems.Drops[drop.DropID] = drop;
        return new WorldItemSpawnedMessage
        {
            RequestId = requestId,
            Drop = CloneDrop(drop),
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
            PickupAvailableAtUnixMilliseconds = drop.PickupAvailableAtUnixMilliseconds,
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

    private bool IsInsideMap(float x, float y)
    {
        float maximumX = State.Terrain.OriginX + State.Terrain.MapWidth * State.Terrain.CellSize;
        float maximumY = State.Terrain.OriginY + State.Terrain.MapHeight * State.Terrain.CellSize;
        return x >= State.Terrain.OriginX && x <= maximumX &&
               y >= State.Terrain.OriginY && y <= maximumY;
    }
}
