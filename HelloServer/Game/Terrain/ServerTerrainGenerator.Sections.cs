namespace HelloServer;

public sealed partial class ServerTerrainGenerator
{
    private static float GetDepthRatio(MapGrid map, int y)
        => map.Height > 1 ? (float)(map.Height - 1 - y) / (map.Height - 1) : 0f;

    // 깊이 구간의 경계를 열마다 조금씩 흔듭니다. 직선으로 잘리지 않게 하려는 것입니다.
    //
    // 클라이언트는 예전에 Mathf.PerlinNoise 를 썼습니다.
    // 그것은 유니티 안에만 있는 함수라 서버가 같은 값을 낼 수 없었습니다.
    // 그래서 양쪽 다 이 정수 해시를 쓰도록 바꿨습니다. 더하기 곱하기뿐이라 어디서나 같습니다.
    private static float GetSectionBoundaryOffset(MapGrid map, int x)
    {
        const float MAX_OFFSET_IN_CELLS = 4f;

        uint hash = unchecked((uint)(map.Seed * 374761393 + x * 668265263));
        hash = (hash ^ (hash >> 13)) * 1274126177u;
        float noise = (hash & 0x00FFFFFF) / 16777215f;
        float centeredNoise = noise * Lerp(-1f, 1f, noise);

        return centeredNoise * MAX_OFFSET_IN_CELLS / Math.Max(1, map.Height - 1);
    }

    private static void FillSectionTerrain(MapGrid map,
        IReadOnlyList<ServerTerrainCatalog.DepthSectionDefinition> sections, EllipseShape shape)
    {
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                GridCoord coord = new(x, y);

                if (shape.Contains(coord) == false) continue;

                float depthRatio = GetDepthRatio(map, y);
                float boundaryOffset = GetSectionBoundaryOffset(map, x);

                ServerTerrainCatalog.DepthSectionDefinition section =
                    FindSection(sections, Clamp01(depthRatio + boundaryOffset));

                if (section != null) map.SetTile(coord, section.BaseTileType);
            }
        }
    }

    private static ServerTerrainCatalog.DepthSectionDefinition FindSection(
        IReadOnlyList<ServerTerrainCatalog.DepthSectionDefinition> sections,
        float depthRatio)
    {
        foreach (ServerTerrainCatalog.DepthSectionDefinition section in sections)
            if (section.Contains(depthRatio)) return section;

        return null;
    }

    private static void ApplyBoundaries(MapGrid map,
        ServerTerrainCatalog.ProfileDefinition profile, EllipseShape shape)
    {
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                GridCoord coord = new(x, y);
                if (shape.Contains(coord) == false) continue;
                if (shape.IsTopOpening(coord)) continue;

                if (IsBoundary(coord, profile, shape))
                    map.SetTile(coord, ServerTerrainTileType.Bedrock);
            }
        }
    }

    private static bool IsBoundary(GridCoord coord,
        ServerTerrainCatalog.ProfileDefinition profile, EllipseShape shape)
    {
        for (int distance = 1; distance <= profile.BoundaryThickness; distance++)
        {
            if (shape.Contains(new GridCoord(coord.X - distance, coord.Y)) == false ||
                shape.Contains(new GridCoord(coord.X + distance, coord.Y)) == false ||
                shape.Contains(new GridCoord(coord.X, coord.Y - distance)) == false)
                return true;

            GridCoord above = new(coord.X, coord.Y + distance);

            if (shape.Contains(above) == false &&
                (above.Y < profile.Height || shape.IsTopOpeningColumn(coord.X) == false))
                return true;
        }

        return false;
    }

    // 지형 맨 위에 스폰 안전 구역을 얹고, 플랫폼은 지하 최상단에 둡니다.
    private static void AttachRespawnArea(MapGrid map, ServerTerrainCatalog.ProfileDefinition profile)
    {
        int terrainHeight = map.Height;
        int areaMinX = (map.Width - profile.TopOpeningWidth) / 2;
        int areaMaxX = areaMinX + profile.TopOpeningWidth - 1;
        int areaMinY = terrainHeight;
        int areaMaxY = areaMinY + profile.RespawnAreaHeight - 1;

        map.Grow(profile.RespawnAreaHeight);

        for (int x = areaMinX; x <= areaMaxX; x++)
            map.SetTile(new GridCoord(x, areaMaxY), ServerTerrainTileType.Bedrock);

        for (int y = areaMinY; y < areaMaxY; y++)
        {
            map.SetTile(new GridCoord(areaMinX, y), ServerTerrainTileType.Bedrock);
            map.SetTile(new GridCoord(areaMaxX, y), ServerTerrainTileType.Bedrock);
        }

        int platformMinX = areaMinX + profile.BoundaryThickness;
        int platformMaxX = areaMaxX - profile.BoundaryThickness;
        int platformY = terrainHeight - 1;
        int emptyBelowPlatformY = platformY - 1;

        // 스폰 플랫폼 아래 한 행은 비워 플랫폼 위에서 아래 방향 점프로
        // 지하로 진입할 수 있게 한다.
        for (int x = platformMinX; x <= platformMaxX; x++)
        {
            GridCoord platformCell = new(x, platformY);
            map.SetTile(platformCell, ServerTerrainTileType.SpawnPlatform);
            map.SetResource(platformCell, 0);
            if (emptyBelowPlatformY >= 0)
                map.SetTile(new GridCoord(x, emptyBelowPlatformY), ServerTerrainTileType.Empty);
        }

    }
}
