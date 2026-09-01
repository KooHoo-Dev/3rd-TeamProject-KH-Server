using System.Globalization;

namespace HelloServer;

public enum ServerTerrainTileType
{
    Empty = 0,
    Ground = 1,
    Dirt = 2,
    Stone = 3,
    Bedrock = 4,
    DeepStone = 5,
    DeathLoot = 99,
}

public sealed class ServerTerrainCatalog
{
    public sealed class TileDefinition
    {
        public ServerTerrainTileType Type { get; init; }
        public int MaxDurability { get; init; }
        public bool IsMineable { get; init; }
    }

    public sealed class ResourceDefinition
    {
        public int ResourceID { get; init; }
        public int MaxDurability { get; init; }
        public int DropItemID { get; init; }
        public int DropCount { get; init; }
    }

    public sealed class ProfileDefinition
    {
        public string ProfileID { get; init; }
        public string Name { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public float CellSize { get; init; }
        public int TopOpeningWidth { get; init; }
        public int BoundaryThickness { get; init; }
        public int RespawnAreaHeight { get; init; }
        public int MapHeight => Height + RespawnAreaHeight;
        public int BottomFlatWidth => Math.Max(
            1,
            (int)Math.Round(Width * 0.25f, MidpointRounding.AwayFromZero));
        public float OriginX => -Width * CellSize * 0.5f;
        public float OriginY => -Height * CellSize;
    }

    public sealed class DepthSectionDefinition
    {
        public string ProfileID { get; init; }
        public string SectionID { get; init; }
        public int Order { get; init; }
        public float MinDepthRatio { get; init; }
        public float MaxDepthRatio { get; init; }
        public ServerTerrainTileType BaseTileType { get; init; }

        public bool Contains(float ratio)
        {
            if (ratio < MinDepthRatio) return false;
            return MaxDepthRatio >= 1f ? ratio <= MaxDepthRatio : ratio < MaxDepthRatio;
        }
    }

    public sealed class MineralDefinition
    {
        public string ProfileID { get; init; }
        public string SectionID { get; init; }
        public int Order { get; init; }
        public int ResourceID { get; init; }
        public float ClusterDensity { get; init; }
        public int MinClusterSize { get; init; }
        public int MaxClusterSize { get; init; }
    }

    public sealed class VariantDefinition
    {
        public string ProfileID { get; init; }
        public string SectionID { get; init; }
        public int Order { get; init; }
        public ServerTerrainTileType TileType { get; init; }
        public int ClusterCount { get; init; }
        public int MinWidth { get; init; }
        public int MaxWidth { get; init; }
        public int MinHeight { get; init; }
        public int MaxHeight { get; init; }
        public float FillRatio { get; init; }
    }

    public sealed class CaveDefinition
    {
        public string ProfileID { get; init; }
        public int Order { get; init; }
        public float MinDepthRatio { get; init; }
        public float MaxDepthRatio { get; init; }
        public int CaveCount { get; init; }
        public int MinLength { get; init; }
        public int MaxLength { get; init; }
        public int MinRadius { get; init; }
        public int MaxRadius { get; init; }
    }

    private readonly Dictionary<ServerTerrainTileType, TileDefinition> tiles = new();
    private readonly Dictionary<int, ResourceDefinition> resources = new();
    private readonly Dictionary<string, ProfileDefinition> profiles =
        new(StringComparer.Ordinal);
    private readonly List<DepthSectionDefinition> sections = new();
    private readonly List<MineralDefinition> minerals = new();
    private readonly List<VariantDefinition> variants = new();
    private readonly List<CaveDefinition> caves = new();

    public string DataVersion { get; }

    public ServerTerrainCatalog(string dataRoot)
    {
        LoadTiles(Path.Combine(dataRoot, "terrain_tiles.tsv"));
        LoadResources(Path.Combine(dataRoot, "terrain_resources.tsv"));
        LoadProfiles(Path.Combine(dataRoot, "terrain_seed_profiles.tsv"));
        LoadSections(Path.Combine(dataRoot, "terrain_depth_sections.tsv"));
        LoadMinerals(Path.Combine(dataRoot, "terrain_section_minerals.tsv"));
        LoadVariants(Path.Combine(dataRoot, "terrain_variant_clusters.tsv"));
        LoadCaves(Path.Combine(dataRoot, "terrain_caves.tsv"));
        DataVersion = ComputeDataVersion(dataRoot);
    }

    public ProfileDefinition GetProfile(string profileID) => profiles[profileID];
    public TileDefinition GetTile(ServerTerrainTileType type) => tiles[type];
    public bool TryGetResource(int resourceID, out ResourceDefinition definition)
        => resources.TryGetValue(resourceID, out definition);
    public IReadOnlyList<DepthSectionDefinition> GetSections(string profileID)
        => sections.Where(value => value.ProfileID == profileID).OrderBy(value => value.Order).ToArray();
    public IReadOnlyList<MineralDefinition> GetMinerals(string profileID, string sectionID)
        => minerals.Where(value => value.ProfileID == profileID && value.SectionID == sectionID)
            .OrderBy(value => value.Order).ToArray();
    public IReadOnlyList<VariantDefinition> GetVariants(string profileID)
        => variants.Where(value => value.ProfileID == profileID).OrderBy(value => value.Order).ToArray();
    public IReadOnlyList<CaveDefinition> GetCaves(string profileID)
        => caves.Where(value => value.ProfileID == profileID).OrderBy(value => value.Order).ToArray();

