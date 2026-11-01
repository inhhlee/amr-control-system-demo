using Amr.Simulator.Navigation;
using System.Text.Json;
using Amr.Communication.Models;
using Amr.Communication.Mqtt;
using Amr.Simulator.Map.Control;
using Amr.Simulator.Telemetry;
using Xunit;

namespace Amr.Simulator.Map.Tests;

public sealed class GridMqttTests
{
    [Fact]
    public async Task AmrSenderFailureKeepsIndependentControlConnectionUntilBrokerLoss()
    {
        await using var fixture=new Fixture(new FakeConnection { PublishFailure=true });
        fixture.Session.Connect();await Wait(()=>Phase(fixture.Session)=="AmrUnavailable");
        Assert.Equal("Subscribed",Read(fixture.Session).GetProperty("brokerState").GetString());
        Assert.False(fixture.Connection.Disposed);Assert.False(fixture.Session.Start());
        fixture.Connection.Lose();await Wait(()=>Phase(fixture.Session)=="Failed");
        Assert.True(fixture.Connection.Disposed);
    }

    [Fact]
    public async Task ResetDuringFinalPublicationIgnoresPreviousMissionCompletion()
    {
        await using var fixture=new Fixture();
        int reset=0;
        fixture.Connection.Publishing=publication=> {
            if(publication.Kind!="Telemetry")return;
            using var document=JsonDocument.Parse(publication.Payload);
            var payload=document.RootElement;
            if(payload.GetProperty("pos").GetProperty("x").GetDouble()==5 && payload.GetProperty("sp").GetDouble()==0 && Interlocked.Exchange(ref reset,1)==0)
                fixture.Session.Reset();
        };
        fixture.Session.Start();await Wait(()=>Volatile.Read(ref reset)==1,8000);
        await Wait(()=>Phase(fixture.Session)=="Ready" && Read(fixture.Session).GetProperty("position").GetProperty("col").GetInt32()==0);
        Assert.False(Read(fixture.Session).GetProperty("missionFinalReceived").GetBoolean());
        Assert.Equal(JsonValueKind.Null,Read(fixture.Session).GetProperty("missionFinalTelemetry").ValueKind);
        Assert.False(fixture.Connection.Disposed);
    }
    [Fact]
    public async Task LocalResetDuringMovementWaitsForExecutionAndAllowsRestart()
    {
        string directory=Path.Combine(Path.GetTempPath(),"local-reset-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var session=new GridMapSession(directory,navigator:new(new(0,0),[new(0,2)],new int[1,3]),stepIntervalMs:30);
            Assert.True(session.Start());await Wait(()=>Read(session).GetProperty("navigation").GetProperty("moves").GetInt32()>0);
            session.Reset();await Wait(()=>Phase(session)=="Ready");
            Assert.Equal(0,Read(session).GetProperty("navigation").GetProperty("moves").GetInt32());
            Assert.True(session.Start());await Wait(()=>Phase(session)=="Completed");
        }
        finally{Directory.Delete(directory,true);}
    }
    private static SimulatorMqttOptions Options => new(new MqttConnectionSettings(), 50, 80);
    private static JsonElement Read(GridMapSession session) => JsonSerializer.SerializeToElement(session.Read(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static string Phase(GridMapSession session) => Read(session).GetProperty("navigation").GetProperty("phase").GetString()!;

    [Fact]
    public async Task ReadyHasNoReceivedPosition()
    {
        await using var fixture = new Fixture();
        var state = Read(fixture.Session);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("position").ValueKind);
        Assert.Equal("mqtt-grid-control-receiver", state.GetProperty("source").GetString());
    }

    [Fact]
    public void GridCoordinateUsesOneMeterCellsAndUpwardPositiveY()
    {
        var navigator = new GridNavigator();
        navigator.Start(); navigator.Step(); navigator.Step();
        var snapshot = GridTelemetry.Capture("robot", navigator.Read());
        Assert.Equal(1, snapshot.X); Assert.Equal(-1, snapshot.Y);
        Assert.Equal("BUSY", snapshot.State);
        Assert.Equal(1 / 0.6, snapshot.Speed, 10);
        var received = new TelemetryMessage("robot", 1, new(snapshot.X, snapshot.Y), snapshot.Speed, snapshot.State, DateTimeOffset.UtcNow);
        Assert.Equal(navigator.Read().Current, GridMapCoordinates.ReceivedCell(received));
    }

    [Theory]
    [InlineData(0.5, 0)]
    [InlineData(0, -0.5)]
    [InlineData(-1, 0)]
    [InlineData(6, 0)]
    [InlineData(0, -5)]
    [InlineData(0, 1)]
    public void InvalidGridCoordinateIsNotDisplayed(double x, double y) =>
        Assert.Null(GridMapCoordinates.ReceivedCell(new("r", 1, new(x, y), 0, "IDLE", DateTimeOffset.UtcNow)));

    [Fact]
    public async Task PublishedPositionNeverFillsReceivedPosition()
    {
        await using var fixture = new Fixture(new FakeConnection { Echo = false });
        fixture.Session.Start();
        await Wait(() => Read(fixture.Session).GetProperty("publishedCount").GetInt32() > 0);
        Assert.Equal(JsonValueKind.Null, Read(fixture.Session).GetProperty("position").ValueKind);
        fixture.Connection.Inject(new TelemetryMessage(fixture.Session.AmrId, 10, new(2, -2), 0, "IDLE", DateTimeOffset.UtcNow));
        var state = Read(fixture.Session);
        Assert.Equal(2, state.GetProperty("position").GetProperty("row").GetInt32());
        Assert.Equal(2, state.GetProperty("position").GetProperty("col").GetInt32());
        Assert.Equal(0, state.GetProperty("navigation").GetProperty("current").GetProperty("row").GetInt32());
    }

    [Fact]
    public async Task HeartbeatAndRejectedTelemetryDoNotMoveMarker()
    {
        await using var fixture = new Fixture(new FakeConnection { Echo = false });
        fixture.Session.Start();
        await Wait(() => Read(fixture.Session).GetProperty("publishedCount").GetInt32() > 0);
        fixture.Connection.Inject(new HeartbeatMessage(fixture.Session.AmrId, 1, "OK", DateTimeOffset.UtcNow));
        fixture.Connection.Inject(new TelemetryMessage("other", 1, new(0, 0), 0, "IDLE", DateTimeOffset.UtcNow));
        fixture.Connection.Inject(new TelemetryMessage(fixture.Session.AmrId, 2, new(0.5, 0), 0, "IDLE", DateTimeOffset.UtcNow));
        fixture.Connection.Reject();
        var state = Read(fixture.Session);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("position").ValueKind);
        Assert.Equal(3, state.GetProperty("rejectedCount").GetInt32());
        Assert.NotEqual("Failed", Phase(fixture.Session));
    }

