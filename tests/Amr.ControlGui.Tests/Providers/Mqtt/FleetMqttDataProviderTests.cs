using System.Collections.Concurrent;

using Amr.Communication.Tests;
using Amr.Core;
using Amr.Infrastructure;
using MQTTnet;
using Xunit;

namespace Amr.ControlGui.Tests;

public sealed class FleetMqttDataProviderTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly FakeMqttClient A = MqttDataProviderTests.Client(), B = MqttDataProviderTests.Client();
        public readonly FleetMqttAmrDataProvider Provider;
        public readonly ConcurrentQueue<AmrChange> Changes = new();
        private readonly Task _pump;
        public AmrChange Latest => Changes.Last();
        public Fixture(int capacity = 64)
        {
            A.PublishResponse.SetResult(new(null, MqttClientPublishReasonCode.Success, null, []));
            B.PublishResponse.SetResult(new(null, MqttClientPublishReasonCode.Success, null, []));
            Provider = new(new(), [new("A", new("A goal", 10, 2)), new("B", new("B goal", 20, 5))],
                reconnect: new() { MaxAttempts = 0 }, subscriptionCapacity: capacity, clientFactory: id => id == "A" ? A : B);
            _pump = Task.Run(async () => { await foreach (var change in Provider.WatchAsync()) Changes.Enqueue(change); });
            Provider.Start();
        }
        public async Task Ready()
        {
            await MqttDataProviderTests.Until(() => Changes.LastOrDefault()?.Connection?.State == DataConnectionState.Connected);
            await Data(A, "A", 1); await Data(B, "B", 2);
            await MqttDataProviderTests.Until(() => Latest.Devices.Length == 2 && Latest.Devices.All(x => x.Liveness == LivenessObservation.Alive));
        }
        public async ValueTask DisposeAsync() { await Provider.DisposeAsync(); await _pump.WaitAsync(TimeSpan.FromSeconds(5)); }
    }
    private static Task Send(FakeMqttClient client, string id, string kind, string json) => client.ReceiveAsync(
        new MqttApplicationMessageBuilder().WithTopic($"amr/{id}/{kind}").WithPayload(json).Build());
    private static async Task Data(FakeMqttClient client, string id, int x, string state = "IDLE")
    {
        await Send(client, id, "heartbeat", $$"""{"id":"{{id}}","ts":100,"status":"OK"}""");
        await Send(client, id, "telemetry", $$"""{"id":"{{id}}","ts":100,"pos":{"x":{{x}},"y":2},"sp":0,"state":"{{state}}"}""");
    }
    private static Task Ack(FakeMqttClient client, string id, string cmd, string result = "SUCCESS", string reason = "ARRIVED") =>
        Send(client, id, "ack", $$"""{"id":"{{id}}","ts":100,"cmdId":"{{cmd}}","result":"{{result}}","reason":"{{reason}}"}""");

    [Fact]
    public async Task FullSnapshotsPreserveBothDevicesDestinationsAndOneRevisionSpace()
    {
        await using var f = new Fixture();
        await f.Ready();
        Assert.NotEqual(f.A.Options.ClientId, f.B.Options.ClientId);
        Assert.Contains(f.Changes, x => x.Devices.IsEmpty && x.Connection!.RegisteredAmrIds.Length == 2);
        await f.Provider.SetDestinationAsync("A", new("new A", 40, 8));
        await Data(f.B, "B", 9, "BUSY");
        await MqttDataProviderTests.Until(() => f.Latest.Devices.Any(x => x.AmrId == "B" && x.X == 9) &&
            f.Latest.Devices.Any(x => x.AmrId == "A" && x.Journey?.Destination.X == 40));
        Assert.Equal(2, f.Latest.Devices.Length);
        Assert.Equal(20, f.Latest.Devices.Single(x => x.AmrId == "B").Journey!.Destination.X);
        Assert.All(f.Latest.Devices, x => Assert.Equal(f.Latest.Revision, x.Revision));
        Assert.Equal(f.Latest.Revision, (await f.Provider.GetDeviceAsync("B"))!.Revision);
    }

    [Fact]
    public async Task CommandsPublishOnlyToSelectedDeviceAndWrongDeviceAckCannotCompleteRequest()
    {
        await using var f = new Fixture(); await f.Ready();
        var a = await f.Provider.DispatchAsync(new("A", CommandKind.MOVE, 40, 2));
        var b = await f.Provider.DispatchAsync(new("B", CommandKind.MOVE, 60, 5));
        Assert.True(a.Accepted); Assert.True(b.Accepted);
        await MqttDataProviderTests.Until(() => f.A.PublishCalls == 1 && f.B.PublishCalls == 1);
        Assert.Equal("amr/A/command", f.A.PublishedMessage!.Topic);
        Assert.Equal("amr/B/command", f.B.PublishedMessage!.Topic);
        await Ack(f.B, "B", a.CommandId!); // B 토픽의 A 명령 ID로 A 요청을 완료하지 않는다.
        await Ack(f.B, "B", b.CommandId!);
        await MqttDataProviderTests.Until(() => f.Provider.GetCommandAsync("B", b.CommandId!).Result?.Result == CommandResult.Success);
        Assert.Equal(CommandResult.Unconfirmed, (await f.Provider.GetCommandAsync("A", a.CommandId!))!.Result);
        Assert.Null(await f.Provider.GetCommandAsync("B", a.CommandId!));
        await Ack(f.A, "A", a.CommandId!, "FAILURE", "FAULT_INTERRUPTED");
        await MqttDataProviderTests.Until(() => f.Latest.Commands.Any(x => x.CommandId == a.CommandId && x.Result == CommandResult.Failure));
        Assert.Equal(CommandResult.Success, f.Latest.Commands.Single(x => x.CommandId == b.CommandId).Result);
        Assert.False((await f.Provider.DispatchAsync(new("unknown", CommandKind.STOP))).Accepted);
    }

    [Fact]
    public async Task SameAlarmCodeFaultAndResetArePartitionedByDevice()
    {
        await using var f = new Fixture(); await f.Ready();
        foreach (var (client, id) in new[] { (f.A, "A"), (f.B, "B") })
            await Send(client, id, "alarm", $$"""{"id":"{{id}}","ts":100,"code":"SENSOR","level":"ERR","alarmState":"ACTIVE","message":"test"}""");
        await Data(f.A, "A", 1, "FAULT");
        await MqttDataProviderTests.Until(() => f.Latest.Alarms.Length == 2 && f.Latest.Devices.Single(x => x.AmrId == "A").State == AmrState.FAULT);
        var reset = await f.Provider.DispatchAsync(new("A", CommandKind.RESET));
        Assert.True(reset.Accepted);
        await Ack(f.A, "A", reset.CommandId!, "FAILURE", "FAULT_CAUSE_ACTIVE");
        await MqttDataProviderTests.Until(() => f.Latest.Commands.Any(x => x.CommandId == reset.CommandId && x.Result == CommandResult.Failure));
        Assert.All(f.Latest.Alarms, x => Assert.True(x.IsActive));
        var retry = await f.Provider.DispatchAsync(new("A", CommandKind.RESET)); Assert.True(retry.Accepted);
        await Send(f.A, "A", "alarm", """{"id":"A","ts":101,"code":"SENSOR","level":"ERR","alarmState":"CLEARED"}""");
        await Data(f.A, "A", 1); await Ack(f.A, "A", retry.CommandId!, "SUCCESS", "RESET_COMPLETED");
        await MqttDataProviderTests.Until(() => f.Latest.Commands.Any(x => x.CommandId == retry.CommandId && x.Result == CommandResult.Success));
        Assert.False(f.Latest.Alarms.Single(x => x.AmrId == "A").IsActive);
        Assert.True(f.Latest.Alarms.Single(x => x.AmrId == "B").IsActive);
        Assert.Equal(1, f.Latest.Devices.Single(x => x.AmrId == "A").X);
    }

    [Fact]
    public async Task OneSessionDisconnectDoesNotInvalidateOtherDeviceOrDiscardMarkers()
    {
        await using var f = new Fixture(); await f.Ready();
        f.A.IsConnected = false;
        await f.A.NotifyDisconnectedAsync(new(true, new(), MqttClientDisconnectReason.UnspecifiedError, "", [], null));
        await MqttDataProviderTests.Until(() => f.Latest.DeviceConnections["A"].State == DataConnectionState.Failed);
        Assert.Equal(2, f.Latest.Devices.Length);
        Assert.False(f.Latest.DeviceConnections["A"].TelemetryIsCurrent);
        Assert.True(f.Latest.DeviceConnections["B"].TelemetryIsCurrent);
        Assert.True((await f.Provider.DispatchAsync(new("B", CommandKind.MOVE, 30, 5))).Accepted);
        Assert.False((await f.Provider.DispatchAsync(new("A", CommandKind.MOVE, 30, 5))).Accepted);
    }

    [Fact]
    public async Task SlowSubscriberResynchronizesAndDisposeJoinsEveryOwnedClient()
    {
        await using var f = new Fixture(); await f.Ready();
        await using var slow = f.Provider.WatchAsync().GetAsyncEnumerator(); Assert.True(await slow.MoveNextAsync());
        // 느린 구독자만 64건을 초과한다.
        for (int i = 0; i < 80; i++)
        {
            await Data(f.A, "A", i + 10);
            await MqttDataProviderTests.Until(() => f.Latest.Devices.Single(x => x.AmrId == "A").X == i + 10);
        }
        await Assert.ThrowsAsync<AmrResyncRequiredException>(() => slow.MoveNextAsync().AsTask());
        await using var resumed = f.Provider.WatchAsync().GetAsyncEnumerator(); Assert.True(await resumed.MoveNextAsync());
        Assert.Equal(2, resumed.Current.Devices.Length);
        await f.Provider.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, f.A.DisposeCalls); Assert.Equal(1, f.B.DisposeCalls);
        Assert.Equal(0, f.A.MessageHandlerCount); Assert.Equal(0, f.B.DisconnectedHandlerCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => f.Provider.GetDevicesAsync());
    }

    [Theory]
    [InlineData("--amr-id", "A")]
    [InlineData("--simulator-config", "one.json")]
    [InlineData("--fleet-config", "second.json")]
    public void FleetOptionsRejectAmbiguousOrRepeatedInputs(string key, string value) =>
        Assert.Throws<ArgumentException>(() => MqttGuiOptions.Parse(["--mqtt", "--fleet-config", "fleet.json", key, value]));

    [Fact]
    public void FleetOptionsAcceptConfigAndKeepSingleMode()
    {
        Assert.Equal("fleet.json", MqttGuiOptions.Parse(["--mqtt", "--fleet-config", "fleet.json"]).FleetConfig);
        Assert.Null(MqttGuiOptions.Parse(["--mqtt", "--amr-id", "A"]).FleetConfig);
        Assert.Throws<ArgumentException>(() => new FleetMqttAmrDataProvider(new(), [new("A"), new("A")]));
    }
}
