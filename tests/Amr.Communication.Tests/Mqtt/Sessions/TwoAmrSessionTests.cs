using System.Collections.Concurrent;
using Amr.Communication.Models;
using Amr.Communication.Monitoring;
using Amr.Communication.Mqtt;
using MQTTnet;
using MQTTnet.Packets;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class TwoAmrSessionTests
{
    [Fact]
    public async Task TimeoutAndRecoveryAreIndependentAndUnknownOrInvalidInputsCannotRecover()
    {
        await using var run = new Run();
        await run.Session.StartAsync();
        await run.Client.ReceiveAsync(Packet("A")); await run.Client.ReceiveAsync(Packet("B"));
        run.Clock.Advance(4); await run.Client.ReceiveAsync(Packet("B"));
        run.Clock.Advance(1);
        await Until(() => run.Read("A").State == HeartbeatObservationState.Unresponsive);
        Assert.Equal(HeartbeatObservationState.Alive, run.Read("B").State);
        await run.Client.ReceiveAsync(Packet("A", status: "BAD"));
        await run.Client.ReceiveAsync(Packet("OTHER"));
        await run.Client.ReceiveAsync(Packet("A", telemetry: true));
        Assert.Equal(HeartbeatObservationState.Unresponsive, run.Read("A").State);
        Assert.Equal(2, run.Session.Heartbeats.Count);
        run.Clock.Advance(.1); await run.Client.ReceiveAsync(Packet("A"));
        Assert.Equal(1, run.Read("A").TimeoutCount);
        Assert.Equal(1, run.Read("A").RecoveryCount);
        Assert.Equal(0, run.Read("B").TimeoutCount);
        Assert.Equal(0, run.Read("B").RecoveryCount);
        Assert.Contains(run.Observations, x => x.AmrId == "A" && x.Heartbeat.State == HeartbeatObservationState.Unresponsive);
        Assert.DoesNotContain(run.Observations, x => x.AmrId == "B" && x.Heartbeat.State == HeartbeatObservationState.Unresponsive);
    }

    [Fact]
    public async Task ExistingPerAmrQueueAllowsBToFinishWhileAIsHeldAndBothObservationsStayAlive()
    {
        var aEntered = Signal(); var release = Signal(); var bFinished = Signal();
        await using var run = new Run(async (message, token) => {
            if (message.AmrId == "A") { aEntered.TrySetResult(); await release.Task.WaitAsync(token); }
            else bFinished.TrySetResult();
        });
        await run.Session.StartAsync();
        await run.Client.ReceiveAsync(Packet("A", telemetry: true));
        await aEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await run.Client.ReceiveAsync(Packet("B", telemetry: true));
        await bFinished.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await run.Client.ReceiveAsync(Packet("A")); await run.Client.ReceiveAsync(Packet("B"));
        Assert.False(release.Task.IsCompleted);
        Assert.All(run.Session.Heartbeats, x => Assert.Equal(HeartbeatObservationState.Alive, x.State));
        Assert.All(run.Observations.Where(x => x.Heartbeat.State == HeartbeatObservationState.Alive), x => Assert.True(x.BrokerConnected));
        release.SetResult();
    }

    [Fact]
    public async Task BrokerLossPublishesStoppedForBothAndNoLateTimeoutOrRecovery()
    {
        await using var run = new Run();
        await run.Session.StartAsync();
        await run.Client.ReceiveAsync(Packet("A")); await run.Client.ReceiveAsync(Packet("B"));
        run.Client.IsConnected = false;
        await run.Client.NotifyDisconnectedAsync(new(true, null, MqttClientDisconnectReason.UnspecifiedError, null, null, null));
        await run.Session.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        int count = run.Observations.Count;
        run.Clock.Advance(100);
        await run.Client.ReceiveAsync(Packet("A"));
        Assert.Equal(count, run.Observations.Count);
        Assert.All(run.Session.Heartbeats, x => Assert.Equal(HeartbeatObservationState.Stopped, x.State));
        var terminal = run.Observations.Where(x => x.FailureReason == "BrokerDisconnected").ToArray();
        Assert.Equal(new[] { "A", "B" }, terminal.Select(x => x.AmrId));
        Assert.All(terminal, x => Assert.False(x.BrokerConnected));
        Assert.Equal(1, run.Client.DisposeCalls);
        Assert.Equal(0, run.Client.MessageHandlerCount);
    }

    [Fact]
    public async Task ObservationSequenceIsOrderedAcrossAmrsAndFinalStopUsesSameSession()
    {
        await using var run = new Run();
        await run.Session.StartAsync();
        await Task.WhenAll(Enumerable.Range(0, 30).Select(i => run.Client.ReceiveAsync(Packet(i % 2 == 0 ? "A" : "B"))));
        await run.Session.StopAsync();
        var observations = run.Observations.ToArray();
        Assert.All(observations, x => Assert.Equal(run.Session.SessionId, x.SessionId));
        Assert.Equal(observations.Select(x => x.Sequence).Order(), observations.Select(x => x.Sequence));
        Assert.Equal(observations.Length, observations.Select(x => x.Sequence).Distinct().Count());
        Assert.All(observations.TakeLast(2), x => Assert.Equal(HeartbeatObservationState.Stopped, x.Heartbeat.State));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void EmptyOrDuplicateRegistrationsAreRejectedBeforeClientOwnership(bool empty)
    {
        var client = new FakeMqttClient();
        Assert.Throws<ArgumentException>(() => new ControlCommunicationSession(new(), empty ? Array.Empty<string>() : new[] { "A", "A" },
            (_, _) => Task.CompletedTask, client: client));
        Assert.Equal(0, client.DisposeCalls);
        Assert.Equal(0, client.MessageHandlerCount);
        client.Dispose();
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static MqttApplicationMessage Packet(string id, string status = "OK", bool telemetry = false) =>
        new MqttApplicationMessageBuilder().WithTopic($"amr/{id}/{(telemetry ? "telemetry" : "heartbeat")}")
            .WithPayload(telemetry ? $$"""{"id":"{{id}}","ts":1,"pos":{"x":0,"y":0},"sp":0,"state":"IDLE"}"""
                : $$"""{"id":"{{id}}","ts":1,"status":"{{status}}"}""").Build();
    private static async Task Until(Func<bool> predicate)
    { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)); while (!predicate()) await Task.Delay(5, timeout.Token); }

    private sealed class Run : IAsyncDisposable
    {
        public FakeMqttClient Client = new();
        public Clock Clock = new();
        public ConcurrentQueue<AmrCommunicationObservation> Observations = new();
        public ControlCommunicationSession Session;
        public Run(Func<ReceivedMessage, CancellationToken, Task>? receive = null)
        {
            Client.ConnectResponse.SetResult(new() { ResultCode = MqttClientConnectResultCode.Success });
            Client.SubscribeResponse.SetResult(new(1, MessageSubscription.TopicFilters.Select(topic =>
                new MqttClientSubscribeResultItem(new MqttTopicFilter { Topic = topic }, MqttClientSubscribeResultCode.GrantedQoS0)).ToList(), "", []));
            Client.UnsubscribeResponse.SetResult(new(2, MessageSubscription.TopicFilters.Select(topic =>
                new MqttClientUnsubscribeResultItem(topic, MqttClientUnsubscribeResultCode.Success)).ToList(), "", []));
            Client.DisconnectCompletion.SetResult();
            Session = new(new(), new[] { "A", "B" }, receive ?? ((_, _) => Task.CompletedTask), client: Client, clock: Clock);
            Session.ObservationChanged += Observations.Enqueue;
        }
        public HeartbeatObservation Read(string id) => Session.Heartbeats.Single(x => x.AmrId == id);
        public ValueTask DisposeAsync() => Session.DisposeAsync();
    }
    private sealed class Clock : TimeProvider
    {
        private long _stamp = 100;
        private Timer? _timer;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _stamp);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => _timer = new(callback, state);
        public void Advance(double seconds) { Interlocked.Add(ref _stamp, (long)(seconds * TimeSpan.TicksPerSecond)); _timer?.Tick(); }
        private sealed class Timer(TimerCallback callback, object? state) : ITimer
        {
            private volatile bool _disposed;
            public void Tick() { if (!_disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
