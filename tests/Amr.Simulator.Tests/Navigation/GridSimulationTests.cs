using System.Collections.Concurrent;
using System.Text.Json;
using Amr.Communication.Mqtt;
using Amr.Simulator.Navigation;
using Amr.Simulator.Telemetry;
using Xunit;

namespace Amr.Simulator.Tests;

public sealed class GridSimulationTests
{
    [Fact]
    public async Task FinalPublicationRetainsMissionSnapshotWhenResetOccursDuringNetworkWait()
    {
        var sender=new Sender();var simulation=Mission(sender);
        GridSimulationCompletion? completion=null;
        sender.OnPublication=publication=> {
            using var document=JsonDocument.Parse(publication.Payload);
            if(publication.Kind=="Telemetry" && document.RootElement.GetProperty("sp").GetDouble()==0 &&
                document.RootElement.GetProperty("pos").GetProperty("x").GetDouble()==5)
            { sender.OnPublication=null;simulation.Reset(); }
        };
        simulation.MissionCompleted+=value=>completion=value;
        using var stop=new CancellationTokenSource();
        var running=simulation.RunSessionAsync(Options,stop.Token);
        try
        {
            Assert.True(simulation.Start());await Wait(()=>completion is not null);
            Assert.Equal(5,completion!.X);Assert.Equal(-4,completion.Y);
            Assert.True(completion.Generation<simulation.MissionGeneration);Assert.Equal("Ready",simulation.Read().Phase);
        }
        finally{stop.Cancel();}
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>running);
    }
    [Fact]
    public async Task PersistentAmrPublishesHeartbeatBeforeStartAfterArrivalAndAfterMovementCancellation()
    {
        var sender=new Sender();var simulation=Mission(sender);
        int completed=0;simulation.MissionCompleted+=_=>Interlocked.Increment(ref completed);
        using var stop=new CancellationTokenSource();
        var running=simulation.RunSessionAsync(Options,stop.Token);
        int Heartbeats()=>sender.Publications.Count(x=>x.Kind=="Heartbeat");
        try
        {
            await Wait(()=>Heartbeats()>=2);Assert.Equal("Ready",simulation.Read().Phase);Assert.Equal(0,simulation.Read().Moves);
            Assert.True(simulation.Start());await Wait(()=>Volatile.Read(ref completed)==1);
            int count=Heartbeats();await Wait(()=>Heartbeats()>count+1);
            Assert.False(running.IsCompleted);Assert.False(sender.Disposed);Assert.Equal("Completed",simulation.Read().Phase);
            Assert.True(simulation.Start());await Wait(()=>simulation.Read().Moves>=1);simulation.Cancel();
            count=Heartbeats();await Wait(()=>Heartbeats()>count+1);Assert.False(running.IsCompleted);
            simulation.Reset();Assert.Equal("Ready",simulation.Read().Phase);
        }
        finally{stop.Cancel();}
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>running);Assert.True(sender.Disposed);
    }
    private static SimulatorMqttOptions Options => new(new MqttConnectionSettings(), 10, 20);

    [Fact]
    public async Task SimulatorAloneDetoursDuringMovementAndPublishesOrderedMission()
    {
        var sender = new Sender();
        var simulation = Mission(sender);
        simulation.Moved += () =>
        {
            if (simulation.Read().Moves == 1) simulation.SetObstacle(new(2, 2), true);
        };
        Assert.True(simulation.Start());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var final = await simulation.RunAsync(Options, timeout.Token);
        var state = simulation.Read();
        Assert.Equal("Completed", state.Phase);
        Assert.Equal(15, state.Moves);
        Assert.Equal(new[] { 11, 13, 15 }, state.ReachedWaypoints.Select(v => v.Moves));
        Assert.All(state.Traveled, cell => Assert.Equal(0, state.Map[cell.Row][cell.Col]));
        Assert.Equal(0, state.ReachedWaypoints[0].Index);
        Assert.Contains(new GridCell(4, 3), state.Traveled.Take(11));
        Assert.True(sender.Disposed);
        var points = sender.Publications.Where(p => p.Kind == "Telemetry").Select(p => JsonSerializer.Deserialize<JsonElement>(p.Payload)).ToArray();
        Assert.NotEmpty(points);
        Assert.All(points, p => Assert.Equal("AMR_001", p.GetProperty("id").GetString()));
        var last = points[^1];
        Assert.Equal(final!.Timestamp, last.GetProperty("ts").GetInt64());
        Assert.Equal(5, final.X); Assert.Equal(-4, final.Y);
        Assert.Equal(0, last.GetProperty("sp").GetDouble());
        Assert.Equal("IDLE", last.GetProperty("state").GetString());
    }

    [Fact]
    public async Task BlockedWaypointWaitsAndResumesWithoutSkippingIt()
    {
        var sender = new Sender();
        var simulation = Mission(sender);
        simulation.SetObstacle(new(4, 3), true);
        simulation.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = simulation.RunAsync(Options, timeout.Token);
        await Wait(() => simulation.Read().Phase == "Waiting" && simulation.Read().NextWaypointIndex == 1);
        var waiting = simulation.Read();
        int heartbeats = sender.Publications.Count(p => p.Kind == "Heartbeat");
        await Wait(() => sender.Publications.Count(p => p.Kind == "Heartbeat") >= heartbeats + 2);
        Assert.Equal(waiting.Current, simulation.Read().Current);
        simulation.SetObstacle(new(4, 3), false);
        await run;
        Assert.Equal(new[] { 0, 1, 2 }, simulation.Read().ReachedWaypoints.Select(v => v.Index));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SenderLossStopsAndDisposesSender(bool duringConnect)
    {
        var sender = new Sender { LoseOnConnect = duringConnect };
        var simulation = Mission(sender);
        simulation.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = simulation.RunAsync(Options, timeout.Token);
        if (!duringConnect)
        {
            await Wait(() => sender.Publications.Count > 1);
            sender.Lose();
        }
        var error = await Assert.ThrowsAsync<IOException>(async () => await run);
        Assert.Equal("SenderDisconnected", error.Message);
        Assert.Equal("Canceled", simulation.Read().Phase);
        if (duringConnect) Assert.Equal(0, simulation.Read().Moves);
        Assert.True(sender.Disposed);
    }

    [Fact]
    public async Task CancellationWaitsForSenderCleanupBeforeResetOrRestart()
    {
        var sender = new Sender { HoldDisconnect = true };
        var simulation = Mission(sender);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        simulation.Start();
        var run = simulation.RunAsync(Options, cancel.Token);
        try
        {
            await Wait(() => sender.Publications.Count > 1);
            cancel.Cancel();
            await Wait(() => sender.Disconnecting);
            Assert.False(run.IsCompleted);
            Assert.False(simulation.Start());
            Assert.Throws<InvalidOperationException>(simulation.Reset);
        }
        finally { sender.ReleaseDisconnect(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await run);
        Assert.True(sender.Disposed);
        simulation.Reset();
        Assert.Equal("Ready", simulation.Read().Phase);
        Assert.True(simulation.Start());
    }

    [Fact]
    public async Task PublishFailureStopsMovementAndCleansUp()
    {
        var sender = new Sender { PublishFailure = true };
        var simulation = Mission(sender);
        simulation.Start();
        await Assert.ThrowsAsync<IOException>(() => simulation.RunAsync(Options, CancellationToken.None));
        Assert.Equal(0, simulation.Read().Moves);
        Assert.Equal("Canceled", simulation.Read().Phase);
        Assert.True(sender.Disposed);
    }

    [Fact]
    public async Task LocalRunNeedsNoMapOrMqttAndCanRestart()
    {
        var simulation = new GridSimulation("local", new(new(0, 0), [new(0, 1), new(0, 2)], new int[1, 3]), 10);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        for (int run = 0; run < 2; run++)
        {
            Assert.True(simulation.Start());
            Assert.Null(await simulation.RunAsync(null, timeout.Token));
            Assert.Equal("Completed", simulation.Read().Phase);
            Assert.Equal(2, simulation.Read().Moves);
        }
    }

    private static GridSimulation Mission(Sender sender) => new("AMR_001",
        new(new(0, 0), [new(2, 3), new(4, 3), new(4, 5)]), 20, () => sender);

    private static async Task Wait(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }

    private sealed class Sender : ISimulationTransport
    {
        public event Action? ConnectionLost;
        public ConcurrentQueue<SimulationPublication> Publications { get; } = new();
        public bool LoseOnConnect { get; init; }
        public bool HoldDisconnect { get; init; }
        public bool PublishFailure { get; init; }
        public Action<SimulationPublication>? OnPublication { get; set; }
        public volatile bool Disconnecting, Disposed;
        private readonly TaskCompletionSource _disconnect = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ConnectAsync(CancellationToken token)
        {
            if (LoseOnConnect) Lose();
            return Task.CompletedTask;
        }
        public Task PublishAsync(SimulationPublication publication, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (PublishFailure) throw new IOException("PublishFailure");
            Publications.Enqueue(publication);
            OnPublication?.Invoke(publication);
            return Task.CompletedTask;
        }
        public void Lose() => ConnectionLost?.Invoke();
        public void ReleaseDisconnect() => _disconnect.TrySetResult();
        public async Task DisconnectAsync()
        {
            Disconnecting = true;
            if (HoldDisconnect) await _disconnect.Task;
        }
        public void Dispose() => Disposed = true;
    }
}
