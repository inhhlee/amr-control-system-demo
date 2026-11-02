using System.Collections.Concurrent;
using System.Buffers;
using System.Text.Json;
using Amr.Communication.Models;
using Amr.Communication.Mqtt;
using Amr.Communication.Tests;
using Amr.Core;
using Amr.Infrastructure;
using MQTTnet;
using Xunit;

namespace Amr.ControlGui.Tests;

public sealed class MqttControlTests
{
    [Fact]
    public async Task ResetRequiresFaultAndReceivedRecoveryEvidenceWithoutOptimisticClear()
    {
        var client = MqttDataProviderTests.Client();
        client.PublishResponse.SetResult(new(null, MqttClientPublishReasonCode.Success, null, []));
        await using var provider = new MqttAmrDataProvider(new(), clientFactory: () => client, destination: new("G", 20, 2));
        await using var watch = provider.WatchAsync().GetAsyncEnumerator();
        await Ready(provider, client, watch);
        await MqttDataProviderTests.Telemetry(client, 4, "BUSY");
        await MqttDataProviderTests.ReadUntil(watch, x => x.Devices[0].State == AmrState.BUSY);
        Assert.False((await provider.DispatchAsync(new("AMR_001", CommandKind.RESET))).Accepted);
        const string active = """{"id":"AMR_001","ts":1,"code":"DRIVE","level":"ERR","alarmState":"ACTIVE"}""";
        await MqttDataProviderTests.Send(client, "alarm", active);
        await MqttDataProviderTests.Telemetry(client, 4, "FAULT");
        await MqttDataProviderTests.ReadUntil(watch, x => x.Devices[0].State == AmrState.FAULT);
        Assert.False((await provider.DispatchAsync(new("AMR_001", CommandKind.MOVE, 20, 2))).Accepted);
        Assert.False((await provider.DispatchAsync(new("AMR_001", CommandKind.RESET, 0, 0))).Accepted);
        var early = await provider.DispatchAsync(new("AMR_001", CommandKind.RESET)); Assert.True(early.Accepted);
        await MqttDataProviderTests.Until(() => client.PublishCalls == 1);
        using (var json = JsonDocument.Parse(client.PublishedMessage!.Payload.ToArray()))
        {
            Assert.Equal("RESET", json.RootElement.GetProperty("type").GetString());
            Assert.Empty(json.RootElement.GetProperty("param").EnumerateObject());
        }
        Assert.False((await provider.DispatchAsync(new("AMR_001", CommandKind.RESET))).Accepted);
        await Ack(client, early.CommandId!, "FAILURE", "FAULT_CAUSE_ACTIVE");
        var failed = await MqttDataProviderTests.ReadUntil(watch, x => x.Commands.Any(c => c.CommandId == early.CommandId && c.Result == CommandResult.Failure));
        Assert.True(failed.Alarms[0].IsActive); Assert.Equal(AmrState.FAULT, failed.Devices[0].State);
        var reset = await provider.DispatchAsync(new("AMR_001", CommandKind.RESET)); Assert.True(reset.Accepted);
        await Ack(client, reset.CommandId!, reason: "RESET_COMPLETED");
        var ackOnly = await MqttDataProviderTests.ReadUntil(watch, x => x.Commands.Any(c => c.CommandId == reset.CommandId && c.Result == CommandResult.Success));
        Assert.True(ackOnly.Alarms[0].IsActive); Assert.Equal(AmrState.FAULT, ackOnly.Devices[0].State);
        await MqttDataProviderTests.Telemetry(client, 4, "IDLE");
        await MqttDataProviderTests.ReadUntil(watch, x => x.Devices[0].State == AmrState.IDLE);
        Assert.False((await provider.DispatchAsync(new("AMR_001", CommandKind.MOVE, 20, 2))).Accepted);
        await MqttDataProviderTests.Send(client, "alarm", active.Replace("ACTIVE", "CLEARED"));
        var cleared = await MqttDataProviderTests.ReadUntil(watch, x => !x.Alarms[0].IsActive);
        Assert.Equal((4d, 2d), (cleared.Devices[0].X, cleared.Devices[0].Y));
        Assert.True((await provider.DispatchAsync(new("AMR_001", CommandKind.MOVE, 20, 2))).Accepted);
    }

