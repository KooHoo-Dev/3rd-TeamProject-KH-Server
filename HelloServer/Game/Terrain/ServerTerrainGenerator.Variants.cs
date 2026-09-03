namespace HelloServer;

public sealed partial class ServerTerrainGenerator
{
    #region 변이 군집

    private static void ApplyVariants(
        MapGrid map,
        IReadOnlyList<ServerTerrainCatalog.DepthSectionDefinition> sections,
        IReadOnlyList<ServerTerrainCatalog.VariantDefinition> rules,
        Random random)
    {
        foreach (ServerTerrainCatalog.VariantDefinition rule in rules)
        {
            ServerTerrainCatalog.DepthSectionDefinition section =
                FindSectionById(sections, rule.SectionID);
            if (section == null) continue;

            for (int i = 0; i < rule.ClusterCount; i++)
                TryCreateCluster(map, rule, section, random);
        }
    }

    // 자리를 못 찾으면 크기를 다시 뽑아 스무 번까지 시도합니다.
    // 실패해도 난수를 두 번 쓴다는 점이 중요합니다. 클라이언트와 같아야 합니다.
    private static void TryCreateCluster(
        MapGrid map,
        ServerTerrainCatalog.VariantDefinition rule,
        ServerTerrainCatalog.DepthSectionDefinition section,
        Random random)
    {
        const int MAX_PLACEMENT_ATTEMPTS = 20;

        for (int i = 0; i < MAX_PLACEMENT_ATTEMPTS; i++)
        {
            int width = random.Next(rule.MinWidth, rule.MaxWidth + 1);
            int height = random.Next(rule.MinHeight, rule.MaxHeight + 1);

            if (TryFindCenter(map, section, width, height, random, out GridCoord center) == false)
                continue;

            FillVariantCluster(map, center, width, height, rule, section, random);
            return;
        }
    }

    private static bool TryFindCenter(MapGrid map, ServerTerrainCatalog.DepthSectionDefinition section,
        int width, int height, Random random, out GridCoord center)
    {
        List<GridCoord> candidates = new();

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                GridCoord coord = new(x, y);

                if (map.GetTile(coord) != section.BaseTileType) continue;

                float depthRatio = Clamp01(GetDepthRatio(map, y) + GetSectionBoundaryOffset(map, x));

                if (section.Contains(depthRatio) == false) continue;
                if (HasPlacementMargin(map, coord, width, height) == false) continue;

                candidates.Add(coord);
            }
        }

        if (candidates.Count == 0)
        {
            center = default;
            return false;
        }

        center = candidates[random.Next(candidates.Count)];
        return true;
    }

    private static void FillVariantCluster(MapGrid map, GridCoord center, int width, int height,
        ServerTerrainCatalog.VariantDefinition rule,
        ServerTerrainCatalog.DepthSectionDefinition section,
        Random random)
    {
        float radiusX = width * 0.5f;
        float radiusY = height * 0.5f;

        int minX = center.X - width / 2;
        int minY = center.Y - height / 2;

        for (int localY = 0; localY < height; localY++)
        {
            for (int localX = 0; localX < width; localX++)
            {
                GridCoord coord = new(minX + localX, minY + localY);

                if (map.IsInside(coord) == false) continue;
                if (map.GetTile(coord) != section.BaseTileType) continue;

                float depthRatio = Clamp01(
                    GetDepthRatio(map, coord.Y) + GetSectionBoundaryOffset(map, coord.X));

                if (section.Contains(depthRatio) == false) continue;

                float normalizedX = (localX + 0.5f - radiusX) / radiusX;
                float normalizedY = (localY + 0.5f - radiusY) / radiusY;

                if (normalizedX * normalizedX + normalizedY * normalizedY > 1f) continue;

                // 여기까지 통과한 칸에서만 난수를 뽑습니다. 순서가 곧 결과입니다.
                if (random.NextDouble() > rule.FillRatio) continue;

                map.SetTile(coord, rule.TileType);
            }
        }
    }

    private static bool HasPlacementMargin(MapGrid map, GridCoord center, int width, int height)
    {
        int minX = center.X - width / 2;
        int minY = center.Y - height / 2;

        return minX >= 0 && minX + width <= map.Width &&
               minY >= 0 && minY + height <= map.Height;
    }

    #endregion

    private static ServerTerrainCatalog.DepthSectionDefinition FindSectionById(
        IReadOnlyList<ServerTerrainCatalog.DepthSectionDefinition> sections, string sectionID)
    {
        foreach (ServerTerrainCatalog.DepthSectionDefinition section in sections)
            if (section.SectionID == sectionID) return section;

        return null;
    }
}
