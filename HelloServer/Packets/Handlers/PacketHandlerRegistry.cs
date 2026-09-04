namespace HelloServer;

// 새 메시지 종류를 만들면 여기 목록에 핸들러를 넣어야 합니다.
// 빠뜨리면 서버가 그 메시지를 받고도 아무 일도 하지 않습니다.
public static class PacketHandlerRegistry
{
    public static PacketDispatcher CreateDefault()
    {
        return new PacketDispatcher(new IPacketHandler[]
        {
            new PlayerMovePacketHandler(),
            new PlayerReadyPacketHandler(),
            new PlayerDamagePacketHandler(),
            new PlayerRespawnPacketHandler(),
            new ChatSendPacketHandler(),
            new TerrainExcavationPacketHandler(),
            new TerrainDeathLootPacketHandler(),
            new TerrainCollapseStartPacketHandler(),
            new TerrainCollapsePlacementPacketHandler(),
            new WorldItemPickupPacketHandler(),
            new WorldItemDropPacketHandler(),
            new DynamiteThrowPacketHandler(),
            new DynamiteExplodePacketHandler(),
        });
    }
}
