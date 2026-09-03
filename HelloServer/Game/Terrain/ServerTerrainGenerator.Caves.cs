namespace HelloServer;

public sealed partial class ServerTerrainGenerator
{
    private static void ApplyCaves(MapGrid map, IReadOnlyList<ServerTerrainCatalog.CaveDefinition> rules, Random random)
    {
        foreach (ServerTerrainCatalog.CaveDefinition rule in rules)
        {
            for (int i = 0; i < rule.CaveCount; i++)
                TryCreateCave(map, rule, random);
        }
            
    }

    private static void TryCreateCave(MapGrid map, ServerTerrainCatalog.CaveDefinition rule, Random random)
    {
        for (int i = 0; i < rule.MaxAttempts; i++)
        {
            if (TryFindStart(map, rule, random, out GridCoord start) == false) return;

            HashSet<GridCoord> candidateCells = BuildCandidate(map, start, rule, random);

            if (candidateCells.Count == 0) continue;
            if (TryApplyCandidate(map, candidateCells)) return;
        }
    }

    private static bool TryFindStart(MapGrid map, ServerTerrainCatalog.CaveDefinition rule, 
        Random random, out GridCoord start)
    {
        List<GridCoord> candidates = new();

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                GridCoord coord = new(x, y);
                ServerTerrainTileType type = map.GetTile(coord);

                if (type is ServerTerrainTileType.Empty or ServerTerrainTileType.Bedrock)
                    continue;

                float depthRatio = GetDepthRatio(map, y);

                if (depthRatio < rule.MinDepthRatio || depthRatio > rule.MaxDepthRatio) continue;
                if (IsNearBoundary(map, coord, rule.MaxRadius + 1)) continue;

                candidates.Add(coord);
            }
        }

        if (candidates.Count == 0)
        {
            start = default;
            return false;
        }

        start = candidates[random.Next(candidates.Count)];
        return true;
    }

    private static HashSet<GridCoord> BuildCandidate(MapGrid map, GridCoord start,
        ServerTerrainCatalog.CaveDefinition rule, Random random)
    {
        HashSet<GridCoord> candidateCells = new();
        Vec2 position = new(start.X, start.Y);
        float horizontalDirection = random.Next(2) == 0 ? -1f : 1f;
        Vec2 direction = new(horizontalDirection, 0f);
        int length = random.Next(rule.MinLength, rule.MaxLength + 1);

        for (int i = 0; i < length; i++)
        {
            int radius = random.Next(rule.MinRadius, rule.MaxRadius + 1);

            CollectCircleCells(map, position, radius, candidateCells);

            float verticalChange = (float)(random.NextDouble() * 0.5 - 0.25);
            direction = new Vec2(horizontalDirection, Math.Clamp(direction.Y + verticalChange, -0.5f, 0.5f)).Normalized();
            position += direction;
        }

        return candidateCells;
    }

    private static void CollectCircleCells(MapGrid map, Vec2 position, int radius, HashSet<GridCoord> candidateCells)
    {
        GridCoord center = new(RoundToInt(position.X), RoundToInt(position.Y));

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                if (x * x + y * y > radius * radius) continue;

                GridCoord coord = new(center.X + x, center.Y + y);
                if (map.IsInside(coord) == false) continue;

                ServerTerrainTileType type = map.GetTile(coord);

                if (type is ServerTerrainTileType.Empty or ServerTerrainTileType.Bedrock) continue;
                if (IsNearBoundary(map, coord, 1)) continue;

                candidateCells.Add(coord);
            }
        }
        
    }

    private static bool IsNearBoundary(MapGrid map, GridCoord center, int margin)
    {
        for (int y = -margin; y <= margin; y++)
        {
            for (int x = -margin; x <= margin; x++)
            {
                GridCoord coord = new(center.X + x, center.Y + y);

                if (map.IsInside(coord) == false) return true;
                if (map.GetTile(coord) == ServerTerrainTileType.Bedrock) return true;
            }
        }

        return false;
    }

    private static bool TryApplyCandidate(MapGrid map, HashSet<GridCoord> candidateCells)
    {
        List<(GridCoord Coord, ServerTerrainTileType Type)> backups = new(candidateCells.Count);

        foreach (GridCoord coord in candidateCells)
        {
            backups.Add((coord, map.GetTile(coord)));
            map.SetTile(coord, ServerTerrainTileType.Empty);
        }

        if (HasFloatingGroup(map) == false) return true;

        foreach ((GridCoord coord, ServerTerrainTileType type) in backups)
            map.SetTile(coord, type);

        return false;
    }
}
