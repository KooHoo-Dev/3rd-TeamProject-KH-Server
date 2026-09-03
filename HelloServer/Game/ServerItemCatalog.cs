using System.Globalization;

namespace HelloServer;

public sealed class ServerItemCatalog
{
    public sealed record PickaxeDefinition(float Range, int DigPower);

    private readonly Dictionary<int, PickaxeDefinition> pickaxes = new();

    public ServerItemCatalog(string path)
    {
        foreach (string line in File.ReadLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] cells = line.Split('\t');
            if (cells.Length < 9 || cells[4] != "Pickaxe") continue;

            int id = int.Parse(cells[0], CultureInfo.InvariantCulture);
            float range = float.Parse(cells[7], CultureInfo.InvariantCulture);
            int digPower = int.Parse(cells[8], CultureInfo.InvariantCulture);
            pickaxes.Add(id, new PickaxeDefinition(range, digPower));
        }
    }

    public bool TryGetPickaxe(int itemID, out PickaxeDefinition definition)
        => pickaxes.TryGetValue(itemID, out definition);
}
