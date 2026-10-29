using Amr.Simulator.Navigation;
using Xunit;

namespace Amr.Simulator.Tests;

public sealed class GridNavigationTests
{
    [Fact]
    public void OriginalMapHasNineStepPath()
    {
        var s = new GridNavigator().Read();
        Assert.Equal(9, s.RemainingDistance);
        Assert.Equal(s.Start, s.Path[0]);
        Assert.Equal(s.Goal, s.Path[^1]);
        AssertValid(s);
    }

    [Fact]
    public void StartEqualsGoalReturnsOneCell()
    {
        var cell = new GridCell(0, 0);
        Assert.Equal(new[] { cell }, GridPathfinder.FindPath(new int[1, 1], cell, cell));
    }

    [Fact]
    public void UnreachableReturnsEmptyPath() =>
        Assert.Empty(GridPathfinder.FindPath(new[,] { { 0, 1, 0 } }, new(0, 0), new(0, 2)));

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    public void EmptyMapRejected(int rows, int cols) =>
        Assert.Throws<ArgumentException>(() => GridPathfinder.FindPath(new int[rows, cols], new(0, 0), new(0, 0)));

    [Fact]
    public void NullMapRejected() =>
        Assert.Throws<ArgumentNullException>(() => GridPathfinder.FindPath(null!, new(0, 0), new(0, 0)));

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void NonBinaryMapRejected(int value) =>
        Assert.Throws<ArgumentException>(() => GridPathfinder.FindPath(new[,] { { 0, value, 0 } }, new(0, 0), new(0, 2)));

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    public void InvalidCoordinateRejected(int row, int col) =>
        Assert.Throws<ArgumentException>(() => GridPathfinder.FindPath(new int[2, 2], new(row, col), new(0, 0)));

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public void BlockedEndpointsRejected(int start, int goal) =>
        Assert.Throws<ArgumentException>(() => GridPathfinder.FindPath(new[,] { { start, goal } }, new(0, 0), new(0, 1)));

    [Fact]
    public void CompletesInNineStepsAndThenStaysStopped()
    {
        var navigator = new GridNavigator();
        Assert.True(navigator.Start());
        Assert.False(navigator.Start());
        for (int i = 0; i < 9; i++) { Assert.True(navigator.Step()); AssertValid(navigator.Read()); }
        var s = navigator.Read();
        Assert.Equal("Completed", s.Phase);
        Assert.Equal(s.Goal, s.Current);
        Assert.Equal(9, s.Moves);
        Assert.Equal(0, s.RemainingDistance);
        Assert.False(navigator.Step());
        Assert.Equal(s.Current, navigator.Read().Current);
    }

    [Fact]
    public void NewObstacleDetoursFromCurrentPosition()
    {
        var navigator = new GridNavigator();
        navigator.Start();
        for (int i = 0; i < 3; i++) navigator.Step();
        Assert.Equal(new GridCell(2, 1), navigator.Read().Current);
        navigator.SetObstacle(new(2, 2), true);
        var s = navigator.Read();
        Assert.Equal(new GridCell(2, 1), s.Current);
        Assert.Equal(8, s.RemainingDistance);
        Assert.DoesNotContain(new GridCell(2, 2), s.Path);
        Assert.Equal(new GridCell(2, 0), s.Path[1]);
        for (int i = 0; i < 8; i++) { Assert.True(navigator.Step()); AssertValid(navigator.Read()); }
        Assert.Equal("Completed", navigator.Read().Phase);
        Assert.Equal(11, navigator.Read().Moves);
    }

    [Fact]
    public void FullyBlockedWaitsThenAutomaticallyResumes()
    {
        var navigator = new GridNavigator();
        navigator.Start(); navigator.Step();
        var current = navigator.Read().Current;
        navigator.SetObstacle(new(1, 1), true);
        Assert.Equal("Waiting", navigator.Read().Phase);
        Assert.Null(navigator.Read().RemainingDistance);
        for (int i = 0; i < 10; i++) Assert.False(navigator.Step());
        Assert.Equal(current, navigator.Read().Current);
        Assert.Equal(1, navigator.Read().Moves);
        navigator.SetObstacle(new(1, 1), false);
        Assert.Equal("Running", navigator.Read().Phase);
        Assert.True(navigator.Step());
        Assert.Equal(new GridCell(1, 1), navigator.Read().Current);
    }

    [Fact]
    public void RemovingObstacleSelectsShorterRouteImmediately()
    {
        var navigator = new GridNavigator();
        navigator.SetObstacle(new(2, 2), true);
        navigator.Start();
        for (int i = 0; i < 3; i++) navigator.Step();
        Assert.Equal(8, navigator.Read().RemainingDistance);
        navigator.SetObstacle(new(2, 2), false);
        Assert.Equal(6, navigator.Read().RemainingDistance);
        Assert.Equal(new GridCell(2, 2), navigator.Read().Path[1]);
    }

