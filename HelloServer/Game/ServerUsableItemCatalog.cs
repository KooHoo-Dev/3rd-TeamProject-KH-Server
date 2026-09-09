namespace HelloServer;

/// <summary>Items.tsv에 넣기 어려운 사용 효과의 서버 권위 정의입니다.</summary>
public static class ServerUsableItemCatalog
{
    public sealed record Definition(int ItemID, int HealAmount, float DetectRadius);

    private static readonly Dictionary<int, Definition> definitions = new()
    {
        [51] = new Definition(51, 20, 0f),
        [52] = new Definition(52, 0, 6f),
    };

    public static bool TryGet(int itemID, out Definition definition)
        => definitions.TryGetValue(itemID, out definition);
}
