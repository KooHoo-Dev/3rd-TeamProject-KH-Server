namespace HelloServer;

/// <summary>
/// 클라이언트가 다이너마이트 사용을 요청할 때 보내는 메시지입니다.
/// 능력치 수치는 넣지 않습니다. 서버 정의를 신뢰합니다.
/// </summary>
public sealed class DynamiteThrowRequest : PacketHeader
{
    public DynamiteThrowRequest()
    {
        Type = PacketTypes.DynamiteThrow;
    }

    public int ItemID { get; set; }

    public float StartX { get; set; }
    public float StartY { get; set; }

    public float DirectionX { get; set; }
    public float DirectionY { get; set; }

    public bool IsValid()
    {
        return ItemID > 0 &&
               float.IsFinite(StartX) &&
               float.IsFinite(StartY) &&
               float.IsFinite(DirectionX) &&
               float.IsFinite(DirectionY);
    }
}

/// <summary>
/// 서버가 다이너마이트 사용을 승인한 뒤 모든 클라이언트에 보내는 시작 메시지입니다.
/// 클라이언트는 이 수치로 투사체를 표시할 뿐, 지형이나 피해를 확정하지 않습니다.
/// </summary>
public sealed class DynamiteThrownMessage : PacketHeader
{
    public DynamiteThrownMessage()
    {
        Type = PacketTypes.DynamiteThrown;
    }

    public string ProjectileID { get; set; }
    public string OwnerPlayerID { get; set; }
    public int ItemID { get; set; }

    public float StartX { get; set; }
    public float StartY { get; set; }

    // 서버가 정규화해 확정한 방향
    public float DirectionX { get; set; }
    public float DirectionY { get; set; }

    // 늦게 받은 클라이언트가 즉시 경과 시간만큼 진행시키기 위한 서버 시각
    public long StartedAtUnixMilliseconds { get; set; }

    // 서버 정의를 그대로 포함한다.
    public float ThrowSpeed { get; set; }
    public float FuseTime { get; set; }
    public float ExplosionRadius { get; set; }
}

public sealed class DynamiteExplodeRequest : PacketHeader
{
    public DynamiteExplodeRequest()
    {
        Type = PacketTypes.DynamiteExplodeRequest;
    }

    public string ProjectileID { get; set; }
    public float X { get; set; }
    public float Y { get; set; }

    public bool IsValid()
    {
        return string.IsNullOrWhiteSpace(ProjectileID) == false &&
               float.IsFinite(X) &&
               float.IsFinite(Y);
    }
}

public sealed class DynamiteExplodedMessage : PacketHeader
{
    public DynamiteExplodedMessage()
    {
        Type = PacketTypes.DynamiteExploded;
    }

    public string ProjectileID { get; set; }
    public string OwnerPlayerID { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float ExplosionRadius { get; set; }
}