    private static async Task Ready(MqttAmrDataProvider provider, FakeMqttClient client, IAsyncEnumerator<AmrChange> watch)
    {
        provider.Start();
        await MqttDataProviderTests.ReadUntil(watch, x => x.Connection!.State == DataConnectionState.Connected);
        await MqttDataProviderTests.Telemetry(client, 0, "IDLE"); await MqttDataProviderTests.Heartbeat(client);
        await MqttDataProviderTests.ReadUntil(watch, x => x.Devices.FirstOrDefault()?.Liveness == LivenessObservation.Alive);
    }
    private static Task Ack(FakeMqttClient client, string id, string result = "SUCCESS", string reason = "ARRIVED") =>
        MqttDataProviderTests.Send(client, "ack", JsonSerializer.Serialize(new { id = "AMR_001", cmdId = id, result, reason, ts = 1 }));

    [Fact]
    public async Task FastAckIsMatchedBeforePublishReturnsAndTheRegisteredIdIsUsedOnWire()
    {
        var client = MqttDataProviderTests.Client();
        await using var provider = new MqttAmrDataProvider(new(), clientFactory: () => client, destination: new("G", 3, 2));
        await using var watch = provider.WatchAsync().GetAsyncEnumerator();
        await Ready(provider, client, watch);
        Assert.Null((await provider.GetDeviceAsync("AMR_001"))!.Journey!.TraveledDistance);
        _ = JsonSerializer.Serialize(await provider.GetDeviceAsync("AMR_001"));
        var request = await provider.DispatchAsync(new("AMR_001", CommandKind.MOVE, 3, 2));
        Assert.True(request.Accepted);
        await MqttDataProviderTests.Until(() => client.PublishCalls == 1);
        using (var payload = JsonDocument.Parse(client.PublishedMessage!.Payload.ToArray()))
            Assert.Equal(request.CommandId, payload.RootElement.GetProperty("cmdId").GetString());
        await Ack(client, request.CommandId!);
        await MqttDataProviderTests.ReadUntil(watch, x => x.Commands.FirstOrDefault()?.Result == CommandResult.Success);
        client.PublishResponse.SetResult(new(null, MqttClientPublishReasonCode.Success, null, []));
        var returned = await MqttDataProviderTests.ReadUntil(watch, x => x.Commands.FirstOrDefault()?.Publish == PublishObservation.Returned);
        Assert.Equal(CommandResult.Success, returned.Commands[0].Result);
        await Ack(client, request.CommandId!, "FAILURE", "CONFLICT");
        var conflict = await MqttDataProviderTests.ReadUntil(watch, x => x.Commands[0].HasConflictingResult);
        Assert.Equal(CommandResult.Success, conflict.Commands[0].Result);
    }

