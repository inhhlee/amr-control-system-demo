using Amr.Communication.Mqtt;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class HeartbeatSubscriptionTests
{
    [Fact]
    public async Task Subscribe_WaitsForQoS0ApprovalAndRequestsOnlyHeartbeat()
    {
        var client = new FakeMqttClient { IsConnected = true };
        var received = new List<HeartbeatDiagnostic>();
        using var subscription = new HeartbeatSubscription(client, received.Add, showPayload: true);
        Task pending = subscription.SubscribeAsync();
        Assert.False(pending.IsCompleted);
        var filter = Assert.Single(client.SubscribeOptions!.TopicFilters);
        Assert.Equal("amr/+/heartbeat", filter.Topic);
        Assert.Equal(MqttQualityOfServiceLevel.AtMostOnce, filter.QualityOfServiceLevel);
        Assert.Equal(MqttRetainHandling.DoNotSendOnSubscribe, filter.RetainHandling);
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", "before-approval"));
        Assert.Empty(received);
        Approve(client);
        await pending;
        var before = DateTimeOffset.UtcNow;
        const string payload = "{\"id\":\"AMR_001\",\"ts\":1791195767000,\"status\":\"OK\"}";
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", payload));
        var diagnostic = Assert.Single(received);
        Assert.Equal(payload, diagnostic.PayloadPreview);
        Assert.Equal("amr/AMR_001/heartbeat", diagnostic.Topic);
        Assert.InRange(diagnostic.ReceivedAtUtc, before, DateTimeOffset.UtcNow);
        Assert.False(diagnostic.PayloadTruncated);
    }

    [Theory]
    [InlineData("amr/AMR_001/telemetry")]
    [InlineData("amr/AMR_001/alarm")]
    [InlineData("amr/AMR_001/ack")]
    [InlineData("amr/AMR_001/heartbeat/extra")]
    [InlineData("amr/area/AMR_001/heartbeat")]
    [InlineData("other/AMR_001/heartbeat")]
    [InlineData("amr/AMR_001/Heartbeat")]
    public async Task Receive_IgnoresTopicsOutsideFilterAndContinuesReceiving(string topic)
    {
        var client = new FakeMqttClient { IsConnected = true };
        var received = new List<HeartbeatDiagnostic>();
        using var subscription = new HeartbeatSubscription(client, received.Add);
        Approve(client);
        await subscription.SubscribeAsync();
        await client.ReceiveAsync(Message(topic, "outside-filter"));
        Assert.Empty(received);
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", "not-json"));
        // 형식 검사는 이번 범위가 아니다. 기본 진단에는 원문이 노출되지 않는다.
        var diagnostic = Assert.Single(received);
        Assert.Null(diagnostic.PayloadPreview);
        Assert.Equal(8, diagnostic.PayloadBytes);
    }

    [Fact]
    public async Task Subscribe_RejectsFailureAndCanDetachHandler()
    {
        var client = new FakeMqttClient { IsConnected = true };
        var received = new List<HeartbeatDiagnostic>();
        using var subscription = new HeartbeatSubscription(client, received.Add);
        Approve(client, MqttClientSubscribeResultCode.NotAuthorized);
        await Assert.ThrowsAsync<InvalidOperationException>(() => subscription.SubscribeAsync());
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", "ignored"));
        Assert.Empty(received);
        await subscription.StopAsync();
        Assert.Equal(0, client.MessageHandlerCount);
        Assert.Null(client.UnsubscribeOptions);
    }

    [Fact]
    public async Task Stop_WaitsForUnsubscribeAndPreventsFurtherDiagnostics()
    {
        var client = new FakeMqttClient { IsConnected = true };
        var received = new List<HeartbeatDiagnostic>();
        using var subscription = new HeartbeatSubscription(client, received.Add);
        Approve(client);
        await subscription.SubscribeAsync();
        Task pending = subscription.StopAsync();
        Assert.False(pending.IsCompleted);
        Assert.Equal("amr/+/heartbeat", Assert.Single(client.UnsubscribeOptions!.TopicFilters));
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", "during-stop"));
        Assert.Empty(received);
        CompleteUnsubscribe(client);
        await pending;
        await subscription.StopAsync();
        subscription.Dispose();
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", "after-stop"));
        Assert.Empty(received);
        Assert.Equal(0, client.MessageHandlerCount);
        Assert.Equal(0, client.DisposeCalls); // 연결 객체가 Client를 소유한다.
        await Assert.ThrowsAsync<ObjectDisposedException>(() => subscription.SubscribeAsync());
    }

    [Fact]
    public async Task Stop_OnRejectedUnsubscribeStillDetachesHandler()
    {
        var client = new FakeMqttClient { IsConnected = true };
        using var subscription = new HeartbeatSubscription(client, _ => { });
        Approve(client);
        await subscription.SubscribeAsync();
        CompleteUnsubscribe(client, MqttClientUnsubscribeResultCode.NotAuthorized);
        await Assert.ThrowsAsync<InvalidOperationException>(() => subscription.StopAsync());
        Assert.Equal(0, client.MessageHandlerCount);
    }

    [Fact]
    public async Task Receive_CapsOptInPayloadPreviewAndKeepsOriginalByteCount()
    {
        var client = new FakeMqttClient { IsConnected = true };
        var received = new List<HeartbeatDiagnostic>();
        using var subscription = new HeartbeatSubscription(client, received.Add, showPayload: true);
        Approve(client);
        await subscription.SubscribeAsync();
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", new string('x', 10000)));
        var diagnostic = Assert.Single(received);
        Assert.Equal(10000, diagnostic.PayloadBytes);
        Assert.Equal(new string('x', 256), diagnostic.PayloadPreview);
        Assert.True(diagnostic.PayloadTruncated);
    }

    private static MqttApplicationMessage Message(string topic, string payload) =>
        new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce).Build();

    private static void Approve(FakeMqttClient client, MqttClientSubscribeResultCode code = MqttClientSubscribeResultCode.GrantedQoS0) =>
        client.SubscribeResponse.SetResult(new MqttClientSubscribeResult(1,
            [new MqttClientSubscribeResultItem(new MqttTopicFilter { Topic = HeartbeatSubscription.TopicFilter }, code)], "", []));

    private static void CompleteUnsubscribe(FakeMqttClient client, MqttClientUnsubscribeResultCode code = MqttClientUnsubscribeResultCode.Success) =>
        client.UnsubscribeResponse.SetResult(new MqttClientUnsubscribeResult(2,
            [new MqttClientUnsubscribeResultItem(HeartbeatSubscription.TopicFilter, code)], "", []));
}
