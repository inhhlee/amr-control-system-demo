using System.Text.Json;
using Amr.Simulator.Configuration;
using Amr.Simulator.Simulation;
using Xunit;

namespace Amr.Simulator.Tests;

public sealed class MovementTests
{
    private const double CoordinateTolerance = 0.000001;

    private static SimulatedAmr Create(Position2D initial, double speed, params Position2D[] route)
    {
        string json = JsonSerializer.Serialize(new
        {
            amrId = "AMR_001",
            initialPosition = new { x = initial.X, y = initial.Y },
            speedMps = speed,
            updateIntervalMs = 100,
            waypoints = route.Select(point => new { x = point.X, y = point.Y })
        });
        return new SimulatedAmr(SimulatorSettingsLoader.Parse(json));
    }

    private static void AssertPosition(Position2D expected, Position2D actual)
    {
        Assert.True(double.IsFinite(actual.X) && double.IsFinite(actual.Y));
        Assert.InRange(Math.Abs(actual.X - expected.X), 0, CoordinateTolerance);
        Assert.InRange(Math.Abs(actual.Y - expected.Y), 0, CoordinateTolerance);
    }

    private static (Position2D, string, double, int, bool) Snapshot(SimulatedAmr amr) =>
        (amr.Position, amr.State, amr.CurrentSpeedMps, amr.NextWaypointIndex, amr.IsCompleted);

    [Theory]
    [InlineData(0, 0, 1, 0, 1, 0.5, 0.5, 0)]
    [InlineData(0, 0, 0, -2, 2, 0.5, 0, -1)]
    [InlineData(-2, 3, 2, 3, 2, 0.5, -1, 3)]
    [InlineData(0, 0, 3, 4, 1, 2.5, 1.5, 2)]
    [InlineData(3, 4, 0, 0, 1, 2.5, 1.5, 2)]
    [InlineData(-3, -4, 0, 0, 2, 1.25, -1.5, -2)]
    public void ElapsedTime_ProducesStraightLinePositionAndBusyState(double x, double y,
        double targetX, double targetY, double speed, double seconds, double expectedX, double expectedY)
    {
        var amr = Create(new Position2D(x, y), speed, new Position2D(targetX, targetY));
        Assert.Equal("IDLE", amr.State);
        Assert.Equal(0, amr.CurrentSpeedMps);
        Assert.False(amr.IsCompleted);
        amr.Advance(seconds);
        AssertPosition(new Position2D(expectedX, expectedY), amr.Position);
        Assert.Equal("BUSY", amr.State);
        Assert.Equal(speed, amr.CurrentSpeedMps);
        Assert.Equal(speed, amr.Settings.SpeedMps);
        Assert.False(amr.IsCompleted);
    }

    [Fact]
    public void RemainingTime_IsAppliedToNextSegmentThenStopsAtFinalWaypoint()
    {
        var amr = Create(new Position2D(0, 0), 1, new Position2D(1, 0), new Position2D(1, 1));
        amr.Advance(1.5);
        AssertPosition(new Position2D(1, 0.5), amr.Position);
        Assert.Equal(1, amr.NextWaypointIndex);
        Assert.Equal("BUSY", amr.State);
        amr.Advance(0.5);
        Assert.Equal(new Position2D(1, 1), amr.Position);
        Assert.True(amr.IsCompleted);
        Assert.Equal(2, amr.NextWaypointIndex);
        Assert.Equal("IDLE", amr.State);
        Assert.Equal(0, amr.CurrentSpeedMps);
        Assert.Equal(1, amr.Settings.SpeedMps);
    }

    [Fact]
    public void ExactIntermediateArrival_RemainsBusyUntilFinalArrival()
    {
        var amr = Create(new Position2D(0, 0), 1, new Position2D(1, 0), new Position2D(1, 1));
        amr.Advance(1);
        Assert.Equal(new Position2D(1, 0), amr.Position);
        Assert.Equal(1, amr.NextWaypointIndex);
        Assert.False(amr.IsCompleted);
        Assert.Equal("BUSY", amr.State);
        Assert.Equal(1, amr.CurrentSpeedMps);
    }

    [Fact]
    public void OneUpdate_CrossesMultipleWaypointsWithoutReordering()
    {
        var amr = Create(new Position2D(0, 0), 1,
            new Position2D(1, 0), new Position2D(1, 1), new Position2D(2, 1));
        amr.Advance(2.5);
        AssertPosition(new Position2D(1.5, 1), amr.Position);
        Assert.Equal(2, amr.NextWaypointIndex);
        Assert.Equal("BUSY", amr.State);
        amr.Advance(100);
        Assert.Equal(new Position2D(2, 1), amr.Position);
        Assert.True(amr.IsCompleted);
        Assert.Equal("IDLE", amr.State);
        Assert.Equal(0, amr.CurrentSpeedMps);
    }

