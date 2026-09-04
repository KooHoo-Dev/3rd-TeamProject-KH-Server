namespace HelloServer;

/// <summary>
/// 서버가 권위 있게 관리하는 다이너마이트 성능 정의입니다.
/// 클라이언트는 dynamite.thrown 메시지에 포함된 값을 표시용으로만 사용합니다.
/// </summary>
public static class ServerDynamiteCatalog
{
    public sealed record DynamiteDefinition(
        int ItemID,
        float ThrowSpeed,
        float FuseTime,
        float ExplosionRadius,
        int ExplosionPower);

    private static readonly Dictionary<int, DynamiteDefinition> definitions = new()
    {
        [1] = new DynamiteDefinition(
            ItemID: 1,
            ThrowSpeed: 10f,
            FuseTime: 5f,
            ExplosionRadius: 2.5f,
            ExplosionPower: 3),
    };

    public static bool TryGet(int itemID, out DynamiteDefinition definition)
    {
        return definitions.TryGetValue(itemID, out definition);
    }
}
