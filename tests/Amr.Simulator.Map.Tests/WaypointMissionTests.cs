using System.Text;
using Amr.Simulator.Map.Navigation;
using Xunit;

namespace Amr.Simulator.Map.Tests;

public sealed class WaypointMissionTests
{
    private static readonly GridCell[] Route = [new(2, 3), new(4, 3), new(4, 5)];
    private static GridNavigator Mission() => new(new(0, 0), Route);

    [Fact]
    public void NormalMissionVisitsEveryTargetInOrderAndStopsAtFinalGoal()
    {
        var navigator = Mission();
        Assert.Equal(5, navigator.Read().RemainingDistance);
        Assert.Equal(Route[0], navigator.Read().CurrentTarget);
        navigator.Start();
        for (int move = 1; move <= 9; move++)
        {
            Assert.True(navigator.Step());
            Assert.Equal(move < 9 ? "Running" : "Completed", navigator.Read().Phase);
        }
        var state = navigator.Read();
        Assert.Equal(new[] { 0, 1, 2 }, state.ReachedWaypoints.Select(x => x.Index));
        Assert.Equal(new[] { 5, 7, 9 }, state.ReachedWaypoints.Select(x => x.Moves));
        Assert.Equal(Route, state.ReachedWaypoints.Select(x => x.Cell));
        Assert.Equal(3, state.NextWaypointIndex);
        Assert.Null(state.CurrentTarget);
        Assert.False(navigator.Step());
    }

    [Fact]
    public void DetourCrossingLaterWaypointDoesNotSkipCurrentTarget()
    {
        var navigator = Mission();
        navigator.Start();
        for (int i = 0; i < 3; i++) navigator.Step();
        navigator.SetObstacle(new(2, 2), true);
        Assert.Equal(8, navigator.Read().RemainingDistance);
        for (int i = 0; i < 6; i++) navigator.Step();
        var crossed = navigator.Read();
        Assert.Equal(Route[1], crossed.Current); // W2를 먼저 지나지만 아직 W1이 목표다.
        Assert.Equal(0, crossed.NextWaypointIndex);
        Assert.Empty(crossed.ReachedWaypoints);
        Assert.Equal(Route[0], crossed.CurrentTarget);
        for (int i = 0; i < 6; i++) navigator.Step();
        var completed = navigator.Read();
        Assert.Equal("Completed", completed.Phase);
        Assert.Equal(15, completed.Moves);
        Assert.Equal(new[] { 11, 13, 15 }, completed.ReachedWaypoints.Select(x => x.Moves));
        Assert.DoesNotContain(new GridCell(2, 2), completed.Traveled);
    }

    [Fact]
    public void BlockedCurrentWaypointWaitsAndResumesWithoutSkipping()
    {
        var navigator = Mission();
        navigator.SetObstacle(Route[0], true);
        navigator.Start();
        for (int i = 0; i < 5; i++) Assert.False(navigator.Step());
        Assert.Equal("Waiting", navigator.Read().Phase);
        Assert.Equal(0, navigator.Read().Moves);
        Assert.Equal(0, navigator.Read().NextWaypointIndex);
        navigator.SetObstacle(Route[0], false);
        for (int i = 0; i < 9; i++) navigator.Step();
        Assert.Equal("Completed", navigator.Read().Phase);
    }

    [Fact]
    public void BlockedFutureWaypointDoesNotPreventReachingCurrentWaypoint()
    {
        var navigator = Mission();
        navigator.SetObstacle(Route[1], true);
        navigator.Start();
        Assert.Equal("Running", navigator.Read().Phase);
        for (int i = 0; i < 5; i++) Assert.True(navigator.Step());
        Assert.Equal("Waiting", navigator.Read().Phase);
        Assert.Equal(Route[0], navigator.Read().Current);
        Assert.Equal(Route[1], navigator.Read().CurrentTarget);
        Assert.Equal(1, navigator.Read().NextWaypointIndex);
        for (int i = 0; i < 5; i++) Assert.False(navigator.Step());
        navigator.SetObstacle(Route[1], false);
        for (int i = 0; i < 4; i++) navigator.Step();
        Assert.Equal("Completed", navigator.Read().Phase);
    }

    [Fact]
    public void ConsecutiveDuplicateWaypointsConsumeNoExtraMoves()
    {
        var navigator = new GridNavigator(new(0, 0), [new(0, 0), new(0, 1), new(0, 1), new(4, 5)]);
        navigator.Start();
        for (int i = 0; i < 9; i++) navigator.Step();
        Assert.Equal("Completed", navigator.Read().Phase);
        Assert.Equal(new[] { 0, 1, 1, 9 }, navigator.Read().ReachedWaypoints.Select(x => x.Moves));
    }

