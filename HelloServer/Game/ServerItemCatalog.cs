using System.Globalization;

namespace HelloServer;

public sealed class ServerItemCatalog
{
    public sealed record PickaxeDefinition(float Range, int DigPower);
    public sealed record ItemDefinition(int Price, string ItemType, int Weight);

    private readonly Dictionary<int, PickaxeDefinition> pickaxes = new();
    private readonly Dictionary<int, ItemDefinition> items = new();

    public ServerItemCatalog(string path)
    {
        foreach (string line in File.ReadLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] cells = line.Split('\t');
            if (cells.Length < 10) continue;

            int id = int.Parse(cells[0], CultureInfo.InvariantCulture);
            int price = int.Parse(cells[3], CultureInfo.InvariantCulture);
            string itemType = cells[4];
            int weight = int.Parse(cells[9], CultureInfo.InvariantCulture);
            if (price < 0)
                throw new InvalidDataException($"아이템 가격은 음수일 수 없습니다. ItemID={id}");
            if (weight < 0)
                throw new InvalidDataException($"아이템 무게는 음수일 수 없습니다. ItemID={id}");

            items.Add(id, new ItemDefinition(price, itemType, weight));

            if (cells[4] != "Pickaxe") continue;

            float range = float.Parse(cells[7], CultureInfo.InvariantCulture);
            int digPower = int.Parse(cells[8], CultureInfo.InvariantCulture);
            pickaxes.Add(id, new PickaxeDefinition(range, digPower));
        }
    }

    public bool TryGetPickaxe(int itemID, out PickaxeDefinition definition)
        => pickaxes.TryGetValue(itemID, out definition);

    public bool TryGetItem(int itemID, out ItemDefinition definition)
        => items.TryGetValue(itemID, out definition);

    public int GoldItemID => items
        .Where(pair => pair.Value.ItemType == "Gold")
        .Select(pair => pair.Key)
        .Single();
}
