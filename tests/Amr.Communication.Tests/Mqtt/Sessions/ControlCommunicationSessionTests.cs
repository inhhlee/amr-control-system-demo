using System.Collections.Concurrent;
using Amr.Communication.Models;
using Amr.Communication.Monitoring;
using Amr.Communication.Mqtt;
using MQTTnet;
using MQTTnet.Packets;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class ControlCommunicationSessionTests
{
    [Fact]
    public async Task PeriodicInspectionRegistersOnlyValidTargetHeartbeatAndReportsEachGapOnce()
    {
        await using var run = new Run();
        await run.Session.StartAsync();
        run.Clock.Advance(20);
        await run.Client.ReceiveAsync(Packet("heartbeat", "OTHER"));
        await run.Client.ReceiveAsync(Packet("heartbeat", status: "BAD"));
        await run.Client.ReceiveAsync(Packet("telemetry"));
        Assert.Equal(HeartbeatObservationState.AwaitingFirst, run.Session.Heartbeat.State);
        Assert.DoesNotContain(run.Events, e => e.Name == "HeartbeatTimedOut");
        await run.Client.ReceiveAsync(Packet("heartbeat"));
        Assert.Equal(HeartbeatObservationState.Alive, run.Session.Heartbeat.State);
        run.Clock.Advance(5);
        await Until(() => run.Events.Any(e => e.Name == "HeartbeatTimedOut"));
        run.Clock.Advance(10);
        await run.Client.ReceiveAsync(Packet("heartbeat"));
        Assert.Equal(HeartbeatObservationState.Alive, run.Session.Heartbeat.State);
        Assert.Equal(1, run.Session.Heartbeat.TimeoutCount);
        Assert.Equal(1, run.Session.Heartbeat.RecoveryCount);
        Assert.Single(run.Events, e => e.Name == "HeartbeatMonitoringStarted");
        Assert.Single(run.Events, e => e.Name == "HeartbeatTimedOut");
        Assert.Single(run.Events, e => e.Name == "HeartbeatRecovered");
    }

    [Fact]
    public async Task SlowCoreHandlerDoesNotDelayObservationPeriodicTimeoutOrRecovery()
    {
        var entered = Signal();
        var release = Signal();
        await using var run = new Run(async (_, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); });
        await run.Session.StartAsync();
        await run.Client.ReceiveAsync(Packet("telemetry"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await run.Client.ReceiveAsync(Packet("heartbeat"));
        run.Clock.Advance(5);
        await Until(() => run.Session.Heartbeat.State == HeartbeatObservationState.Unresponsive);
        run.Clock.Advance(.1);
        await run.Client.ReceiveAsync(Packet("heartbeat"));
        Assert.Equal(HeartbeatObservationState.Alive, run.Session.Heartbeat.State);
        Assert.Equal(1, run.Session.Heartbeat.RecoveryCount);
        Assert.False(release.Task.IsCompleted);
        // Disposal must cancel the held handler and await its actual completion.
    }

    [Fact]
    public async Task BrokerLossStopsMonitoringImmediatelyAndLateDisconnectCannotChangeResult()
    {
        await using var run = new Run();
        await run.Session.StartAsync();
        await run.Client.ReceiveAsync(Packet("heartbeat"));
        var stale = run.Client.CaptureDisconnectedCallback();
        run.Client.IsConnected = false;
        await stale(Disconnected());
        Assert.Equal(HeartbeatObservationState.Stopped, run.Session.Heartbeat.State);
        Assert.False(run.Session.IsConnected);
        await run.Session.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        run.Clock.Advance(30);
        await stale(Disconnected());
        await run.Client.ReceiveAsync(Packet("heartbeat"));
        Assert.Equal("BrokerDisconnected", run.Session.FailureReason);
        Assert.Single(run.Events, e => e.Name == "Failed");
        Assert.DoesNotContain(run.Events, e => e.Name == "HeartbeatTimedOut");
        run.AssertDisposed();
    }

    [Fact]
    public async Task RepeatedStopAndDisposeWaitForSameCleanupAndTimerIsAlreadyStopped()
    {
        await using var run = new Run(holdDisconnect: true);
        await run.Session.StartAsync();
        await run.Client.ReceiveAsync(Packet("heartbeat"));
        var first = run.Session.StopAsync();
        Assert.Same(first, run.Session.StopAsync());
        var disposal = run.Session.DisposeAsync().AsTask();
        await run.Client.DisconnectEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(first.IsCompleted);
        Assert.False(disposal.IsCompleted);
        Assert.Equal(HeartbeatObservationState.Stopped, run.Session.Heartbeat.State);
        Assert.Equal(0, run.Clock.ActiveTimers);
        run.Clock.Advance(30);
        run.Client.DisconnectCompletion.SetResult();
        await Task.WhenAll(first, disposal).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.DoesNotContain(run.Events, e => e.Name == "HeartbeatTimedOut");
        Assert.Single(run.Events, e => e.Name == "Closed");
        run.AssertDisposed();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopDuringConnectOrSubscribeCancelsStartupBeforeDisposingResources(bool subscribing)
    {
        await using var run = new Run(holdConnect: !subscribing, holdSubscribe: subscribing);
        var start = run.Session.StartAsync();
        await (subscribing ? run.Client.SubscribeEntered : run.Client.ConnectEntered).Task.WaitAsync(TimeSpan.FromSeconds(3));
        var stop = run.Session.StopAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        await stop.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(run.Session.FailureReason);
        Assert.DoesNotContain(run.Events, e => e.Name == "Subscribed");
        run.AssertDisposed();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectDeniedOrPartialSubscriptionCleansUpWithoutStartingTimer(bool partial)
    {
        await using var run = new Run(holdConnect: !partial, holdSubscribe: partial);
        if (partial) run.Client.SubscribeResponse.SetResult(Subscription(false));
        else run.Client.ConnectResponse.SetResult(new() { ResultCode = MqttClientConnectResultCode.NotAuthorized });
        await Assert.ThrowsAnyAsync<Exception>(() => run.Session.StartAsync());
        Assert.StartsWith(partial ? "Start:" : "Connect:", run.Session.FailureReason);
        Assert.DoesNotContain(run.Events, e => e.Name == "Subscribed");
        Assert.Equal(HeartbeatObservationState.Stopped, run.Session.Heartbeat.State);
        run.AssertDisposed();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisconnectDuringStartupSeparatesInitialFailureFromApprovedConnectionLoss(bool subscribing)
    {
        await using var run = new Run(holdSubscribe: subscribing);
        if (!subscribing) run.Client.BeforeConnectReturn = () => {
            run.Client.IsConnected = false;
            run.Client.NotifyDisconnectedAsync(Disconnected()).GetAwaiter().GetResult();
        };
        var start = run.Session.StartAsync();
        if (subscribing)
        {
            await run.Client.SubscribeEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            run.Client.IsConnected = false;
            await run.Client.NotifyDisconnectedAsync(Disconnected());
        }
        await Assert.ThrowsAnyAsync<Exception>(() => start);
        Assert.Equal(subscribing ? "BrokerDisconnected" : "Connect:TransportFailure", run.Session.FailureReason);
        Assert.DoesNotContain(run.Events, e => e.Name == "Subscribed");
        run.AssertDisposed();
    }

    [Fact]
    public async Task OwnerCancellationStopsEstablishedSessionAndFreshSessionAwaitsFirstHeartbeat()
    {
        using var owner = new CancellationTokenSource();
        await using var run = new Run();
        await run.Session.StartAsync(owner.Token);
        await run.Client.ReceiveAsync(Packet("heartbeat"));
        owner.Cancel();
        await run.Session.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(run.Session.FailureReason);
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.Session.StartAsync());
        await using var fresh = new Run();
        await fresh.Session.StartAsync();
        Assert.Equal(HeartbeatObservationState.AwaitingFirst, fresh.Session.Heartbeat.State);
        Assert.Null(fresh.Session.Heartbeat.LastReceivedAtUtc);
        run.AssertDisposed();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandlerOrObserverFailureStopsWithoutWaitingOnItsOwnCallback(bool observer)
    {
        await using var run = new Run((_, _) => observer ? Task.CompletedTask : Task.FromException(new IOException("handler")),
            entry => { if (observer && entry.Name == "HeartbeatMonitoringStarted") throw new IOException("observer"); });
        await run.Session.StartAsync();
        await run.Client.ReceiveAsync(Packet("heartbeat"));
        await run.Session.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(observer ? "ObserverCallbackFailed" : "ReceiverHandlerFailed", run.Session.FailureReason);
        Assert.Single(run.Events, e => e.Name == "Failed");
        run.AssertDisposed();
    }

    [Fact]
    public async Task UnsubscribeFailureStillDisposesClientDetachesHandlersAndFaultsCompletion()
    {
        var run = new Run(holdUnsubscribe: true);
        await run.Session.StartAsync();
        run.Client.UnsubscribeResponse.SetException(new IOException("unsubscribe"));
        await Assert.ThrowsAsync<AggregateException>(() => run.Session.StopAsync());
        Assert.True(run.Session.Completion.IsFaulted);
        await Assert.ThrowsAsync<AggregateException>(() => run.Session.DisposeAsync().AsTask());
        Assert.Single(run.Events, e => e.Name == "Closed");
        run.AssertDisposed();
    }

    [Fact]
    public async Task BrokerLossWhileTimerStartsCannotPublishSubscribedAfterFailure()
    {
        await using var run = new Run();
        run.Clock.BeforeCreateTimer = () => {
            run.Client.IsConnected = false;
            run.Client.NotifyDisconnectedAsync(Disconnected()).GetAwaiter().GetResult();
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.Session.StartAsync());
        Assert.Equal("BrokerDisconnected", run.Session.FailureReason);
        Assert.DoesNotContain(run.Events, e => e.Name == "Subscribed");
        run.AssertDisposed();
    }

    [Fact]
    public async Task StopBeforeStartDoesNotConnectOrLeakAndLaterStartIsRejected()
    {
        await using var run = new Run();
        await run.Session.StopAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => run.Session.StartAsync());
        Assert.Equal(0, run.Client.ConnectCalls);
        run.AssertDisposed();
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static MqttClientDisconnectedEventArgs Disconnected() => new(true, null,
        MqttClientDisconnectReason.UnspecifiedError, null, null, null);
    private static MqttApplicationMessage Packet(string kind, string id = "ONE", string status = "OK") =>
        new MqttApplicationMessageBuilder().WithTopic($"amr/{id}/{kind}").WithPayload(kind == "heartbeat"
            ? $$"""{"id":"{{id}}","ts":101,"status":"{{status}}"}"""
            : $$"""{"id":"{{id}}","ts":102,"pos":{"x":1,"y":2},"sp":0,"state":"IDLE"}"""
        ).Build();
    private static MqttClientSubscribeResult Subscription(bool accepted = true) => new(1,
        MessageSubscription.TopicFilters.Select((topic, index) => new MqttClientSubscribeResultItem(
            new MqttTopicFilter { Topic = topic }, !accepted && index == 2
                ? MqttClientSubscribeResultCode.NotAuthorized : MqttClientSubscribeResultCode.GrantedQoS0)).ToList(), "", []);
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }

    private sealed class Run : IAsyncDisposable
    {
        public FakeMqttClient Client { get; } = new();
        public ManualClock Clock { get; } = new();
        public ConcurrentQueue<CommunicationSessionEvent> Events { get; } = new();
        public ControlCommunicationSession Session { get; }
        public Run(Func<ReceivedMessage, CancellationToken, Task>? handler = null, Action<CommunicationSessionEvent>? observe = null,
            bool holdConnect = false, bool holdSubscribe = false, bool holdDisconnect = false, bool holdUnsubscribe = false)
        {
            if (!holdConnect) Client.ConnectResponse.SetResult(new() { ResultCode = MqttClientConnectResultCode.Success });
            if (!holdSubscribe) Client.SubscribeResponse.SetResult(Subscription());
            if (!holdDisconnect) Client.DisconnectCompletion.SetResult();
            if (!holdUnsubscribe) Client.UnsubscribeResponse.SetResult(new(2, MessageSubscription.TopicFilters
                .Select(topic => new MqttClientUnsubscribeResultItem(topic, MqttClientUnsubscribeResultCode.Success)).ToList(), "", []));
            Session = new(new(), "ONE", handler ?? ((_, _) => Task.CompletedTask),
                entry => { Events.Enqueue(entry); observe?.Invoke(entry); }, client: Client, clock: Clock);
        }
        public void AssertDisposed()
        {
            Assert.Equal(1, Client.DisposeCalls);
            Assert.Equal(0, Client.MessageHandlerCount);
            Assert.Equal(0, Client.DisconnectedHandlerCount);
            Assert.Equal(0, Clock.ActiveTimers);
        }
        public ValueTask DisposeAsync() => Session.DisposeAsync();
    }

    // Exercise the real PeriodicTimer without waiting five wall-clock seconds per case.
    private sealed class ManualClock : TimeProvider
    {
        private long _stamp = 100;
        private readonly List<Timer> _timers = [];
        public Action? BeforeCreateTimer;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _stamp);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
        public int ActiveTimers { get { lock (_timers) return _timers.Count(t => !t.Disposed); } }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            BeforeCreateTimer?.Invoke();
            var timer = new Timer(callback, state);
            lock (_timers) _timers.Add(timer);
            return timer;
        }
        public void Advance(double seconds)
        {
            Interlocked.Add(ref _stamp, (long)Math.Round(seconds * TimeSpan.TicksPerSecond));
            Timer[] timers;
            lock (_timers) timers = _timers.Where(t => !t.Disposed).ToArray();
            foreach (var timer in timers) timer.Tick();
        }
        private sealed class Timer(TimerCallback callback, object? state) : ITimer
        {
            public volatile bool Disposed;
            public void Tick() { if (!Disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