    [Fact]
    public async Task PendingPublicationsAreOrderedAndNeverForwardedToANewSession()
    {
        var first = MqttDataProviderTests.Client(); var second = MqttDataProviderTests.Client();
        second.PublishResponse.SetResult(new(null, MqttClientPublishReasonCode.Success, null, []));
        var clients = new ConcurrentQueue<FakeMqttClient>([first, second]);
        var events = new ConcurrentQueue<CommunicationSessionEvent>();
        await using var provider = new MqttAmrDataProvider(new(), reconnect: new() { Delay = TimeSpan.FromMilliseconds(10), MaxAttempts = 1 },
            report: events.Enqueue, clientFactory: () => clients.TryDequeue(out var client) ? client : throw new InvalidOperationException());
        await using var watch = provider.WatchAsync().GetAsyncEnumerator();
        await Ready(provider, first, watch);
        var move = await provider.DispatchAsync(new("AMR_001", CommandKind.MOVE, 3, 2));
        await MqttDataProviderTests.Until(() => first.PublishCalls == 1);
        var stop = await provider.DispatchAsync(new("AMR_001", CommandKind.STOP));
        Assert.True(stop.Accepted); Assert.Equal(1, first.PublishCalls);
        first.IsConnected = false;
        await first.NotifyDisconnectedAsync(new(true, new(), MqttClientDisconnectReason.UnspecifiedError, "", [], null));
        await MqttDataProviderTests.Until(() => events.Any(x => x.Name == "Reconnected"));
        await MqttDataProviderTests.ReadUntil(watch, x => x.Connection!.State == DataConnectionState.Connected && first.DisposeCalls == 1);
        await MqttDataProviderTests.Until(() => provider.GetCommandAsync("AMR_001", stop.CommandId!).Result?.Result == CommandResult.Failure);
        Assert.Equal(0, second.PublishCalls);
        Assert.Equal(PublishObservation.NotAttempted, (await provider.GetCommandAsync("AMR_001", stop.CommandId!))!.Publish);
        await MqttDataProviderTests.Telemetry(second, 1, "STOPPED"); await MqttDataProviderTests.Heartbeat(second);
        await MqttDataProviderTests.ReadUntil(watch, x => x.Connection!.TelemetryIsCurrent && x.Devices[0].Liveness == LivenessObservation.Alive && x.Devices[0].State == AmrState.STOPPED);
        var recovery = await provider.DispatchAsync(new("AMR_001", CommandKind.STOP));
        Assert.True(recovery.Accepted, recovery.Reason);
        await Ack(second, recovery.CommandId!, reason: "STOPPED");
        await MqttDataProviderTests.ReadUntil(watch, x => x.Commands.Any(c => c.CommandId == move.CommandId && c.SupersededByConfirmedStop));
        var fresh = await provider.DispatchAsync(new("AMR_001", CommandKind.MOVE, 4, 2));
        Assert.True(fresh.Accepted);
        Assert.Equal(CommandResult.Unconfirmed, (await provider.GetCommandAsync("AMR_001", move.CommandId!))!.Result);
    }

    [Fact]
    public async Task ActualAlarmAndHistoryAreReceivedAndFilteringDoesNotRemoveRecords()
    {
        var client = MqttDataProviderTests.Client();
        await using var provider = new MqttAmrDataProvider(new(), clientFactory: () => client);
        await using var watch = provider.WatchAsync().GetAsyncEnumerator();
        await Ready(provider, client, watch);
        Assert.Equal(new[] { "AMR_001" }, watch.Current.Connection!.RegisteredAmrIds);
        await MqttDataProviderTests.Send(client, "alarm", """{"id":"AMR_001","ts":1,"code":"A1","level":"WARN","alarmState":"ACTIVE","message":"TEST"}""");
        var raised = await MqttDataProviderTests.ReadUntil(watch, x => x.Alarms.Length == 1);
        Assert.True(raised.Alarms[0].IsActive); Assert.Equal(AlarmSeverity.Warning, raised.Alarms[0].Severity);
        await MqttDataProviderTests.Send(client, "alarm", """{"id":"AMR_001","ts":2,"code":"A1","level":"WARN","alarmState":"CLEARED","message":"TEST"}""");
        await MqttDataProviderTests.ReadUntil(watch, x => x.Alarms.Length == 1 && !x.Alarms[0].IsActive);
        var all = await provider.GetHistoryAsync(new());
        Assert.Equal(2, (await provider.GetHistoryAsync(new(Type: "알람"))).Entries.Length);
        Assert.Equal(all.Entries.Length, (await provider.GetHistoryAsync(new())).Entries.Length);
        Assert.Equal(2, (await provider.GetHistoryAsync(new(Type: "알람"))).Entries.Length);
    }
}
