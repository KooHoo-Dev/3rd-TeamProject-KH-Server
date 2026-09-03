namespace HelloServer;

public sealed partial class ServerTerrainGenerator
{
    private static readonly GridCoord[] CLUSTER_DIRECTIONS = [new(-1, 0), new(1, 0), new(0, -1), new(0, 1)];

    private void ApplyMinerals(MapGrid map, ServerTerrainCatalog.ProfileDefinition profile,
        IReadOnlyList<ServerTerrainCatalog.DepthSectionDefinition> sections, Random random)
    {
        foreach (ServerTerrainCatalog.DepthSectionDefinition section in sections)
        {
            foreach (ServerTerrainCatalog.MineralDefinition mineral in
                     catalog.GetMinerals(profile.ProfileID, section.SectionID))
                ApplyMineral(map, section, mineral, random);
        }
    }

    // 여기만 x 가 바깥이고 y 가 안쪽
    private static void ApplyMineral(MapGrid map, ServerTerrainCatalog.DepthSectionDefinition section,
        ServerTerrainCatalog.MineralDefinition mineral, Random random)
    {
        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                GridCoord coord = new(x, y);

                // 앞선 군집이 이미 차지한 칸은 난수를 쓰기 전에 건너뜁니다.
                if (IsBaseCellInSection(map, coord, section) == false) continue;
                if (random.NextDouble() >= mineral.ClusterDensity) continue;

                int clusterSize = random.Next(mineral.MinClusterSize, mineral.MaxClusterSize + 1);

                FillMineralCluster(map, coord, clusterSize, section, mineral, random);
            }
        }
    }

    private static void FillMineralCluster(MapGrid map, GridCoord start, int clusterSize, ServerTerrainCatalog.DepthSectionDefinition section, 
        ServerTerrainCatalog.MineralDefinition mineral, Random random)
    {
        List<GridCoord> members = new() { start };
        map.SetResource(start, mineral.ResourceID);

        while (members.Count < clusterSize)
        {
            List<GridCoord> candidates = CollectCandidates(map, members, section);
            if (candidates.Count == 0) break;

            GridCoord selected = candidates[random.Next(candidates.Count)];
            map.SetResource(selected, mineral.ResourceID);
            members.Add(selected);
        }
    }

    private static List<GridCoord> CollectCandidates(MapGrid map, IReadOnlyList<GridCoord> members,
        ServerTerrainCatalog.DepthSectionDefinition section)
    {
        List<GridCoord> candidates = new();
        HashSet<GridCoord> uniqueCandidates = new();

        foreach (GridCoord member in members)
        {
            foreach (GridCoord direction in CLUSTER_DIRECTIONS)
            {
                GridCoord candidate = new(member.X + direction.X, member.Y + direction.Y);

                // 한 번 본 칸은 조건을 못 넘겼어도 다시 보지 않습니다.
                if (uniqueCandidates.Add(candidate) == false) continue;

                if (IsBaseCellInSection(map, candidate, section)) candidates.Add(candidate);
            }
        }

        return candidates;
    }

    private static bool IsBaseCellInSection(MapGrid map, GridCoord coord,
        ServerTerrainCatalog.DepthSectionDefinition section)
    {
        if (map.IsInside(coord) == false) return false;

        float depthRatio = Clamp01(GetDepthRatio(map, coord.Y) + GetSectionBoundaryOffset(map, coord.X));
        if (section.Contains(depthRatio) == false) return false;
        if (map.GetResource(coord) > 0) return false;

        return map.GetTile(coord) == section.BaseTileType;
    }
}
