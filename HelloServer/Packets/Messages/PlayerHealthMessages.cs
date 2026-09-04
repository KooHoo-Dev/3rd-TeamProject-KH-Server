namespace HelloServer;

public sealed class PlayerHealthStateDto
{
    public string PlayerID { get; set; }
    public int CurrentHealth { get; set; }
    public int MaxHealth { get; set; }
    public bool IsDead { get; set; }
}

public sealed class PlayerHealthSnapshotMessage : PacketHeader
{
    public PlayerHealthSnapshotMessage()
    {
        Type = PacketTypes.PlayerHealthSnapshot;
    }

    public PlayerHealthStateDto[] Players { get; set; } = Array.Empty<PlayerHealthStateDto>();
}

public sealed class PlayerHealthChangedMessage : PacketHeader
{
    public PlayerHealthChangedMessage()
    {
        Type = PacketTypes.PlayerHealthChanged;
    }

    public PlayerHealthStateDto Player { get; set; }
    public string DamageType { get; set; }
}

public sealed class PlayerDamageRequest : PacketHeader
{
    public PlayerDamageRequest()
    {
        Type = PacketTypes.PlayerDamage;
    }

    public string DamageType { get; set; }
    public int Amount { get; set; }
    public float X { get; set; }
    public float Y { get; set; }

    public bool IsValid()
    {
        return Amount > 0 && float.IsFinite(X) && float.IsFinite(Y) &&
               string.IsNullOrWhiteSpace(DamageType) == false;
    }
}

public sealed class PlayerDiedMessage : PacketHeader
{
    public PlayerDiedMessage()
    {
        Type = PacketTypes.PlayerDied;
    }

    public PlayerHealthStateDto Player { get; set; }
}

public sealed class PlayerRespawnedMessage : PacketHeader
{
    public PlayerRespawnedMessage()
    {
        Type = PacketTypes.PlayerRespawned;
    }

    public PlayerHealthStateDto Player { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
}

public sealed class PlayerRespawnRequest : PacketHeader
{
    public PlayerRespawnRequest()
    {
        Type = PacketTypes.PlayerRespawnRequest;
    }

    public bool IsValid() => true;
}
