namespace HelloServer;

public sealed class ServerGeneratedTerrain
{
    public MapSessionDescriptor Session { get; init; }
    public TerrainSnapshotDto Snapshot { get; init; }
}

public sealed class ServerTerrainGenerator
{
    private const int RespawnAreaHeight = 7;
    private static readonly GridCoord[] Directions =
    {
        new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
    };

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
        Dictionary<GridCoord, TerrainCellChangeDto> cells = new();
        Random random = new(seed);

        FillSections(cells, profile, sections, seed);
        ApplyBoundaries(cells, profile);
        ApplyCaves(cells, profile, catalog.GetCaves(profileID), random);
        ApplyVariants(cells, sections, catalog.GetVariants(profileID), random);
        ApplyMinerals(cells, profile, sections, random);
        AttachRespawnArea(cells, profile);

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
            Snapshot = new TerrainSnapshotDto
            {
                MapSessionID = mapSessionID,
                Revision = 0,
                MapWidth = profile.Width,
                MapHeight = profile.Height,
                CellSize = profile.CellSize,
                OriginX = profile.OriginX,
                OriginY = profile.OriginY,
                Cells = cells.Values.OrderBy(value => value.Coord).ToList(),
            },
        };
    }

    private void FillSections(
        Dictionary<GridCoord, TerrainCellChangeDto> cells,
        ServerTerrainCatalog.ProfileDefinition profile,
        IReadOnlyList<ServerTerrainCatalog.DepthSectionDefinition> sections,
        int seed)
    {
        for (int y = 0; y < profile.Height; y++)
        {
            for (int x = 0; x < profile.Width; x++)
            {
                GridCoord coord = new(x, y);
                if (Contains(profile, coord) == false) continue;

                float ratio = Clamp01(GetDepthRatio(profile.Height, y) + GetBoundaryOffset(profile, x, seed));
                ServerTerrainCatalog.DepthSectionDefinition section =
                    sections.FirstOrDefault(value => value.Contains(ratio));
                if (section == null) continue;

                cells[coord] = CreateCell(coord, section.BaseTileType, 0);
            }
        }
    }

    private void ApplyBoundaries(
        Dictionary<GridCoord, TerrainCellChangeDto> cells,
        ServerTerrainCatalog.ProfileDefinition profile)
    {
        foreach (GridCoord coord in cells.Keys.ToArray())
        {
            if (IsTopOpening(profile, coord)) continue;
            if (IsBoundary(profile, coord) == false) continue;
            cells[coord] = CreateCell(coord, ServerTerrainTileType.Bedrock, 0);
        }
    }

    private static void ApplyCaves(
        Dictionary<GridCoord, TerrainCellChangeDto> cells,
        ServerTerrainCatalog.ProfileDefinition profile,
        IReadOnlyList<ServerTerrainCatalog.CaveDefinition> caves,
        Random random)
    {
        foreach (ServerTerrainCatalog.CaveDefinition cave in caves)
        {
            List<GridCoord> starts = cells.Values
                .Where(cell => cell.TileTypeID != (int)ServerTerrainTileType.Bedrock)
                .Where(cell =>
                {
                    float ratio = GetDepthRatio(profile.Height, cell.Coord.Y);
                    return ratio >= cave.MinDepthRatio && ratio <= cave.MaxDepthRatio;
                })
                .Select(cell => cell.Coord)
                .ToList();

            for (int index = 0; index < cave.CaveCount && starts.Count > 0; index++)
            {
                GridCoord cursor = starts[random.Next(starts.Count)];
                int horizontal = random.Next(2) == 0 ? -1 : 1;
                int length = random.Next(cave.MinLength, cave.MaxLength + 1);
                for (int step = 0; step < length; step++)
                {
                    int radius = random.Next(cave.MinRadius, cave.MaxRadius + 1);
                    for (int y = -radius; y <= radius; y++)
                    {
                        for (int x = -radius; x <= radius; x++)
                        {
                            if (x * x + y * y > radius * radius) continue;
                            GridCoord target = new(cursor.X + x, cursor.Y + y);
                            if (cells.TryGetValue(target, out TerrainCellChangeDto cell) == false) continue;
                            if (cell.TileTypeID == (int)ServerTerrainTileType.Bedrock) continue;
                            cells.Remove(target);
                        }
                    }

                    cursor = new GridCoord(cursor.X + horizontal, cursor.Y + random.Next(-1, 2));
                }
            }
        }
    }

    private void ApplyVariants(
        Dictionary<GridCoord, TerrainCellChangeDto> cells,
        IReadOnlyList<ServerTerrainCatalog.DepthSectionDefinition> sections,
        IReadOnlyList<ServerTerrainCatalog.VariantDefinition> variants,
        Random random)
    {
        foreach (ServerTerrainCatalog.VariantDefinition variant in variants)
        {
            ServerTerrainCatalog.DepthSectionDefinition section =
                sections.First(value => value.SectionID == variant.SectionID);
            for (int cluster = 0; cluster < variant.ClusterCount; cluster++)
            {
                List<GridCoord> candidates = cells.Values
                    .Where(cell => cell.TileTypeID == (int)section.BaseTileType)
                    .Select(cell => cell.Coord)
                    .ToList();
                if (candidates.Count == 0) break;

                GridCoord center = candidates[random.Next(candidates.Count)];
                int width = random.Next(variant.MinWidth, variant.MaxWidth + 1);
                int height = random.Next(variant.MinHeight, variant.MaxHeight + 1);
                int minX = center.X - width / 2;
                int minY = center.Y - height / 2;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        GridCoord coord = new(minX + x, minY + y);
                        if (cells.TryGetValue(coord, out TerrainCellChangeDto current) == false) continue;
                        if (current.TileTypeID != (int)section.BaseTileType) continue;

                        float nx = (x + 0.5f - width * 0.5f) / (width * 0.5f);
                        float ny = (y + 0.5f - height * 0.5f) / (height * 0.5f);
                        if (nx * nx + ny * ny > 1f) continue;
                        if (random.NextDouble() > variant.FillRatio) continue;
                        cells[coord] = CreateCell(coord, variant.TileType, 0);
                    }
                }
            }
        }
    }

    private void ApplyMinerals(
        Dictionary<GridCoord, TerrainCellChangeDto> cells,
        ServerTerrainCatalog.ProfileDefinition profile,
        IReadOnlyList<ServerTerrainCatalog.DepthSectionDefinition> sections,
        Random random)
    {
        foreach (ServerTerrainCatalog.DepthSectionDefinition section in sections)
        {
            foreach (ServerTerrainCatalog.MineralDefinition mineral in
                     catalog.GetMinerals(profile.ProfileID, section.SectionID))
            {
                List<GridCoord> baseCells = cells.Values
                    .Where(cell => cell.TileTypeID == (int)section.BaseTileType && cell.ResourceID == 0)
                    .Select(cell => cell.Coord)
                    .ToList();
                foreach (GridCoord start in baseCells)
                {
                    if (random.NextDouble() >= mineral.ClusterDensity) continue;
                    int targetCount = random.Next(mineral.MinClusterSize, mineral.MaxClusterSize + 1);
                    List<GridCoord> members = new() { start };
                    SetResource(cells, start, mineral.ResourceID);
                    while (members.Count < targetCount)
                    {
                        List<GridCoord> candidates = new();
                        foreach (GridCoord member in members)
                        {
                            foreach (GridCoord direction in Directions)
                            {
                                GridCoord candidate = new(member.X + direction.X, member.Y + direction.Y);
                                if (candidates.Contains(candidate)) continue;
                                if (cells.TryGetValue(candidate, out TerrainCellChangeDto cell) == false) continue;
                                if (cell.TileTypeID != (int)section.BaseTileType || cell.ResourceID != 0) continue;
                                candidates.Add(candidate);
                            }
                        }

                        if (candidates.Count == 0) break;
                        GridCoord selected = candidates[random.Next(candidates.Count)];
                        SetResource(cells, selected, mineral.ResourceID);
                        members.Add(selected);
                    }
                }
            }
        }
    }

    private void SetResource(
        Dictionary<GridCoord, TerrainCellChangeDto> cells,
        GridCoord coord,
        int resourceID)
    {
        TerrainCellChangeDto cell = cells[coord];
        if (catalog.TryGetResource(resourceID, out ServerTerrainCatalog.ResourceDefinition resource) == false)
            return;
        cell.ResourceID = resourceID;
        cell.Durability = resource.MaxDurability;
        cells[coord] = cell;
    }

    private void AttachRespawnArea(
        Dictionary<GridCoord, TerrainCellChangeDto> cells,
        ServerTerrainCatalog.ProfileDefinition profile)
    {
        int minX = (profile.Width - profile.TopOpeningWidth) / 2;
        int maxX = minX + profile.TopOpeningWidth - 1;
        int minY = profile.Height;
        int maxY = minY + RespawnAreaHeight - 1;
        for (int x = minX; x <= maxX; x++)
            cells[new GridCoord(x, maxY)] = CreateCell(
                new GridCoord(x, maxY), ServerTerrainTileType.Bedrock, 0);
        for (int y = minY; y < maxY; y++)
        {
            cells[new GridCoord(minX, y)] = CreateCell(
                new GridCoord(minX, y), ServerTerrainTileType.Bedrock, 0);
            cells[new GridCoord(maxX, y)] = CreateCell(
                new GridCoord(maxX, y), ServerTerrainTileType.Bedrock, 0);
        }
    }

    private TerrainCellChangeDto CreateCell(
        GridCoord coord,
        ServerTerrainTileType type,
        int resourceID)
    {
        int durability = catalog.GetTile(type).MaxDurability;
        if (resourceID > 0 &&
            catalog.TryGetResource(resourceID, out ServerTerrainCatalog.ResourceDefinition resource))
            durability = resource.MaxDurability;
        return new TerrainCellChangeDto
        {
            Coord = coord,
            TileTypeID = (int)type,
            Durability = durability,
            ResourceID = resourceID,
            LootEntries = new List<TerrainLootEntryDto>(),
        };
    }

    private static bool Contains(ServerTerrainCatalog.ProfileDefinition profile, GridCoord coord)
    {
        if (coord.X < 0 || coord.X >= profile.Width || coord.Y < 0 || coord.Y >= profile.Height)
            return false;
        if (coord.Y < profile.BoundaryThickness &&
            IsInsideCenteredWidth(profile, coord.X, profile.BottomFlatWidth) == false)
            return false;

        float radiusX = profile.Width * 0.5f;
        float topRatio = profile.TopOpeningWidth / (float)profile.Width;
        float bottomRatio = profile.BottomFlatWidth / (float)profile.Width;
        float topFactor = MathF.Sqrt(1f - topRatio * topRatio);
        float bottomFactor = MathF.Sqrt(1f - bottomRatio * bottomRatio);
        float radiusY = profile.Height / (topFactor + bottomFactor);
        float centerY = -topFactor * radiusY;
        float centeredX = coord.X + 0.5f - radiusX;
        float worldY = coord.Y + 0.5f - profile.Height;
        float heightRatio = coord.Y / (float)(profile.Height - 1);
        float widthMultiplier = 0.7f + 0.3f * heightRatio;
        float nx = centeredX / (radiusX * widthMultiplier);
        float ny = (worldY - centerY) / radiusY;
        return nx * nx + ny * ny <= 1f;
    }

    private static bool IsBoundary(ServerTerrainCatalog.ProfileDefinition profile, GridCoord coord)
    {
        for (int distance = 1; distance <= profile.BoundaryThickness; distance++)
        {
            if (Contains(profile, new GridCoord(coord.X - distance, coord.Y)) == false ||
                Contains(profile, new GridCoord(coord.X + distance, coord.Y)) == false ||
                Contains(profile, new GridCoord(coord.X, coord.Y - distance)) == false)
                return true;
            GridCoord above = new(coord.X, coord.Y + distance);
            if (Contains(profile, above) == false &&
                (above.Y < profile.Height || IsTopOpeningColumn(profile, coord.X) == false))
                return true;
        }

        return false;
    }

    private static bool IsTopOpening(ServerTerrainCatalog.ProfileDefinition profile, GridCoord coord)
        => coord.Y >= profile.Height - profile.BoundaryThickness &&
           IsTopOpeningColumn(profile, coord.X);
    private static bool IsTopOpeningColumn(ServerTerrainCatalog.ProfileDefinition profile, int x)
        => IsInsideCenteredWidth(profile, x, profile.TopOpeningWidth);
    private static bool IsInsideCenteredWidth(
        ServerTerrainCatalog.ProfileDefinition profile,
        int x,
        int width)
        => MathF.Abs(x + 0.5f - profile.Width * 0.5f) < width * 0.5f;
    private static float GetDepthRatio(int height, int y)
        => height > 1 ? (float)(height - 1 - y) / (height - 1) : 0f;
    private static float GetBoundaryOffset(
        ServerTerrainCatalog.ProfileDefinition profile,
        int x,
        int seed)
    {
        uint hash = unchecked((uint)(seed * 374761393 + x * 668265263));
        hash = (hash ^ (hash >> 13)) * 1274126177u;
        float noise = (hash & 0x00FFFFFF) / 16777215f;
        float centered = noise * (-1f + 2f * noise);
        return centered * 4f / Math.Max(1, profile.Height - 1);
    }
    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
}
