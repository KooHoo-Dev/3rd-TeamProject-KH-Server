namespace HelloServer;

public sealed class MapSessionDescriptor
{
    public string MapSessionID { get; set; }
    public string ProfileID { get; set; }
    public int Seed { get; set; }
    public string TerrainDataVersion { get; set; }
}

public sealed class MapSessionMessage : PacketHeader
{
    public MapSessionMessage()
    {
        Type = PacketTypes.MapSession;
    }

    public MapSessionDescriptor Session { get; set; }
}
