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
}
