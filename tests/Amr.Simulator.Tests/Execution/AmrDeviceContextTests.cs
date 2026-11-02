using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Amr.Simulator.Configuration;
using Amr.Simulator.Simulation;
using Amr.Simulator.Telemetry;
using Xunit;

namespace Amr.Simulator.Tests;

public sealed class AmrDeviceContextTests
{
    private static ConfiguredAmr Device(string id, AmrOperationMode mode = AmrOperationMode.Command,
        params FaultScenario[] faults) => new(SimulatorSettingsLoader.Parse(JsonSerializer.Serialize(new
        { amrId = id, initialPosition = new { x = 4, y = 2 }, speedMps = 1, updateIntervalMs = 100,
            waypoints = new[] { new { x = 6, y = 2 }, new { x = 6, y = 4 } } })), faults, mode);

    [Fact]
    public async Task FaultAndResetOfOneDevice_DoNotAffectOtherDeviceWithSameCommandId()
    {
        var a = new Transport(); var b = new Transport(); var ac = new StepClock(); var bc = new StepClock();
        await using var da = Context(Device("A", AmrOperationMode.Command, new FaultScenario("F", "ERR", 1000, 3000, null)), a, ac);
        await using var db = Context(Device("B"), b, bc);
        var ar = da.RunAsync(); var br = db.RunAsync();
        await Ready(a, b);
        a.Send("A", "SAME", "MOVE", 100, 2); b.Send("B", "SAME", "MOVE", 100, 2);
        ac.Step(100); bc.Step(100);
        await Until(() => State(a, "BUSY") && State(b, "BUSY"));
        ac.Step(1000); bc.Step(1100);
        await Until(() => Ack(a, "SAME", "FAULT_INTERRUPTED") && X(b) > 4);
        double faultX = X(a);
        Assert.Equal("FAULT", LastTelemetry(a).GetProperty("state").GetString());
        Assert.Equal(0, LastTelemetry(a).GetProperty("sp").GetDouble());
        Assert.Single(a.OfKind("alarm")); Assert.Empty(b.OfKind("alarm"));
        Assert.False(Ack(b, "SAME", "FAULT_INTERRUPTED"));
        a.Send("A", "EARLY", "RESET"); ac.Step(100);
        await Until(() => Ack(a, "EARLY", "FAULT_CAUSE_ACTIVE"));
        ac.Step(2000); bc.Step(1000);
        await Until(() => a.OfKind("heartbeat").Count() >= 3);
        Assert.Equal(faultX, X(a)); Assert.Equal("FAULT", LastTelemetry(a).GetProperty("state").GetString());
        a.Send("A", "RECOVER", "RESET"); ac.Step(100);
        await Until(() => Ack(a, "RECOVER", "RESET_COMPLETED"));
        Assert.Equal(faultX, X(a)); Assert.Equal("IDLE", LastTelemetry(a).GetProperty("state").GetString());
        Assert.Equal(new[] { "ACTIVE", "CLEARED" }, a.OfKind("alarm").Select(p => p.GetProperty("alarmState").GetString()));
        Assert.Equal("BUSY", LastTelemetry(b).GetProperty("state").GetString());
        b.Send("B", "STOP-B", "STOP"); bc.Step(100);
        await Until(() => Ack(b, "SAME", "STOP_INTERRUPTED") && Ack(b, "STOP-B", "STOPPED"));
        Assert.DoesNotContain(a.OfKind("ack"), p => p.GetProperty("cmdId").GetString() == "STOP-B");
        da.RequestStop(); db.RequestStop();
        Assert.Equal(new[] { 0, 0 }, await Task.WhenAll(ar, br).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(a.Unsubscribed && b.Unsubscribed && a.Disposed && b.Disposed);
        Assert.Equal(0, a.SubscriberCount + b.SubscriberCount);
    }

    [Fact]
    public async Task RouteUsesAllWaypointsWithoutSyntheticAck_AndCanReceiveCommandsAfterArrival()
    {
        var t = new Transport(); var c = new StepClock();
        await using var device = Context(Device("A", AmrOperationMode.Route), t, c);
        var run = device.RunAsync(); await Ready(t);
        c.Step(2000); await Until(() => X(t) == 6);
        Assert.Equal(2, LastTelemetry(t).GetProperty("pos").GetProperty("y").GetDouble());
        c.Step(2000); await Until(() => LastTelemetry(t).GetProperty("pos").GetProperty("y").GetDouble() == 4);
        Assert.Equal("IDLE", LastTelemetry(t).GetProperty("state").GetString());
        Assert.Empty(t.OfKind("ack")); Assert.False(run.IsCompleted);
        t.Send("A", "AFTER", "MOVE", 7, 4); c.Step(100);
        await Until(() => LastTelemetry(t).GetProperty("state").GetString() == "BUSY");
        c.Step(1000); await Until(() => Ack(t, "AFTER", "ARRIVED"));
        device.RequestStop(); Assert.Equal(0, await run);
    }

    [Fact]
    public async Task RouteFaultReset_DoesNotResumeConfiguredRouteOrInventMoveAck()
    {
        var t = new Transport(); var c = new StepClock();
        await using var d = Context(Device("A", AmrOperationMode.Route, new FaultScenario("F", "ERR", 500, 1500, null)), t, c);
        var run = d.RunAsync(); await Ready(t);
        c.Step(2000); await Until(() => State(t, "FAULT"));
        Assert.Equal(4.5, X(t)); Assert.Empty(t.OfKind("ack"));
        t.Send("A", "R", "RESET"); c.Step(100);
        await Until(() => Ack(t, "R", "RESET_COMPLETED"));
        c.Step(5000); await Until(() => t.OfKind("heartbeat").Count() >= 3);
        Assert.Equal(4.5, X(t)); Assert.Equal("IDLE", LastTelemetry(t).GetProperty("state").GetString());
        d.RequestStop(); Assert.Equal(0, await run);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeviceDisconnectOrQueueOverflow_DoesNotCancelHealthySibling(bool overflow)
    {
        var a = new Transport(); var b = new Transport(); var ac = new StepClock(); var bc = new StepClock();
        await using var da = Context(Device("A"), a, ac, 2);
        await using var db = Context(Device("B"), b, bc);
        var ar = da.RunAsync(); var br = db.RunAsync(); await Ready(a, b);
        if (overflow) for (int i = 0; i < 3; i++) a.Send("A", $"Q{i}", "STOP");
        else a.LoseConnection();
        Assert.Equal(1, await ar.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(br.IsCompleted); Assert.False(b.Disposed);
        b.Send("B", "ALIVE", "MOVE", 4, 2); bc.Step(100);
        await Until(() => Ack(b, "ALIVE", "ARRIVED"));
        db.RequestStop(); Assert.Equal(0, await br);
    }

    [Fact]
    public async Task BlockedPublisherDoesNotBlockSibling_AndCancellationJoinsBoth()
    {
        var a = new Transport { BlockPublish = true }; var b = new Transport();
        await using var da = Context(Device("A"), a, new StepClock());
        var bc = new StepClock(); await using var db = Context(Device("B"), b, bc);
        using var stop = new CancellationTokenSource();
        var ar = da.RunAsync(stop.Token); var br = db.RunAsync(stop.Token);
        await a.PublishEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); await Ready(b);
        b.Send("B", "M", "MOVE", 4, 2); bc.Step(100);
        await Until(() => Ack(b, "M", "ARRIVED"));
        stop.Cancel(); Assert.Equal(new[] { 0, 0 }, await Task.WhenAll(ar, br).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(a.Disposed && b.Disposed);
    }

    [Fact]
    public async Task ForeignAndRetainedCommandsNeverEnterDomain_ConcurrentCallbacksAreSerialized()
    {
        var t = new Transport(); var c = new StepClock();
        await using var d = Context(Device("A"), t, c);
        var run = d.RunAsync(); await Ready(t);
        t.Send("B", "FOREIGN", "MOVE", 100, 2);
        t.Send("A", "RETAINED", "MOVE", 100, 2, true);
        await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Task.Run(() => t.Send("A", $"S{i}", "STOP"))));
        c.Step(100);
        await Until(() => t.OfKind("ack").Count() == 32);
        Assert.All(t.OfKind("ack"), p => Assert.StartsWith("S", p.GetProperty("cmdId").GetString()));
        Assert.Equal(4, X(t));
        d.RequestStop(); Assert.Equal(0, await run);
    }

    [Fact]
    public async Task FaultTimeStartsAfterEachDevicesSubscriptionIndependently()
    {
        var a = new Transport(); var b = new Transport { DelaySubscribe = true };
        var ac = new StepClock(); var bc = new StepClock();
        var fault = new FaultScenario("F", "ERR", 1000, 3000, null);
        await using var da = Context(Device("A", AmrOperationMode.Command, fault), a, ac);
        await using var db = Context(Device("B", AmrOperationMode.Command, fault), b, bc);
        var ar = da.RunAsync(); var br = db.RunAsync(); await Ready(a);
        ac.Step(1500); bc.SetBeforeStart(10000);
        await Until(() => State(a, "FAULT"));
        b.AllowSubscribe.TrySetResult(); await Ready(b);
        bc.Step(500); await Until(() => b.OfKind("telemetry").Count() >= 2);
        Assert.Empty(b.OfKind("alarm"));
        bc.Step(500); await Until(() => State(b, "FAULT"));
        da.RequestStop(); db.RequestStop(); await Task.WhenAll(ar, br);
    }

    [Fact]
    public async Task CallbackAlreadyCapturedBeforeUnsubscribe_CannotTurnNormalShutdownIntoFailure()
    {
        var t = new Transport();
        await using var device = Context(Device("A"), t, new StepClock());
        var run = device.RunAsync(); await Ready(t);
        t.CaptureLateDisconnect();
        device.RequestStop();
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(t.Disposed);
        Assert.Equal(0, t.SubscriberCount);
    }

    [Fact]
    public void FleetMqttDefaultsDoNotRelaxSingleModeOrAllowCommandModeOffline()
    {
        var (_, options) = SimulatorMqttOptions.Parse(["--config", "f.json", "--mqtt"], useFleetDefaults: true);
        Assert.NotNull(options); Assert.Equal(1884, options.Connection.Port);
        Assert.Equal(200, options.TelemetryMs); Assert.Equal(1000, options.HeartbeatMs);
        Assert.Throws<ArgumentException>(() => SimulatorMqttOptions.Parse(["--config", "f.json", "--mqtt"]));
        Assert.Throws<ArgumentException>(() => new AmrDeviceContext(Device("A"), null, TextWriter.Null, TextWriter.Null));
    }

    private static AmrDeviceContext Context(ConfiguredAmr d, Transport t, StepClock clock, int capacity = 128) =>
        new(d, new(new(), 100, 1000), TextWriter.Null, TextWriter.Null, clock, t, capacity);
    private static async Task Ready(params Transport[] transports) =>
        await Until(() => transports.All(t => t.OfKind("heartbeat").Any()));
    private static bool Ack(Transport t, string id, string reason) => t.OfKind("ack").Any(p =>
        p.GetProperty("cmdId").GetString() == id && p.GetProperty("reason").GetString() == reason);
    private static bool State(Transport t, string state) => t.OfKind("telemetry").Any(p => p.GetProperty("state").GetString() == state);
    private static JsonElement LastTelemetry(Transport t) => t.OfKind("telemetry").Last();
    private static double X(Transport t) => LastTelemetry(t).GetProperty("pos").GetProperty("x").GetDouble();
    private static async Task Until(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(5, deadline.Token);
    }

    private sealed class StepClock : ISimulationClock
    {
        private long _now;
        private readonly Channel<long> _steps = Channel.CreateUnbounded<long>();
        public void Step(long ms) => _steps.Writer.TryWrite(ms);
        public void SetBeforeStart(long ms) => Interlocked.Exchange(ref _now, ms);
        public long GetTimestamp() => Interlocked.Read(ref _now);
        public double GetElapsedSeconds(long start, long end) => (end - start) / 1000d;
        public async Task DelayAsync(long ms, CancellationToken token) => Interlocked.Add(ref _now, await _steps.Reader.ReadAsync(token));
    }

    private sealed class Transport : IAmrDeviceTransport
    {
        public event Action? ConnectionLost;
        public event DeviceCommandReceived? CommandReceived;
        private Action? _lateDisconnect;
        public void CaptureLateDisconnect() => _lateDisconnect = ConnectionLost;
        public int SubscriberCount => (CommandReceived?.GetInvocationList().Length ?? 0) + (ConnectionLost?.GetInvocationList().Length ?? 0);
        public ConcurrentQueue<SimulationPublication> Messages { get; } = new();
        public bool Disposed { get; private set; }
        public bool Unsubscribed { get; private set; }
        public bool BlockPublish { get; init; }
        public bool DelaySubscribe { get; init; }
        public TaskCompletionSource AllowSubscribe { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PublishEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IEnumerable<JsonElement> OfKind(string kind) => Messages.Where(m => m.Kind == kind).Select(m =>
        { using var doc = JsonDocument.Parse(m.Payload); return doc.RootElement.Clone(); });
        public void Send(string device, string id, string type, double? x = null, double? y = null, bool retained = false) =>
            CommandReceived?.Invoke($"amr/{device}/command", new ReadOnlySequence<byte>(JsonSerializer.SerializeToUtf8Bytes(
                new { cmdId = id, type, ts = 1, param = x is null ? (object)new { } : new { x, y } })), retained);
        public void LoseConnection() => ConnectionLost?.Invoke();
        public Task ConnectAsync(CancellationToken token) => Task.CompletedTask;
        public Task SubscribeAsync(CancellationToken token) => DelaySubscribe ? AllowSubscribe.Task.WaitAsync(token) : Task.CompletedTask;
        public Task UnsubscribeAsync(CancellationToken token) { Unsubscribed = true; _lateDisconnect?.Invoke(); return Task.CompletedTask; }
        public async Task PublishAsync(SimulationPublication message, CancellationToken token)
        {
            PublishEntered.TrySetResult();
            if (BlockPublish) await Task.Delay(Timeout.Infinite, token);
            Messages.Enqueue(message);
        }
        public Task DisconnectAsync() => Task.CompletedTask;
        public void Dispose() => Disposed = true;
    }
}
