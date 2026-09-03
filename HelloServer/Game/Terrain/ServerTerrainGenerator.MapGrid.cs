namespace HelloServer;

public sealed partial class ServerTerrainGenerator
{
    #region 유니티 Mathf 와 같은 값을 내는 도우미

    // 이름과 동작을 유니티 쪽에 맞춥니다. 여기가 다르면 지형이 어긋납니다.
    private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

    private static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

    // 유니티 Mathf.RoundToInt 는 (int)Math.Round(f) 입니다.
    // .5 는 짝수 쪽으로 갑니다. AwayFromZero 가 아닙니다.
    private static int RoundToInt(float value) => (int)Math.Round((double)value);

    private static float Sqrt(float value) => (float)Math.Sqrt(value);

    // 유니티 Vector2 의 최소 대응물입니다. 동굴이 걸어가는 데만 씁니다.
    private readonly struct Vec2
    {
        public readonly float X;
        public readonly float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

        // 유니티와 같습니다. 길이가 1e-5 이하면 영벡터를 돌려줍니다.
        public Vec2 Normalized()
        {
            float magnitude = Sqrt(X * X + Y * Y);
            return magnitude > 1E-05f ? new Vec2(X / magnitude, Y / magnitude) : new Vec2(0f, 0f);
        }
    }

    #endregion

    #region 맵

    // 클라이언트 TerrainMapFile 과 같은 자료입니다.
    // 타일은 y * Width + x 로 늘어놓고, 자원은 칸마다 번호 하나를 답니다.
    private sealed class MapGrid
    {
        public int Width;
        public int Height;
        public int Seed;
        public List<ServerTerrainTileType> Tiles = new();
        public List<int> Resources = new();

        public ServerTerrainTileType GetTile(GridCoord coord) => Tiles[coord.Y * Width + coord.X];

        public void SetTile(GridCoord coord, ServerTerrainTileType type)
        {
            Tiles[coord.Y * Width + coord.X] = type;

            // 빈 칸에는 자원이 남지 않습니다. 클라이언트도 같습니다.
            if (type == ServerTerrainTileType.Empty) SetResource(coord, 0);
        }

        public int GetResource(GridCoord coord) => Resources[coord.Y * Width + coord.X];

        public void SetResource(GridCoord coord, int resourceID)
            => Resources[coord.Y * Width + coord.X] = resourceID < 0 ? 0 : resourceID;

        public bool IsInside(GridCoord coord)
            => coord.X >= 0 && coord.X < Width && coord.Y >= 0 && coord.Y < Height;

        public void Grow(int rows)
        {
            for (int i = 0; i < Width * rows; i++)
            {
                Tiles.Add(ServerTerrainTileType.Empty);
                Resources.Add(0);
            }

            Height += rows;
        }
    }

    private static MapGrid CreateEmptyMap(
        ServerTerrainCatalog.ProfileDefinition profile,
        int seed)
    {
        MapGrid map = new()
        {
            Width = profile.Width,
            Height = profile.Height,
            Seed = seed,
        };

        for (int i = 0; i < profile.Width * profile.Height; i++)
        {
            map.Tiles.Add(ServerTerrainTileType.Empty);
            map.Resources.Add(0);
        }

        return map;
    }

    #endregion

    #region 타원 모양

    // 위아래가 잘린 타원입니다. 맵의 바깥 윤곽을 정합니다.
    private readonly struct EllipseShape
    {
        private readonly ServerTerrainCatalog.ProfileDefinition profile;
        private readonly float radiusX;
        private readonly float radiusY;
        private readonly float centerY;

        public EllipseShape(ServerTerrainCatalog.ProfileDefinition profile)
        {
            this.profile = profile;
            radiusX = profile.Width * 0.5f;

            float topRatio = profile.TopOpeningWidth / (float)profile.Width;
            float bottomRatio = profile.BottomFlatWidth / (float)profile.Width;
            float topFactor = Sqrt(1f - topRatio * topRatio);
            float bottomFactor = Sqrt(1f - bottomRatio * bottomRatio);

            radiusY = profile.Height / (topFactor + bottomFactor);
            centerY = -topFactor * radiusY;
        }

        public bool Contains(GridCoord coord)
        {
            if (coord.X < 0 || coord.X >= profile.Width ||
                coord.Y < 0 || coord.Y >= profile.Height)
                return false;

            if (coord.Y < profile.BoundaryThickness &&
                IsInsideCenteredWidth(coord.X, profile.BottomFlatWidth) == false)
                return false;

            const float BottomWidthMultiplier = 0.7f;

            float centeredX = coord.X + 0.5f - radiusX;
            float worldY = coord.Y + 0.5f - profile.Height;
            float heightRatio = coord.Y / (float)(profile.Height - 1);
            float widthMultiplier = Lerp(BottomWidthMultiplier, 1f, heightRatio);
            float normalizedX = centeredX / (radiusX * widthMultiplier);
            float normalizedY = (worldY - centerY) / radiusY;

            return normalizedX * normalizedX + normalizedY * normalizedY <= 1f;
        }

        public bool IsTopOpening(GridCoord coord)
            => coord.Y >= profile.Height - profile.BoundaryThickness &&
               IsInsideCenteredWidth(coord.X, profile.TopOpeningWidth);

        public bool IsTopOpeningColumn(int x)
            => IsInsideCenteredWidth(x, profile.TopOpeningWidth);

        private bool IsInsideCenteredWidth(int x, int width)
        {
            float centeredX = x + 0.5f - radiusX;
            return Math.Abs(centeredX) < width * 0.5f;
        }
    }

    #endregion
}