    [Fact]
    public void CancellationStopsMovementAndDoesNotResumeOnMapEdit()
    {
        var navigator = new GridNavigator();
        navigator.Start(); navigator.Step(); navigator.Cancel();
        var before = navigator.Read();
        navigator.SetObstacle(new(2, 2), true);
        Assert.False(navigator.Step());
        Assert.Equal("Canceled", navigator.Read().Phase);
        Assert.Equal(before.Current, navigator.Read().Current);
        Assert.True(navigator.Start());
        Assert.Equal(before.Start, navigator.Read().Current);
    }

    [Fact]
    public void ResetRestoresOriginalMapAndStopsActiveRun()
    {
        var navigator = new GridNavigator();
        navigator.Start(); navigator.Step(); navigator.SetObstacle(new(2, 2), true);
        navigator.Reset();
        var s = navigator.Read();
        Assert.Equal("Ready", s.Phase);
        Assert.Equal(s.Start, s.Current);
        Assert.Equal(0, s.Map[2][2]);
        Assert.Equal(9, s.RemainingDistance);
        Assert.Equal(0, s.Moves);
        Assert.False(navigator.Step());
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(0, 0)]
    [InlineData(4, 5)]
    [InlineData(-1, 0)]
    [InlineData(5, 0)]
    public void ProtectedOrInvalidCellEditRejected(int row, int col)
    {
        var navigator = new GridNavigator();
        Assert.Throws<ArgumentException>(() => navigator.SetObstacle(new(row, col), true));
        Assert.Equal(9, navigator.Read().RemainingDistance);
        Assert.Equal(0, navigator.Read().MapVersion);
    }

    [Fact]
    public void OccupiedCellEditRejectedAndSnapshotIsIndependent()
    {
        var navigator = new GridNavigator();
        navigator.Start(); navigator.Step();
        var s = navigator.Read();
        Assert.Throws<ArgumentException>(() => navigator.SetObstacle(s.Current, true));
        s.Map[0][1] = 1; s.Path[0] = new(99, 99); s.Traveled[0] = new(99, 99);
        Assert.Equal(0, navigator.Read().Map[0][1]);
        Assert.Equal(navigator.Read().Current, navigator.Read().Path[0]);
        Assert.Equal(navigator.Read().Start, navigator.Read().Traveled[0]);
    }

    [Fact]
    public void DijkstraMatchesBfsOnThreeHundredSeededMaps()
    {
        var random = new Random(221026);
        for (int trial = 0; trial < 300; trial++)
        {
            var map = new int[6, 7];
            for (int r = 0; r < 6; r++)
                for (int c = 0; c < 7; c++) map[r, c] = random.Next(100) < 35 ? 1 : 0;
            var start = new GridCell(random.Next(6), random.Next(7));
            var goal = new GridCell(random.Next(6), random.Next(7));
            map[start.Row, start.Col] = map[goal.Row, goal.Col] = 0;
            var path = GridPathfinder.FindPath(map, start, goal);
            Assert.Equal(BfsDistance(map, start, goal), path.Count == 0 ? (int?)null : path.Count - 1);
            for (int i = 0; i < path.Count; i++)
            {
                Assert.Equal(0, map[path[i].Row, path[i].Col]);
                if (i > 0) Assert.Equal(1, Math.Abs(path[i].Row - path[i - 1].Row) + Math.Abs(path[i].Col - path[i - 1].Col));
            }
        }
    }

    private static int? BfsDistance(int[,] map, GridCell start, GridCell goal)
    {
        var queue = new Queue<(GridCell Cell, int Distance)>();
        var visited = new HashSet<GridCell> { start };
        queue.Enqueue((start, 0));
        while (queue.TryDequeue(out var item))
        {
            if (item.Cell == goal) return item.Distance;
            foreach (var d in new[] { new GridCell(-1, 0), new(1, 0), new(0, -1), new(0, 1) })
            {
                var next = new GridCell(item.Cell.Row + d.Row, item.Cell.Col + d.Col);
                if (next.Row < 0 || next.Row >= map.GetLength(0) || next.Col < 0 || next.Col >= map.GetLength(1) || map[next.Row, next.Col] == 1 || !visited.Add(next)) continue;
                queue.Enqueue((next, item.Distance + 1));
            }
        }
        return null;
    }

    private static void AssertValid(GridSnapshot s)
    {
        Assert.Equal(0, s.Map[s.Current.Row][s.Current.Col]);
        for (int i = 0; i < s.Path.Length; i++)
        {
            Assert.Equal(0, s.Map[s.Path[i].Row][s.Path[i].Col]);
            if (i > 0) Assert.Equal(1, Math.Abs(s.Path[i].Row - s.Path[i - 1].Row) + Math.Abs(s.Path[i].Col - s.Path[i - 1].Col));
        }
    }
}
