namespace HelloServer;

// Room을 수정하지 않고 도메인 Handler 구성을 확장하는 등록 지점입니다.
public static class PacketDispatcherFactory
{
    public static PacketDispatcher CreateDefault()
    {
        return new PacketDispatcher(new IPacketHandler[]
        {
            new PlayerMovePacketHandler(),
            new ChatSendPacketHandler(),
            new TerrainExcavationPacketHandler(),
            new WorldItemPickupPacketHandler(),
        });
    }
}