    [Fact]
    public async Task InitialConnectionFailureDoesNotMoveAndDisposesConnection()
    {
        await using var fixture = new Fixture(new FakeConnection { ConnectFailure = true });
        fixture.Session.Start();
        await Wait(() => Phase(fixture.Session) == "Failed");
        Assert.Equal(0, Read(fixture.Session).GetProperty("navigation").GetProperty("moves").GetInt32());
        Assert.Contains("ConnectionFailed", Read(fixture.Session).GetProperty("failure").GetString());
        Assert.True(fixture.Connection.Disposed);
    }

    [Fact]
    public async Task LossBeforeConnectReturnsIsNotMissed()
    {
        await using var fixture = new Fixture(new FakeConnection { LoseOnConnect = true });
        fixture.Session.Start();
        await Wait(() => Phase(fixture.Session) == "Failed");
        Assert.Equal(0, Read(fixture.Session).GetProperty("navigation").GetProperty("moves").GetInt32());
        Assert.True(fixture.Connection.Disposed);
    }

    [Fact]
    public async Task BrokerLossStopsMovingAndPreservesLastReceivedPosition()
    {
        await using var fixture = new Fixture();
        fixture.Session.Start();
        await Wait(() => Read(fixture.Session).GetProperty("telemetryCount").GetInt32() > 0);
        fixture.Connection.Lose();
        await Wait(() => Phase(fixture.Session) == "Failed");
        var before = Read(fixture.Session);
        await Task.Delay(650);
        var after = Read(fixture.Session);
        Assert.Equal(before.GetProperty("position").GetRawText(), after.GetProperty("position").GetRawText());
        Assert.Equal(before.GetProperty("navigation").GetProperty("moves").GetInt32(), after.GetProperty("navigation").GetProperty("moves").GetInt32());
        Assert.Equal("BrokerDisconnected", after.GetProperty("failure").GetString());
    }

