namespace HelloServer;

public sealed partial class ServerTerrainGenerator
{
    // 떠 있는 덩어리? 낙하하는 덩어리?? 그거 검사

    private static readonly GridCoord[] DIRECTIONS = [new(0, 1), new(0, -1), new(-1, 0), new(1, 0)];

    // 다녀간 칸을 HashSet 이 아니라 bool 배열로 표시합니다.
    //
    // 격자는 9,630칸으로 크기가 정해져 있고 좌표는 y * Width + x 하나로 번호가 됩니다.
    // 해시를 계산하고 통을 늘려 가며 5,000개를 담을 이유가 없습니다.
    // 동굴을 뚫을 때마다 불리는 자리라 방 하나 만드는 시간의 절반 가까이가 여기였습니다.
    //
    // 순서에는 영향이 없습니다. 여기서 하는 일은 담기와 확인뿐이고,
    // HashSet 을 도는 코드가 없기 때문입니다.
    private static bool HasFloatingGroup(MapGrid map)
    {
        bool[] visited = new bool[map.Width * map.Height];

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                GridCoord start = new(x, y);
                ServerTerrainTileType type = map.GetTile(start);

                if (type is ServerTerrainTileType.Empty or ServerTerrainTileType.Bedrock) continue;
                if (visited[y * map.Width + x]) continue;

                if (IsGroupSupported(map, start, visited) == false) return true;
            }
        }

        return false;
    }

    private static bool IsGroupSupported(MapGrid map, GridCoord start, bool[] visited)
    {
        Queue<GridCoord> queue = new();
        bool isSupported = false;

        visited[start.Y * map.Width + start.X] = true;
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            GridCoord current = queue.Dequeue();
            GridCoord below = new(current.X, current.Y - 1);

            if (map.IsInside(below) && map.GetTile(below) == ServerTerrainTileType.Bedrock)
                isSupported = true;

            foreach (GridCoord direction in DIRECTIONS)
            {
                GridCoord next = new(current.X + direction.X, current.Y + direction.Y);

                if (map.IsInside(next) == false) continue;

                ServerTerrainTileType type = map.GetTile(next);

                if (type is ServerTerrainTileType.Empty or ServerTerrainTileType.Bedrock)
                    continue;

                int index = next.Y * map.Width + next.X;
                if (visited[index]) continue;

                visited[index] = true;
                queue.Enqueue(next);
            }
        }

        return isSupported;
    }
}
