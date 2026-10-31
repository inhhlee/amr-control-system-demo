using System.Collections.Concurrent;
using Amr.Communication.Command;
using Amr.Communication.Mqtt;
using MQTTnet;
using MQTTnet.Packets;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class ReconnectingCommunicationSessionTests
{
    internal static FakeMqttClient Client(bool subscribe = true, bool publish = true)
    {
        var c = new FakeMqttClient();
        c.ConnectResponse.SetResult(new() { ResultCode = MqttClientConnectResultCode.Success });
        if (subscribe) c.SubscribeResponse.SetResult(Subscription());
        c.DisconnectCompletion.SetResult();
        c.UnsubscribeResponse.SetResult(new(2, MessageSubscription.TopicFilters.Select(t =>
            new MqttClientUnsubscribeResultItem(t, MqttClientUnsubscribeResultCode.Success)).ToList(), "", []));
        if (publish) c.PublishResponse.SetResult(CommandPublisherTests.Result());
        return c;
    }

    internal static MqttClientSubscribeResult Subscription(bool accepted = true) => new(1,
        MessageSubscription.TopicFilters.Select((t, index) => new MqttClientSubscribeResultItem(new MqttTopicFilter { Topic = t },
            accepted || index != 2 ? MqttClientSubscribeResultCode.GrantedQoS0 : MqttClientSubscribeResultCode.NotAuthorized)).ToList(), "", []);

    internal static MqttClientDisconnectedEventArgs Lost() => new(true, new(), MqttClientDisconnectReason.UnspecifiedError, "", [], null);
    internal static Task Lose(FakeMqttClient c) { c.IsConnected = false; return c.NotifyDisconnectedAsync(Lost()); }
    internal static async Task Until(Func<bool> predicate)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!predicate()) await Task.Delay(5, stop.Token);
    }
    private static byte[] Stop(string id = "ONE") => new CommandMessage(id, "STOP", 100, new()).ToPayload();
    private static MqttApplicationMessage Heartbeat(long ts) => new MqttApplicationMessageBuilder().WithTopic("amr/ONE/heartbeat")
        .WithPayload($$"""{"id":"ONE","ts":{{ts}},"status":"OK"}""").Build();

    [Fact]
    public async Task DisconnectCreatesFreshClientResubscribesAndDeliversNewMessagesWithoutStaleEvents()
    {
        var first = Client(); var second = Client();
        var clients = new ConcurrentQueue<FakeMqttClient>([first, second]);
        var events = new ConcurrentQueue<CommunicationSessionEvent>();
        await using var session = new ReconnectingCommunicationSession(new(), "ONE", new() { Delay = TimeSpan.FromMilliseconds(10), MaxAttempts = 1 },
            events.Enqueue, () => clients.TryDequeue(out var c) ? c : throw new InvalidOperationException());
        var run = session.RunAsync();
        await Until(() => session.IsReady);
        var stale = first.CaptureDisconnectedCallback();
        await first.ReceiveAsync(Heartbeat(1));
        // 큐 접수와 소비 완료는 다르다. 기존 세션에서 관찰한 전달을 확정한 뒤 단절시킨다.
        await Until(() => events.Count(e => e.Name == "Received") == 1);
        await Lose(first);
        await Until(() => events.Any(e => e.Name == "Reconnected"));
        Assert.Equal(1, first.DisposeCalls);
        Assert.Equal(0, first.MessageHandlerCount);
        Assert.NotEqual(first.Options.ClientId, second.Options.ClientId);
        Assert.Equal(4, second.SubscribeOptions!.TopicFilters.Count);
        await stale(Lost());
        Assert.True(session.IsReady);
        await first.ReceiveAsync(Heartbeat(99));
        await second.ReceiveAsync(Heartbeat(2));
        await Until(() => events.Count(e => e.Name == "Received") == 2);
        Assert.Single(events, e => e.Name == "Reconnected");
        await session.PublishCommandAsync(Stop());
        Assert.Equal(0, first.PublishCalls);
        Assert.Equal(1, second.PublishCalls);
        await session.StopAsync();
        Assert.Equal(0, await run);
        Assert.Equal(1, second.DisposeCalls);
        Assert.Equal(0, second.DisconnectedHandlerCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task InitialFailuresUseExactlyInitialPlusConfiguredAttemptsThenExit(int attempts)
    {
        int created = 0;
        var all = new ConcurrentBag<FakeMqttClient>();
        var events = new ConcurrentQueue<CommunicationSessionEvent>();
        await using var session = new ReconnectingCommunicationSession(new(), "ONE", new() { Delay = TimeSpan.FromMilliseconds(1), MaxAttempts = attempts }, events.Enqueue,
            () => { Interlocked.Increment(ref created); var c = new FakeMqttClient(); all.Add(c); c.ConnectResponse.SetException(new IOException()); return c; });
        Assert.Equal(1, await session.RunAsync().WaitAsync(TimeSpan.FromSeconds(4)));
        Assert.Equal(1 + attempts, created);
        Assert.Equal(attempts, events.Count(e => e.Name == "ReconnectAttempt"));
        Assert.Single(events, e => e.Name == "RetriesExhausted");
        Assert.All(all, c => Assert.Equal(1, c.DisposeCalls));
        Assert.False(session.IsReady);
    }

    [Fact]
    public async Task PartialSubscriptionNeverBecomesReadyAndNextAttemptWaitsForItsApproval()
    {
        var first = Client(subscribe: false); first.SubscribeResponse.SetResult(Subscription(false));
        var second = Client(subscribe: false);
        var clients = new ConcurrentQueue<FakeMqttClient>([first, second]);
        var events = new ConcurrentQueue<CommunicationSessionEvent>();
        await using var session = new ReconnectingCommunicationSession(new(), "ONE", new() { Delay = TimeSpan.FromMilliseconds(1) }, events.Enqueue,
            () => clients.TryDequeue(out var c) ? c : throw new InvalidOperationException());
        var run = session.RunAsync();
        await second.SubscribeEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.False(session.IsReady);
        await Assert.ThrowsAsync<CommandPublishException>(() => session.PublishCommandAsync(Stop()));
        Assert.Equal(0, first.PublishCalls + second.PublishCalls);
        Assert.DoesNotContain(events, e => e.Name is "Ready" or "Reconnected");
        second.SubscribeResponse.SetResult(Subscription());
        await Until(() => session.IsReady);
        Assert.Single(events, e => e.Name == "Ready");
        await session.StopAsync(); Assert.Equal(0, await run);
    }

    [Fact]
    public async Task CancellationInRetryDelayStopsWithoutAnotherClientOrWaitingForDelay()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        await using var session = new ReconnectingCommunicationSession(new(), "ONE", new() { Delay = TimeSpan.FromMinutes(1) },
            e => { if (e.Name == "ReconnectWaiting") entered.TrySetResult(); }, () => { calls++; var c = new FakeMqttClient(); c.ConnectResponse.SetException(new IOException()); return c; });
        var run = session.RunAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
        await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await run); Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringStartupDisposesPendingConnectOrSubscribe(bool subscribing)
    {
        var c = subscribing ? Client(subscribe: false) : new FakeMqttClient();
        await using var session = new ReconnectingCommunicationSession(new(), "ONE", clientFactory: () => c);
        var run = session.RunAsync();
        await (subscribing ? c.SubscribeEntered.Task : c.ConnectEntered.Task).WaitAsync(TimeSpan.FromSeconds(3));
        await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, await run); Assert.Equal(1, c.DisposeCalls);
        Assert.Equal(0, c.MessageHandlerCount); Assert.Equal(0, c.DisconnectedHandlerCount);
    }

    [Fact]
    public async Task PublishedCommandIsCanceledOnLossAndNeverReplayedOnNewConnection()
    {
        var first = Client(publish: false); var second = Client();
        var clients = new ConcurrentQueue<FakeMqttClient>([first, second]);
        await using var session = new ReconnectingCommunicationSession(new(), "ONE", new() { Delay = TimeSpan.FromMilliseconds(10) },
            clientFactory: () => clients.TryDequeue(out var c) ? c : throw new InvalidOperationException());
        var run = session.RunAsync(); await Until(() => session.IsReady);
        var publish = session.PublishCommandAsync(Stop("BEFORE_LOSS"));
        await first.PublishEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Lose(first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publish);
        await Until(() => session.IsReady && second.SubscribeOptions is not null);
        Assert.Equal(1, first.PublishCalls); Assert.Equal(0, second.PublishCalls);
        first.PublishResponse.TrySetResult(CommandPublisherTests.Result());
        await session.PublishCommandAsync(Stop("AFTER_LOSS"));
        Assert.Equal(1, second.PublishCalls);
        await session.StopAsync(); Assert.Equal(0, await run);
    }

    [Fact]
    public async Task RetryBudgetResetsOnlyAfterSuccessfulSubscription()
    {
        var all = new[] { Client(), Client(), Client() };
        var clients = new ConcurrentQueue<FakeMqttClient>(all);
        var events = new ConcurrentQueue<CommunicationSessionEvent>();
        await using var session = new ReconnectingCommunicationSession(new(), "ONE", new() { Delay = TimeSpan.FromMilliseconds(1), MaxAttempts = 1 },
            events.Enqueue, () => clients.TryDequeue(out var c) ? c : throw new InvalidOperationException());
        var run = session.RunAsync(); await Until(() => session.IsReady);
        await Lose(all[0]); await Until(() => events.Count(e => e.Name == "Reconnected") == 1);
        await Lose(all[1]); await Until(() => events.Count(e => e.Name == "Reconnected") == 2);
        Assert.Equal(2, events.Count(e => e.Name == "ReconnectAttempt"));
        Assert.DoesNotContain(events, e => e.Name == "RetriesExhausted");
        await session.StopAsync(); Assert.Equal(0, await run);
    }

    [Fact]
    public async Task ObserverFailureIsFatalAndDoesNotSpawnReconnectLoop()
    {
        var c = Client();
        await using var session = new ReconnectingCommunicationSession(new(), "ONE", report: e => {
            if (e.Name == "Ready") throw new IOException("private output failure");
        }, clientFactory: () => c);
        Assert.Equal(1, await session.RunAsync().WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(1, c.DisposeCalls);
    }

    [Fact]
    public async Task ReadyNotificationCannotBeOvertakenByDisconnectNotification()
    {
        var c = Client();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new ConcurrentQueue<CommunicationSessionEvent>();
        await using var session = new ReconnectingCommunicationSession(new(), "ONE", new() { MaxAttempts = 0 }, e => {
            if (e.Name == "Ready") { entered.TrySetResult(); release.Task.GetAwaiter().GetResult(); }
            events.Enqueue(e);
        }, () => c);
        var run = session.RunAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var disconnectEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loss = Task.Run(async () => { disconnectEntered.TrySetResult(); await Lose(c); });
        await disconnectEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            Assert.NotSame(loss, await Task.WhenAny(loss, Task.Delay(100)));
            Assert.DoesNotContain(events, e => e.Name == "Failed");
        }
        finally { release.TrySetResult(); }
        await loss.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, await run.WaitAsync(TimeSpan.FromSeconds(3)));
        var names = events.Select(e => e.Name).ToList();
        Assert.True(names.IndexOf("Ready") < names.IndexOf("Failed"));
    }

    [Fact]
    public async Task StopBeforeStartIsIdempotentAndPreventsLateRun()
    {
        var session = new ReconnectingCommunicationSession(new(), "ONE");
        await session.StopAsync(); await session.DisposeAsync();
        Assert.Throws<InvalidOperationException>(() => { _ = session.RunAsync(); });
    }
}
