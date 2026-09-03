namespace HelloServer;

public readonly struct GridCoord : IEquatable<GridCoord>, IComparable<GridCoord>
{
    public int X { get; init; }
    public int Y { get; init; }

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