    [Fact]
    public async Task BlockedRobotContinuesIndependentTelemetryAndHeartbeat()
    {
        await using var fixture = new Fixture();
        fixture.Session.SetObstacle(new(1, 1), true);
        fixture.Session.Start();
        await Wait(() => Read(fixture.Session).GetProperty("heartbeatCount").GetInt32() >= 3);
        var state = Read(fixture.Session);
        Assert.Equal("Waiting", Phase(fixture.Session));
        Assert.True(state.GetProperty("telemetryCount").GetInt32() >= 3);
        Assert.Equal(0, state.GetProperty("navigation").GetProperty("moves").GetInt32());
        Assert.Equal("IDLE", state.GetProperty("receivedTelemetry").GetProperty("state").GetString());
        Assert.Equal(0, state.GetProperty("receivedTelemetry").GetProperty("speed").GetDouble());
    }

    [Fact]
    public async Task HostShutdownWaitsForConnectionCleanup()
    {
        var connection = new FakeConnection { HoldDisposal = true };
        await using var fixture = new Fixture(connection);
        fixture.Session.Start();
        await Wait(() => Read(fixture.Session).GetProperty("telemetryCount").GetInt32() > 0);
        var disposing=fixture.Session.DisposeAsync().AsTask();
        await Wait(() => connection.DisposalStarted);
        Assert.False(disposing.IsCompleted);
        Assert.False(fixture.Session.Start());
        connection.ReleaseDisposal();
        await disposing;
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task ResetKeepsTheSessionAndRefreshesActualReceivedPosition()
    {
        var connection = new FakeConnection();
        await using var fixture = new Fixture(connection);
        fixture.Session.Start();
        await Wait(() => Read(fixture.Session).GetProperty("telemetryCount").GetInt32() > 0);
        fixture.Session.Reset();
        await Wait(() => Phase(fixture.Session) == "Ready");
        await Wait(()=>Read(fixture.Session).GetProperty("receivedTelemetry").GetProperty("state").GetString()=="IDLE");
        int count=Read(fixture.Session).GetProperty("heartbeatCount").GetInt32();
        await Wait(()=>Read(fixture.Session).GetProperty("heartbeatCount").GetInt32()>count);
        Assert.False(connection.Disposed);
    }

    [Fact]
    public async Task CompletionRequiresReceivingFinalStoppedTelemetry()
    {
        await using var fixture = new Fixture();
        fixture.Session.Start();
        await Wait(() => Phase(fixture.Session) == "Completed", 12000);
        var state = Read(fixture.Session);
        Assert.Equal(4, state.GetProperty("position").GetProperty("row").GetInt32());
        Assert.Equal(5, state.GetProperty("position").GetProperty("col").GetInt32());
        Assert.Equal(10, state.GetProperty("receivedTrail").GetArrayLength());
        Assert.Equal(state.GetProperty("lastPublishedTimestamp").GetInt64(), state.GetProperty("receivedTelemetry").GetProperty("timestamp").GetInt64());
        Assert.Equal(0, state.GetProperty("receivedTelemetry").GetProperty("speed").GetDouble());
        Assert.Equal("IDLE", state.GetProperty("receivedTelemetry").GetProperty("state").GetString());
        Assert.False(fixture.Connection.Disposed);
    }

    [Fact]
    public async Task PublicationWithoutFinalReceptionCannotCompleteOrMoveMarkerToGoal()
    {
        await using var fixture = new Fixture(new FakeConnection { DropFinal = true });
        fixture.Session.Start();
        await Wait(() => Phase(fixture.Session) == "Failed", 14000);
        var state = Read(fixture.Session);
        Assert.Equal("FinalTelemetryNotReceived", state.GetProperty("failure").GetString());
        Assert.Equal(4, state.GetProperty("position").GetProperty("col").GetInt32());
        Assert.Equal(9, state.GetProperty("navigation").GetProperty("moves").GetInt32());
    }

    [Fact]
    public async Task SeparateServersUseSeparateAmrIds()
    {
        await using var first = new Fixture();
        await using var second = new Fixture();
        Assert.NotEqual(first.Session.AmrId, second.Session.AmrId);
    }

    [Fact]
    public async Task OneMqttAmrVisitsWaypointsEvenWhenDetourCrossesLaterTargetFirst()
    {
        await using var fixture = new Fixture(navigator: new GridNavigator(new(0, 0), [new(2, 3), new(4, 3), new(4, 5)]));
        string id = fixture.Session.AmrId;
        fixture.Session.SetObstacle(new(2, 2), true);
        fixture.Session.Start();
        await Wait(() => Phase(fixture.Session) == "Completed", 16000);
        var state = Read(fixture.Session);
        Assert.Equal(id, state.GetProperty("receivedTelemetry").GetProperty("id").GetString());
        Assert.Equal(15, state.GetProperty("navigation").GetProperty("moves").GetInt32());
        Assert.Equal(new[] { 11, 13, 15 }, state.GetProperty("navigation").GetProperty("reachedWaypoints").EnumerateArray().Select(x => x.GetProperty("moves").GetInt32()));
        Assert.Equal("IDLE", state.GetProperty("receivedTelemetry").GetProperty("state").GetString());
    }

    [Fact]
    public async Task MissionAlreadyAtEveryWaypointStillRequiresFinalReceivedStop()
    {
        await using var fixture = new Fixture(navigator: new GridNavigator(new(0, 0), [new(0, 0), new(0, 0)]));
        fixture.Session.Start();
        await Wait(() => Phase(fixture.Session) == "Completed");
        var state = Read(fixture.Session);
        Assert.Equal(0, state.GetProperty("navigation").GetProperty("moves").GetInt32());
        Assert.Equal(2, state.GetProperty("navigation").GetProperty("nextWaypointIndex").GetInt32());
        Assert.Equal(0, state.GetProperty("receivedTelemetry").GetProperty("speed").GetDouble());
        Assert.Equal(state.GetProperty("lastPublishedTimestamp").GetInt64(), state.GetProperty("receivedTelemetry").GetProperty("timestamp").GetInt64());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletionIsNotVisibleBeforeFinalTelemetryIsReceived(bool alreadyThere)
    {
        var goal = alreadyThere ? new GridCell(0, 0) : new GridCell(0, 1);
        await using var fixture = new Fixture(navigator: new GridNavigator(new(0, 0), [goal]));
        var observed = new System.Collections.Concurrent.ConcurrentQueue<string>();
        fixture.Connection.Publishing = publication =>
        {
            if (publication.Kind != "Telemetry") return;
            using var document = JsonDocument.Parse(publication.Payload);
            var payload = document.RootElement;
            if (payload.GetProperty("pos").GetProperty("x").GetDouble() == goal.Col && payload.GetProperty("sp").GetDouble() == 0)
                if(!Read(fixture.Session).GetProperty("missionFinalReceived").GetBoolean()) observed.Enqueue(Phase(fixture.Session));
        };
        fixture.Session.Start();
        await Wait(() => Phase(fixture.Session) == "Completed");
        Assert.NotEmpty(observed);
        Assert.All(observed, phase => Assert.Equal("WaitingForTelemetry", phase));
    }

    private static async Task Wait(Func<bool> condition, int timeoutMs = 4000)
    {
        using var cancellation = new CancellationTokenSource(timeoutMs);
        while (!condition()) await Task.Delay(10, cancellation.Token);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "grid-mqtt-test-" + Guid.NewGuid().ToString("N"));
        public FakeConnection Connection { get; }
        public GridMapSession Session { get; }
        public Fixture(FakeConnection? connection = null, GridNavigator? navigator = null)
        {
            Directory.CreateDirectory(_directory);
            Connection = connection ?? new();
            Session = new(_directory, Options, () => Connection, navigator, transportFactory: () => new FakeSender(Connection));
        }
        public async ValueTask DisposeAsync()
        {
            Connection.ReleaseDisposal();
            await Session.DisposeAsync();
            Directory.Delete(_directory, true);
        }
    }

    private sealed class FakeSender(FakeConnection connection) : ISimulationTransport
    {
        public event Action? ConnectionLost { add { } remove { } }
        public Task ConnectAsync(CancellationToken token) => Task.CompletedTask;
        public Task PublishAsync(SimulationPublication publication, CancellationToken token) => connection.PublishAsync(publication, token);
        public Task DisconnectAsync() => Task.CompletedTask;
        public void Dispose() { }
    }
    private sealed class FakeConnection : IGridControlConnection
    {
        public event Action<object>? Received;
        public event Action<string>? Failed;
        public event Action? Rejected;
        public bool Echo { get; init; } = true;
        public bool ConnectFailure { get; init; }
        public bool LoseOnConnect { get; init; }
        public bool DropFinal { get; init; }
        public bool PublishFailure { get; init; }
        public bool HoldDisposal { get; init; }
        public Action<SimulationPublication>? Publishing { get; set; }
        public volatile bool DisposalStarted, Disposed;
        private readonly TaskCompletionSource _dispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ConnectAsync(CancellationToken token)
        {
            if (ConnectFailure) throw new IOException("NoBroker");
            if (LoseOnConnect) Failed?.Invoke("SenderDisconnected");
            return Task.CompletedTask;
        }
        public Task PublishAsync(SimulationPublication publication, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(PublishFailure)throw new IOException("AmrPublishFailed");
            Publishing?.Invoke(publication);
            if (!Echo) return Task.CompletedTask;
            using var document = JsonDocument.Parse(publication.Payload);
            var json = document.RootElement;
            string id = json.GetProperty("id").GetString()!;
            long ts = json.GetProperty("ts").GetInt64();
            if (publication.Kind == "Heartbeat") Received?.Invoke(new HeartbeatMessage(id, ts, "OK", DateTimeOffset.UtcNow));
            else
            {
                var position = json.GetProperty("pos");
                double x = position.GetProperty("x").GetDouble(), y = position.GetProperty("y").GetDouble();
                if (!(DropFinal && x == 5 && y == -4)) Received?.Invoke(new TelemetryMessage(id, ts, new(x, y),
                    json.GetProperty("sp").GetDouble(), json.GetProperty("state").GetString()!, DateTimeOffset.UtcNow));
            }
            return Task.CompletedTask;
        }
        public void Inject(object model) => Received?.Invoke(model);
        public void Lose() => Failed?.Invoke("BrokerDisconnected");
        public void Reject() => Rejected?.Invoke();
        public void ReleaseDisposal() => _dispose.TrySetResult();
        public async ValueTask DisposeAsync()
        {
            DisposalStarted = true;
            if (HoldDisposal) await _dispose.Task;
            Disposed = true;
        }
    }
}
