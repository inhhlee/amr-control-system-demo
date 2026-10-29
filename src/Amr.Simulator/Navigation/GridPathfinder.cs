namespace Amr.Simulator.Navigation;

public readonly record struct GridCell(int Row, int Col);

/// <summary>0=통로, 1=벽. 상하좌우 이동 비용은 각각 1이다.</summary>
public static class GridPathfinder
{
    public static List<GridCell> FindPath(int[,] map, GridCell start, GridCell goal)
    {
        ArgumentNullException.ThrowIfNull(map);
        int rows = map.GetLength(0), cols = map.GetLength(1);
        bool InBounds(GridCell p) => p.Row >= 0 && p.Row < rows && p.Col >= 0 && p.Col < cols;
        if (rows == 0 || cols == 0) throw new ArgumentException("맵은 비어 있을 수 없습니다.", nameof(map));
        foreach (int value in map)
            if (value is not (0 or 1)) throw new ArgumentException("맵은 0과 1만 포함해야 합니다.", nameof(map));
        if (!InBounds(start) || !InBounds(goal)) throw new ArgumentException("좌표가 맵 범위를 벗어났습니다.");
        if (map[start.Row, start.Col] != 0 || map[goal.Row, goal.Col] != 0)
            throw new ArgumentException("출발점과 목적지는 통로여야 합니다.");

        var distances = new int[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++) distances[r, c] = int.MaxValue;
        var previous = new Dictionary<GridCell, GridCell>();
        // 같은 거리일 때도 이웃 방문 순서를 유지한다.
        var queue = new PriorityQueue<GridCell, (int Distance, int Order)>();
        int order = 0;
        distances[start.Row, start.Col] = 0;
        queue.Enqueue(start, (0, order++));
        (int Row, int Col)[] directions = [(-1, 0), (1, 0), (0, -1), (0, 1)];
        while (queue.TryDequeue(out var current, out var priority))
        {
            if (priority.Distance != distances[current.Row, current.Col]) continue;
            if (current == goal)
            {
                var path = new List<GridCell> { goal };
                while (path[^1] != start) path.Add(previous[path[^1]]);
                path.Reverse();
                return path;
            }
            foreach (var (dr, dc) in directions)
            {
                var next = new GridCell(current.Row + dr, current.Col + dc);
                if (!InBounds(next) || map[next.Row, next.Col] != 0) continue;
                int candidate = priority.Distance + 1;
                if (candidate >= distances[next.Row, next.Col]) continue;
                distances[next.Row, next.Col] = candidate;
                previous[next] = current;
                queue.Enqueue(next, (candidate, order++));
            }
        }
        return [];
    }
}