    [Fact]
    public void AllWaypointsAtStartCompleteWithoutMovement()
    {
        var navigator = new GridNavigator(new(0, 0), [new(0, 0), new(0, 0)]);
        Assert.True(navigator.Start());
        Assert.Equal("Completed", navigator.Read().Phase);
        Assert.Equal(0, navigator.Read().Moves);
        Assert.False(navigator.Step());
    }

    [Fact]
    public void ResetRestoresOrderedProgressAndDoesNotReuseVisits()
    {
        var navigator = Mission();
        navigator.Start();
        for (int i = 0; i < 6; i++) navigator.Step();
        navigator.Reset();
        var state = navigator.Read();
        Assert.Equal("Ready", state.Phase);
        Assert.Equal(0, state.NextWaypointIndex);
        Assert.Empty(state.ReachedWaypoints);
        Assert.Equal(Route[0], state.CurrentTarget);
        Assert.False(navigator.Step());
    }

    [Fact]
    public void RouteAndSnapshotsCannotMutateMission()
    {
        var source = Route.ToArray();
        var navigator = new GridNavigator(new(0, 0), source);
        source[0] = new(99, 99);
        navigator.Read().Waypoints[0] = new(99, 99);
        Assert.Equal(Route[0], navigator.Read().CurrentTarget);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(5, 0)]
    [InlineData(0, 2)]
    public void InvalidWaypointRejected(int row, int col) =>
        Assert.Throws<ArgumentException>(() => new GridNavigator(new(0, 0), [new(row, col)]));

    [Fact]
    public void EmptyMissionRejected() => Assert.Throws<ArgumentException>(() => new GridNavigator(new(0, 0), []));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissionConfigurationPreservesOrderAndAcceptsUtf8Bom(bool bom)
    {
        await WithFile("""{"amrId":"AMR_001","start":{"row":0,"col":0},"waypoints":[{"row":2,"col":3},{"row":4,"col":3},{"row":4,"col":5}]}""", async path =>
        {
            if (bom)
            {
                var original = await File.ReadAllBytesAsync(path);
                await File.WriteAllBytesAsync(path, new byte[] { 0xef, 0xbb, 0xbf }.Concat(original).ToArray());
            }
            var settings = await GridMissionSettings.LoadAsync(path);
            Assert.Equal("AMR_001", settings.AmrId);
            Assert.Equal(Route, settings.Waypoints);
        });
    }

    [Theory]
    [InlineData("{\"amrId\":\"AMR_001\",\"start\":{\"row\":0,\"col\":0},\"waypoints\":[]}")]
    [InlineData("{\"amrId\":\"AMR_001\",\"start\":{\"row\":0},\"waypoints\":[{\"row\":4,\"col\":5}]}")]
    [InlineData("{\"amrId\":\"bad/id\",\"start\":{\"row\":0,\"col\":0},\"waypoints\":[{\"row\":4,\"col\":5}]}")]
    [InlineData("{\"amrId\":\"AMR_001\",\"start\":{\"row\":0,\"col\":0},\"waypoints\":[{\"row\":0,\"col\":2}]}")]
    [InlineData("{\"amrId\":\"AMR_001\",\"amrId\":\"other\",\"start\":{\"row\":0,\"col\":0},\"waypoints\":[{\"row\":4,\"col\":5}]}")]
    [InlineData("{\"amrId\":\"AMR_001\",\"start\":{\"row\":\"0\",\"col\":0},\"waypoints\":[{\"row\":4,\"col\":5}]}")]
    [InlineData("{\"amrId\":\"AMR_001\",\"start\":{\"row\":0,\"col\":0},\"waypoints\":[{\"row\":4,\"col\":5}],\"unknown\":1}")]
    public async Task InvalidMissionConfigurationRejected(string json) =>
        await WithFile(json, path => Assert.ThrowsAsync<ArgumentException>(() => GridMissionSettings.LoadAsync(path)));

    [Fact]
    public async Task InvalidUtf8NotSilentlyReplaced()
    {
        await WithFile("{}", async path =>
        {
            await File.WriteAllBytesAsync(path, [0xef, 0xbb, 0xbf, 0xff]);
            await Assert.ThrowsAsync<DecoderFallbackException>(() => GridMissionSettings.LoadAsync(path));
        });
    }

    private static async Task WithFile(string json, Func<string, Task> action)
    {
        string path = Path.Combine(Path.GetTempPath(), "mission-config-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));
        try { await action(path); }
        finally { File.Delete(path); }
    }
}
