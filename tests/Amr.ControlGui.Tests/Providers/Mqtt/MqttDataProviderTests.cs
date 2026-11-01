using System.Collections.Concurrent;
using Amr.Communication.Mqtt;
using Amr.Communication.Tests;
using Amr.Core;
using Amr.Infrastructure;
using MQTTnet;
using MQTTnet.Packets;
using Xunit;

namespace Amr.ControlGui.Tests;

public sealed class MqttDataProviderTests
{
    internal static FakeMqttClient Client(bool subscribe = true)
    {
        var client = new FakeMqttClient();
        client.ConnectResponse.SetResult(new() { ResultCode = MqttClientConnectResultCode.Success });
        if (subscribe) client.SubscribeResponse.SetResult(Subscriptions());
        client.DisconnectCompletion.SetResult();
        client.UnsubscribeResponse.SetResult(new(2, MessageSubscription.TopicFilters.Select(topic =>
            new MqttClientUnsubscribeResultItem(topic, MqttClientUnsubscribeResultCode.Success)).ToList(), "", []));
        return client;
    }
    internal static MqttClientSubscribeResult Subscriptions() => new(1, MessageSubscription.TopicFilters.Select(topic =>
        new MqttClientSubscribeResultItem(new MqttTopicFilter { Topic = topic }, MqttClientSubscribeResultCode.GrantedQoS0)).ToList(), "", []);
    internal static Task Send(FakeMqttClient client, string kind, string payload) => client.ReceiveAsync(
        new MqttApplicationMessageBuilder().WithTopic($"amr/AMR_001/{kind}").WithPayload(payload).Build());
    internal static Task Telemetry(FakeMqttClient client, int x, string state = "BUSY") => Send(client, "telemetry",
        $$"""{"id":"AMR_001","ts":100,"pos":{"x":{{x}},"y":2},"sp":{{(state == "BUSY" ? 1 : 0)}},"state":"{{state}}"}""");
    internal static Task Heartbeat(FakeMqttClient client) => Send(client, "heartbeat", """{"id":"AMR_001","ts":100,"status":"OK"}""");
    internal static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }
    internal static async Task<AmrChange> ReadUntil(IAsyncEnumerator<AmrChange> watch, Func<AmrChange, bool> condition)
    {
        while (await watch.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8)))
            if (condition(watch.Current)) return watch.Current;
        throw new InvalidOperationException("조건 확인 전에 구독이 종료되었습니다.");
    }

    [Fact]
    public async Task InitialSnapshotDoesNotInventPositionAndValidatedTelemetryIsPreserved()
    {
        var client = Client();
        await using var provider = new MqttAmrDataProvider(new(), clientFactory: () => client);
        await using var watch = provider.WatchAsync().GetAsyncEnumerator();
        Assert.True(await watch.MoveNextAsync());
        Assert.Empty(watch.Current.Devices);
        Assert.False(watch.Current.Connection!.TelemetryIsCurrent);
        provider.Start();
        await ReadUntil(watch, x => x.Connection!.State == DataConnectionState.Connected);
        await Heartbeat(client);
        await Telemetry(client, -3);
        var change = await ReadUntil(watch, x => x.Devices.Length == 1);
        var device = Assert.Single(change.Devices);
        Assert.Equal((-3, 2, 1), (device.X, device.Y, device.SpeedMps));
        Assert.Equal(AmrState.BUSY, device.State);
        Assert.Equal(LivenessObservation.Alive, device.Liveness);
        Assert.True(change.Connection!.TelemetryIsCurrent);
        await Send(client, "telemetry", """{"id":"AMR_001","ts":100,"pos":{"x":999,"y":0},"sp":-1,"state":"BUSY"}""");
        change = await ReadUntil(watch, x => x.Connection!.RejectedMessages == 1);
        Assert.Equal(-3, Assert.Single(change.Devices).X);
        Assert.Empty(change.Alarms); Assert.Empty(change.Commands);
    }

    [Fact]
    public async Task ReconnectKeepsLastValueAndDoesNotClaimFreshTelemetryUntilNewMessage()
    {
        var first = Client(); var second = Client(subscribe: false);
        var clients = new ConcurrentQueue<FakeMqttClient>([first, second]);
        await using var provider = new MqttAmrDataProvider(new(), reconnect: new() { Delay = TimeSpan.FromMilliseconds(10), MaxAttempts = 1 },
            clientFactory: () => clients.TryDequeue(out var client) ? client : throw new InvalidOperationException());
        await using var watch = provider.WatchAsync().GetAsyncEnumerator();
        provider.Start();
        await ReadUntil(watch, x => x.Connection!.State == DataConnectionState.Connected);
        await Telemetry(first, 7);
        await ReadUntil(watch, x => x.Devices.Length == 1);
        first.IsConnected = false;
        await first.NotifyDisconnectedAsync(new(true, new(), MqttClientDisconnectReason.UnspecifiedError, "", [], null));
        var lost = await ReadUntil(watch, x => x.Connection!.State == DataConnectionState.ReconnectWaiting);
        Assert.False(lost.Connection!.TelemetryIsCurrent);
        Assert.Equal(7, Assert.Single(lost.Devices).X);
        Assert.Equal(AmrState.BUSY, lost.Devices[0].State); // 연결 끊김을 AMR 정지로 바꾸지 않는다.
        Assert.Equal(ConnectionObservation.Disconnected, lost.Devices[0].BrokerConnection);
        await Until(() => second.SubscribeOptions is not null);
        second.SubscribeResponse.SetResult(Subscriptions());
        var ready = await ReadUntil(watch, x => x.Connection!.State == DataConnectionState.Connected);
        Assert.False(ready.Connection!.TelemetryIsCurrent);
        Assert.Equal(LivenessObservation.Unknown, ready.Devices[0].Liveness);
        await Telemetry(second, 9);
        var fresh = await ReadUntil(watch, x => x.Connection!.TelemetryIsCurrent);
        Assert.Equal(9, Assert.Single(fresh.Devices).X);
        Assert.Equal(4, second.SubscribeOptions!.TopicFilters.Count);
        await provider.DisposeAsync();
        Assert.Equal(1, first.DisposeCalls); Assert.Equal(1, second.DisposeCalls);
        Assert.Equal(0, first.MessageHandlerCount); Assert.Equal(0, second.DisconnectedHandlerCount);
    }

    [Fact]
    public async Task HeartbeatTimeoutIsIndependentOfBrokerAndRecoversOnNewHeartbeat()
    {
        var client = Client();
        await using var provider = new MqttAmrDataProvider(new(), clientFactory: () => client);
        await using var watch = provider.WatchAsync().GetAsyncEnumerator();
        provider.Start();
        await ReadUntil(watch, x => x.Connection!.State == DataConnectionState.Connected);
        await Telemetry(client, 2); await Heartbeat(client);
        await ReadUntil(watch, x => x.Devices.FirstOrDefault()?.Liveness == LivenessObservation.Alive);
        var timeout = await ReadUntil(watch, x => x.Devices.FirstOrDefault()?.Liveness == LivenessObservation.Unresponsive);
        Assert.Equal(DataConnectionState.Connected, timeout.Connection!.State);
        Assert.Equal(2, timeout.Devices[0].X);
        await Heartbeat(client);
        var recovery = await ReadUntil(watch, x => x.Devices.FirstOrDefault()?.Liveness == LivenessObservation.Alive);
        Assert.Equal(ConnectionObservation.Connected, recovery.Devices[0].BrokerConnection);
    }

    [Fact]
    public async Task OverflowRequiresResyncAndNewSubscriptionStartsWithLatestSnapshot()
    {
        var client = Client();
        var events = new ConcurrentQueue<CommunicationSessionEvent>();
        await using var provider = new MqttAmrDataProvider(new(), subscriptionCapacity: 1, report: events.Enqueue, clientFactory: () => client);
        await using var slow = provider.WatchAsync().GetAsyncEnumerator();
        Assert.True(await slow.MoveNextAsync());
        provider.Start();
        await Until(() => events.Any(x => x.Name == "Ready"));
        await Telemetry(client, 11);
        await Until(() => provider.GetDevicesAsync().Result.Count == 1);
        await Assert.ThrowsAsync<AmrResyncRequiredException>(() => slow.MoveNextAsync().AsTask());
        await using var newWatch = provider.WatchAsync().GetAsyncEnumerator();
        Assert.True(await newWatch.MoveNextAsync());
        Assert.Equal(AmrChangeKind.InitialSnapshot, newWatch.Current.Kind);
        Assert.Equal(11, newWatch.Current.Devices[0].X);
    }

    [Fact]
    public async Task CancelingOneSubscriberDoesNotStopSharedSession()
    {
        var client = Client();
        await using var provider = new MqttAmrDataProvider(new(), clientFactory: () => client);
        using var cancel = new CancellationTokenSource();
        await using (var canceled = provider.WatchAsync(cancel.Token).GetAsyncEnumerator())
        {
            Assert.True(await canceled.MoveNextAsync());
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.MoveNextAsync().AsTask());
        }
        await using var remaining = provider.WatchAsync().GetAsyncEnumerator();
        provider.Start();
        await ReadUntil(remaining, x => x.Connection!.State == DataConnectionState.Connected);
        await Telemetry(client, 6);
        Assert.Equal(6, (await ReadUntil(remaining, x => x.Devices.Length == 1)).Devices[0].X);
    }

    [Fact]
    public async Task RetryExhaustionRemainsFailedAndDisposeInterruptsRetryDelay()
    {
        int created = 0;
        FakeMqttClient FailedClient()
        {
            Interlocked.Increment(ref created);
            var client = Client();
            client.BeforeConnectReturn = () => throw new IOException("test failure");
            return client;
        }
        await using (var provider = new MqttAmrDataProvider(new(), reconnect: new() { Delay = TimeSpan.FromMilliseconds(10), MaxAttempts = 1 }, clientFactory: FailedClient))
        {
            await using var watch = provider.WatchAsync().GetAsyncEnumerator();
            provider.Start();
            var failed = await ReadUntil(watch, x => x.Connection!.State == DataConnectionState.Failed);
            Assert.Empty(failed.Devices);
            Assert.Equal(2, created);
        }
        created = 0;
        var events = new ConcurrentQueue<CommunicationSessionEvent>();
        await using var waiting = new MqttAmrDataProvider(new(), reconnect: new() { Delay = TimeSpan.FromHours(1), MaxAttempts = 1 },
            report: events.Enqueue, clientFactory: FailedClient);
        waiting.Start();
        await Until(() => events.Any(x => x.Name == "ReconnectWaiting"));
        await waiting.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, created);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting.GetDevicesAsync());
    }

    [Fact]
    public async Task ImmediateStartAndDisposeAlwaysJoinTheOwnedSession()
    {
        for (int i = 0; i < 10; i++)
        {
            var client = Client();
            var provider = new MqttAmrDataProvider(new(), clientFactory: () => client);
            provider.Start();
            await provider.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(0, client.MessageHandlerCount);
            Assert.Equal(0, client.DisconnectedHandlerCount);
        }
    }
}