    [Fact]
    public void CoincidentWaypoints_ConsumeNoTimeIncludingTrailingDuplicates()
    {
        var amr = Create(new Position2D(0, 0), 1,
            new Position2D(0, 0), new Position2D(0, 0), new Position2D(1, 0),
            new Position2D(1, 0), new Position2D(1, 1), new Position2D(1, 1));
        Assert.Equal(2, amr.NextWaypointIndex);
        amr.Advance(1.5);
        AssertPosition(new Position2D(1, 0.5), amr.Position);
        Assert.Equal(4, amr.NextWaypointIndex);
        amr.Advance(0.5);
        Assert.Equal(new Position2D(1, 1), amr.Position);
        Assert.Equal(6, amr.NextWaypointIndex);
        Assert.True(amr.IsCompleted);
        Assert.Equal("IDLE", amr.State);
        Assert.Equal(0, amr.CurrentSpeedMps);
    }

    [Fact]
    public void AllWaypointsAtInitialPosition_CompleteWithoutBecomingBusy()
    {
        var initial = new Position2D(-2, 3);
        var amr = Create(initial, 2.5, Enumerable.Repeat(initial, 1000).ToArray());
        Assert.True(amr.IsCompleted);
        Assert.Equal("IDLE", amr.State);
        Assert.Equal(0, amr.CurrentSpeedMps);
        var before = Snapshot(amr);
        amr.Advance(100);
        Assert.Equal(before, Snapshot(amr));
        Assert.Equal(2.5, amr.Settings.SpeedMps);
    }

    [Fact]
    public void FinalArrival_IsExactAndFurtherUpdatesPreserveTheCompletedState()
    {
        var target = new Position2D(-2, -3);
        var amr = Create(new Position2D(-1, -3), 2, target);
        amr.Advance(0.5);
        Assert.Equal(target, amr.Position);
        Assert.True(amr.IsCompleted);
        Assert.Equal("IDLE", amr.State);
        Assert.Equal(0, amr.CurrentSpeedMps);
        var before = Snapshot(amr);
        amr.Advance(double.MaxValue);
        Assert.Equal(before, Snapshot(amr));
        Assert.Equal(2, amr.Settings.SpeedMps);
    }

    [Fact]
    public void CalculationTolerance_IsNotUsedAsAnArrivalRadius()
    {
        var amr = Create(new Position2D(0, 0), 1, new Position2D(1, 0));
        amr.Advance(1 - CoordinateTolerance * 0.5);
        Assert.InRange(1 - amr.Position.X, 0, CoordinateTolerance);
        Assert.False(amr.IsCompleted);
        Assert.Equal("BUSY", amr.State);
        amr.Advance(CoordinateTolerance);
        Assert.Equal(new Position2D(1, 0), amr.Position);
        Assert.True(amr.IsCompleted);
    }

    [Fact]
    public void RoundedIntermediateWaypoint_DoesNotCorruptTheNextUpdate()
    {
        double nearby = Math.BitIncrement(1d);
        var amr = Create(new Position2D(1, 0), 1,
            new Position2D(nearby, 0), new Position2D(2, 0));
        // 부분 이동의 반올림 결과가 목표와 같아지는 표현 한계도 동일 위치 규칙으로 처리한다.
        amr.Advance((nearby - 1) * 0.75);
        Assert.Equal(new Position2D(nearby, 0), amr.Position);
        amr.Advance(0.25);
        AssertPosition(new Position2D(1.25, 0), amr.Position);
        Assert.False(amr.IsCompleted);
        Assert.Equal(1, amr.NextWaypointIndex);
        Assert.Equal("BUSY", amr.State);
    }

