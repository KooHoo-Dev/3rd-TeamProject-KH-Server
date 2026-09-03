namespace HelloServer;

public sealed class ServerGeneratedTerrain
{
    public MapSessionDescriptor Session { get; init; }
    public TerrainSnapshotDto Snapshot { get; init; }
}

// 유니티 클라이언트의 지형 생성기를 그대로 옮긴 것입니다.
//
// 왜 옮겼나
// : 서버와 클라이언트가 같은 시드에서 글자 하나까지 같은 지형을 만들어야
//  서버가 지형 사진(482KB)을 보내지 않아도 됩니다. 보내는 것은 시드뿐입니다.
//
// 옮긴 원본 (클라이언트 리포의 Assets/02. Scripts/TerrainCollapse/Generation/)
// : TerrainTableSeedGenerator, TerrainCaveGenerator,
//  TerrainVariantClusterGenerator, TerrainMineralClusterGenerator,
//  TerrainMapConnectivityValidator
//
// 고칠 때 지켜야 할 것
// : 난수를 언제 몇 번 뽑는지가 결과를 정합니다.
//  순회 순서(y 바깥/x 안쪽 같은 것)와 조건을 보는 순서도 마찬가지입니다.
//  조건 하나를 앞뒤로 옮기기만 해도 그 뒤의 지형이 전부 달라집니다.
//  보기 좋게 다듬고 싶어도 그대로 두고, 고쳐야 한다면 양쪽을 함께 고치십시오.
//  같은지 확인하는 방법은 문서에 적어 두었습니다.
public sealed partial class ServerTerrainGenerator
{
    private readonly ServerTerrainCatalog catalog;

    public ServerTerrainGenerator(ServerTerrainCatalog catalog)
    {
        this.catalog = catalog;
    }

    public ServerGeneratedTerrain Generate(string roomCode, string profileID, int seed)
    {
        ServerTerrainCatalog.ProfileDefinition profile = catalog.GetProfile(profileID);
        IReadOnlyList<ServerTerrainCatalog.DepthSectionDefinition> sections =
            catalog.GetSections(profileID);

        // 모든 단계가 이 난수 하나를 나눠 씁니다. 클라이언트도 같습니다.
        Random random = new(seed);

        MapGrid map = CreateEmptyMap(profile, seed);
        EllipseShape shape = new(profile);

        FillSectionTerrain(map, sections, shape);
        ApplyBoundaries(map, profile, shape);
        ApplyCaves(map, catalog.GetCaves(profileID), random);
        ApplyVariants(map, sections, catalog.GetVariants(profileID), random);
        ApplyMinerals(map, profile, sections, random);
        AttachRespawnArea(map, profile);

        string mapSessionID = $"{roomCode}-{Guid.NewGuid():N}";
        return new ServerGeneratedTerrain
        {
            Session = new MapSessionDescriptor
            {
                MapSessionID = mapSessionID,
                ProfileID = profileID,
                Seed = seed,
                TerrainDataVersion = catalog.DataVersion,
            },
            Snapshot = CreateSnapshot(map, profile),
        };
    }

    #region 결과 만들기

    private TerrainSnapshotDto CreateSnapshot(
        MapGrid map,
        ServerTerrainCatalog.ProfileDefinition profile)
    {
        List<TerrainCellChangeDto> cells = new();

        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            GridCoord coord = new(x, y);
            ServerTerrainTileType type = map.GetTile(coord);

            if (type == ServerTerrainTileType.Empty) continue;

            int resourceID = map.GetResource(coord);

            cells.Add(new TerrainCellChangeDto
            {
                Coord = coord,
                TileTypeID = (int)type,
                Durability = GetMaxDurability(type, resourceID),
                ResourceID = resourceID,
                LootEntries = Array.Empty<TerrainLootEntryDto>(),
            });
        }

        return new TerrainSnapshotDto
        {
            Revision = 0,
            MapWidth = map.Width,
            MapHeight = map.Height,
            CellSize = profile.CellSize,
            OriginX = profile.OriginX,
            OriginY = profile.OriginY,
            SpawnAreaOriginX = profile.SpawnAreaOriginX,
            SpawnAreaOriginY = profile.SpawnAreaOriginY,
            SpawnAreaWidth = profile.TopOpeningWidth,
            SpawnAreaHeight = profile.RespawnAreaHeight,
            Cells = cells,
        };
    }

    // 자원이 붙은 칸은 자원의 내구도를 씁니다. 클라이언트 GetMaxDurability 와 같습니다.
    private int GetMaxDurability(ServerTerrainTileType type, int resourceID)
    {
        if (type == ServerTerrainTileType.Empty) return 0;

        if (resourceID > 0 &&
            catalog.TryGetResource(resourceID, out ServerTerrainCatalog.ResourceDefinition resource))
            return resource.MaxDurability;

        return catalog.GetTile(type).MaxDurability;
    }

    #endregion
}
