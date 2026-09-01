namespace HelloServer;

// 클라이언트의 Unity 비의존 GridCoord JSON 계약과 동일합니다.
public struct GridCoord : IEquatable<GridCoord>, IComparable<GridCoord>
{
    public int X { get; set; }
    public int Y { get; set; }

    public GridCoord(int x, int y)
    {
        X = x;
        Y = y;
    }

    public bool Equals(GridCoord other)
    {
        return X == other.X && Y == other.Y;
    }

    public override bool Equals(object obj)
    {
        return obj is GridCoord other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y);
    }

    public int CompareTo(GridCoord other)
    {
        int yComparison = Y.CompareTo(other.Y);
        return yComparison != 0 ? yComparison : X.CompareTo(other.X);
    }

    public override string ToString()
    {
        return $"({X}, {Y})";
    }
}

public sealed class MapSessionDescriptor
{
    public string MapSessionID { get; set; }
    public string ProfileID { get; set; }
    public int Seed { get; set; }
    public string TerrainDataVersion { get; set; }

    public bool IsValid()
    {
        return string.IsNullOrWhiteSpace(MapSessionID) == false &&
               string.IsNullOrWhiteSpace(ProfileID) == false &&
               string.IsNullOrWhiteSpace(TerrainDataVersion) == false;
    }
}

public sealed class MapSessionMessage : PacketHeader
{
    public MapSessionMessage()
    {
        Type = PacketTypes.MapSession;
    }

    public MapSessionDescriptor Session { get; set; }
}