    private void LoadTiles(string path)
    {
        foreach (string[] row in ReadRows(path))
        {
            ServerTerrainTileType type = Enum.Parse<ServerTerrainTileType>(row[0], true);
            tiles.Add(type, new TileDefinition
            {
                Type = type,
                MaxDurability = ParseInt(row[2]),
                IsMineable = ParseBool(row[3]),
            });
        }
    }

    private void LoadResources(string path)
    {
        foreach (string[] row in ReadRows(path))
        {
            int id = ParseInt(row[0]);
            resources.Add(id, new ResourceDefinition
            {
                ResourceID = id,
                MaxDurability = ParseInt(row[2]),
                DropItemID = ParseInt(row[4]),
                DropCount = ParseInt(row[5]),
            });
        }
    }

    private void LoadProfiles(string path)
    {
        foreach (string[] row in ReadRows(path))
        {
            ProfileDefinition profile = new()
            {
                ProfileID = row[0],
                Name = row[1],
                Width = ParseInt(row[2]),
                Height = ParseInt(row[3]),
                CellSize = ParseFloat(row[4]),
                TopOpeningWidth = ParseInt(row[5]),
                BoundaryThickness = ParseInt(row[6]),
                RespawnAreaHeight = ParseInt(row[7]),
            };

            if (profile.RespawnAreaHeight <= 0)
                throw new InvalidDataException(
                    $"RespawnAreaHeight는 1 이상이어야 합니다: {profile.ProfileID}");

            profiles.Add(profile.ProfileID, profile);
        }
    }

    private void LoadSections(string path)
    {
        foreach (string[] row in ReadRows(path))
        {
            sections.Add(new DepthSectionDefinition
            {
                ProfileID = row[0],
                SectionID = row[1],
                Order = ParseInt(row[2]),
                MinDepthRatio = ParseFloat(row[3]),
                MaxDepthRatio = ParseFloat(row[4]),
                BaseTileType = Enum.Parse<ServerTerrainTileType>(row[5], true),
            });
        }
    }

    private void LoadMinerals(string path)
    {
        foreach (string[] row in ReadRows(path))
        {
            minerals.Add(new MineralDefinition
            {
                ProfileID = row[0],
                SectionID = row[1],
                Order = ParseInt(row[2]),
                ResourceID = ParseInt(row[3]),
                ClusterDensity = ParseFloat(row[4]),
                MinClusterSize = ParseInt(row[5]),
                MaxClusterSize = ParseInt(row[6]),
            });
        }
    }

    private void LoadVariants(string path)
    {
        foreach (string[] row in ReadRows(path))
        {
            variants.Add(new VariantDefinition
            {
                ProfileID = row[0],
                SectionID = row[1],
                Order = ParseInt(row[2]),
                TileType = Enum.Parse<ServerTerrainTileType>(row[3], true),
                ClusterCount = ParseInt(row[4]),
                MinWidth = ParseInt(row[5]),
                MaxWidth = ParseInt(row[6]),
                MinHeight = ParseInt(row[7]),
                MaxHeight = ParseInt(row[8]),
                FillRatio = ParseFloat(row[9]),
            });
        }
    }

    private void LoadCaves(string path)
    {
        foreach (string[] row in ReadRows(path))
        {
            caves.Add(new CaveDefinition
            {
                ProfileID = row[0],
                Order = ParseInt(row[1]),
                MinDepthRatio = ParseFloat(row[2]),
                MaxDepthRatio = ParseFloat(row[3]),
                CaveCount = ParseInt(row[4]),
                MinLength = ParseInt(row[5]),
                MaxLength = ParseInt(row[6]),
                MinRadius = ParseInt(row[7]),
                MaxRadius = ParseInt(row[8]),
            });
        }
    }

    private static IEnumerable<string[]> ReadRows(string path)
    {
        if (File.Exists(path) == false)
            throw new FileNotFoundException("서버 지형 데이터 테이블을 찾을 수 없습니다.", path);

        return File.ReadLines(path)
            .Skip(1)
            .Where(line => string.IsNullOrWhiteSpace(line) == false)
            .Select(line => line.TrimEnd('\r').Split('\t'));
    }

    private static int ParseInt(string value)
        => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    private static float ParseFloat(string value)
        => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static bool ParseBool(string value) => bool.Parse(value);

    private static string ComputeDataVersion(string dataRoot)
    {
        using System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create();
        foreach (string path in Directory.GetFiles(dataRoot, "*.tsv").OrderBy(value => value))
        {
            byte[] bytes = File.ReadAllBytes(path);
            hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }

        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(hash.Hash).Substring(0, 12).ToLowerInvariant();
    }
}