    private static SimulatedAmr AtPhase(int phase)
    {
        var amr = Create(new Position2D(0, 0), 1, new Position2D(1, 0));
        if (phase == 1) amr.Advance(0.5);
        if (phase == 2) amr.Advance(1);
        return amr;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ZeroElapsed_PreservesInitialMovingAndCompletedState(int phase)
    {
        var amr = AtPhase(phase);
        var before = Snapshot(amr);
        amr.Advance(0);
        amr.Advance(-0d);
        Assert.Equal(before, Snapshot(amr));
    }

    public static IEnumerable<object[]> InvalidElapsedTimes()
    {
        foreach (int phase in new[] { 0, 1, 2 })
            foreach (double seconds in new[] { -0.1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                yield return [phase, seconds];
    }

    [Theory]
    [MemberData(nameof(InvalidElapsedTimes))]
    public void InvalidElapsed_IsRejectedWithParameterAndReasonWithoutChangingState(int phase, double seconds)
    {
        var amr = AtPhase(phase);
        var before = Snapshot(amr);
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => amr.Advance(seconds));
        Assert.Equal("elapsedSeconds", error.ParamName);
        Assert.Contains("0 이상의 유한한 숫자", error.Message);
        Assert.Equal(before, Snapshot(amr));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(1000)]
    public void TimePartitioning_PreservesRouteProgressWithinCoordinateTolerance(int steps)
    {
        Position2D[] route = [new(3, 4), new(-3, 4), new(-3, -2)];
        var one = Create(new Position2D(0, 0), 2, route);
        var many = Create(new Position2D(0, 0), 2, route);
        one.Advance(4);
        for (int i = 0; i < steps; i++) many.Advance(4d / steps);
        AssertPosition(new Position2D(0, 4), one.Position);
        AssertPosition(new Position2D(0, 4), many.Position);
        Assert.Equal(1, many.NextWaypointIndex);
        Assert.Equal(one.State, many.State);
        Assert.Equal(one.CurrentSpeedMps, many.CurrentSpeedMps);
    }

    [Fact]
    public void UpdatingOneAmr_DoesNotMutateSharedSettingsOrAnotherAmr()
    {
        var first = Create(new Position2D(-1, 0), 2, new Position2D(1, 0), new Position2D(1, 2));
        var settings = first.Settings;
        var second = new SimulatedAmr(settings);
        var route = settings.Waypoints.ToArray();
        var otherBefore = Snapshot(second);
        first.Advance(100);
        Assert.Equal(otherBefore, Snapshot(second));
        Assert.Same(settings, first.Settings);
        Assert.Equal(route, settings.Waypoints);
        Assert.Equal(new Position2D(-1, 0), settings.InitialPosition);
        Assert.Equal(2, settings.SpeedMps);
        Assert.Equal(100, settings.UpdateIntervalMs);
    }

    [Theory]
    [InlineData(1e200)]
    [InlineData(1e-200)]
    public void FiniteLargeAndSmallSegments_DoNotOverflowOrCollapseTheirDistance(double scale)
    {
        var target = new Position2D(3 * scale, 4 * scale);
        var amr = Create(new Position2D(0, 0), scale, target);
        amr.Advance(2.5);
        // 수치 범위 보호 시험. 일반 AMR 좌표의 1e-6m 비교와 별도로 정규화해 확인한다.
        Assert.InRange(Math.Abs(amr.Position.X / scale - 1.5), 0, 1e-12);
        Assert.InRange(Math.Abs(amr.Position.Y / scale - 2), 0, 1e-12);
        Assert.False(amr.IsCompleted);
        amr.Advance(3);
        Assert.Equal(target, amr.Position);
        Assert.True(amr.IsCompleted);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1.5, 0.5)]
    public void OppositeFiniteExtremes_AllowPartialProgressEvenIfDistanceOrTravelWouldOverflow(
        double seconds, double expectedFractionOfMax)
    {
        var amr = Create(new Position2D(-double.MaxValue, 0), double.MaxValue,
            new Position2D(double.MaxValue, 0));
        amr.Advance(seconds);
        Assert.True(double.IsFinite(amr.Position.X));
        Assert.InRange(Math.Abs(amr.Position.X / double.MaxValue - expectedFractionOfMax), 0, 1e-12);
        Assert.False(amr.IsCompleted);
        Assert.Equal("BUSY", amr.State);
        amr.Advance(2);
        Assert.Equal(new Position2D(double.MaxValue, 0), amr.Position);
        Assert.True(amr.IsCompleted);
    }

    [Fact]
    public void DiagonalWithUnrepresentableTotalDistance_StillMakesFiniteProgress()
    {
        var target = new Position2D(double.MaxValue, double.MaxValue);
        var amr = Create(new Position2D(0, 0), double.MaxValue, target);
        amr.Advance(0.5);
        double expected = 0.5 / Math.Sqrt(2);
        Assert.InRange(Math.Abs(amr.Position.X / double.MaxValue - expected), 0, 1e-12);
        Assert.InRange(Math.Abs(amr.Position.Y / double.MaxValue - expected), 0, 1e-12);
        Assert.False(amr.IsCompleted);
        amr.Advance(2);
        Assert.Equal(target, amr.Position);
        Assert.True(amr.IsCompleted);
    }

    [Fact]
    public void SubnormalDistanceAndSpeed_PreserveRepresentableProgress()
    {
        var amr = Create(new Position2D(0, 0), double.Epsilon, new Position2D(2 * double.Epsilon, 0));
        amr.Advance(1);
        Assert.Equal(double.Epsilon, amr.Position.X);
        Assert.False(amr.IsCompleted);
        amr.Advance(1);
        Assert.True(amr.IsCompleted);
    }

    [Fact]
    public void TravelTimeBelowSmallestPositiveDouble_StillCompletesWithoutLooping()
    {
        var amr = Create(new Position2D(0, 0), double.MaxValue, new Position2D(double.Epsilon, 0));
        amr.Advance(double.Epsilon);
        Assert.Equal(new Position2D(double.Epsilon, 0), amr.Position);
        Assert.True(amr.IsCompleted);
        Assert.Equal("IDLE", amr.State);
    }

    [Fact]
    public void UnrepresentablySmallFraction_DoesNotJumpToTarget()
    {
        var amr = Create(new Position2D(0, 0), double.Epsilon, new Position2D(double.MaxValue, 0));
        amr.Advance(1);
        Assert.True(double.IsFinite(amr.Position.X));
        Assert.False(amr.IsCompleted);
        Assert.Equal("BUSY", amr.State);
    }
}
