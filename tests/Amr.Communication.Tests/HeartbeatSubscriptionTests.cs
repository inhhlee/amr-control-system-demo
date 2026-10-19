using Amr.Communication.Mqtt;
using Amr.Communication.Models;
using Amr.Communication.Validation;
using System.Text;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class HeartbeatSubscriptionTests
{
    private const string ValidJson = "{\"id\":\"AMR_001\",\"ts\":1791195767000,\"status\":\"OK\"}";

    [Fact]
    public async Task Subscribe_WaitsForQoS0ApprovalAndRequestsOnlyHeartbeat()
    {
        var client = new FakeMqttClient { IsConnected = true };
        var received = new List<HeartbeatDiagnostic>();
        using var subscription = CreateSubscription(client, received.Add, showPayload: true);
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
        using var subscription = CreateSubscription(client, received.Add);
        Approve(client);
        await subscription.SubscribeAsync();
        await client.ReceiveAsync(Message(topic, "outside-filter"));
        Assert.Empty(received);
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", "not-json"));
        // 필터 안의 불량 입력은 거부 진단만 남기며 원문은 노출되지 않는다.
        var diagnostic = Assert.Single(received);
        Assert.Null(diagnostic.PayloadPreview);
        Assert.Equal(8, diagnostic.PayloadBytes);
    }

    [Fact]
    public async Task Subscribe_RejectsFailureAndCanDetachHandler()
    {
        var client = new FakeMqttClient { IsConnected = true };
        var received = new List<HeartbeatDiagnostic>();
        using var subscription = CreateSubscription(client, received.Add);
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
        using var subscription = CreateSubscription(client, received.Add);
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
        using var subscription = CreateSubscription(client, _ => { });
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
        using var subscription = CreateSubscription(client, received.Add, showPayload: true);
        Approve(client);
        await subscription.SubscribeAsync();
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", ValidJson.PadRight(10000)));
        var diagnostic = Assert.Single(received);
        Assert.Equal(10000, diagnostic.PayloadBytes);
        Assert.Equal(ValidJson.PadRight(256), diagnostic.PayloadPreview);
        Assert.True(diagnostic.PayloadTruncated);
    }

    [Fact]
    public async Task Receive_ValidModelOncePerCallbackAndSameReceptionTimeAsDiagnostic()
    {
        var client = new FakeMqttClient { IsConnected = true };
        var models = new List<HeartbeatMessage>();
        var diagnostics = new List<HeartbeatDiagnostic>();
        var rejected = new List<ValidationError>();
        var receiverErrors = new List<string>();
        using var subscription = new HeartbeatSubscription(client, models.Add, rejected.Add,
            receiverErrors.Add, diagnostics.Add, showPayload: true);
        Task pending = subscription.SubscribeAsync();
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", ValidJson));
        Assert.Empty(models);
        Assert.Empty(diagnostics);
        Approve(client);
        await pending;

        // 앞 256바이트의 미리보기만으로는 정상 JSON을 해석할 수 없는 입력이다.
        string longJson = new string(' ', 300) + ValidJson;
        var before = DateTimeOffset.UtcNow;
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", longJson));
        var model = Assert.Single(models);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("AMR_001", model.Id);
        Assert.Equal(1791195767000, model.Timestamp);
        Assert.Equal("OK", model.Status);
        Assert.Equal(diagnostic.ReceivedAtUtc, model.ReceivedAtUtc);
        Assert.InRange(model.ReceivedAtUtc, before, DateTimeOffset.UtcNow);
        Assert.Equal(TimeSpan.Zero, model.ReceivedAtUtc.Offset);
        Assert.True(diagnostic.IsValid);
        Assert.True(diagnostic.PayloadTruncated);
        Assert.Empty(rejected);
        Assert.Empty(receiverErrors);

        // 같은 ts라도 다른 수신 콜백이면 한 번씩 전달한다. MQTT 중복 제거 기능은 아니다.
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", ValidJson));
        Assert.Equal(2, models.Count);
        CompleteUnsubscribe(client);
        await subscription.StopAsync();
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", ValidJson));
        Assert.Equal(2, models.Count);
    }

    [Theory]
    [InlineData("{\"id\":\"AMR_001\",\"status\":\"OK\"}", ValidationErrorCode.MissingField, "$.ts")]
    [InlineData("{\"id\":\"AMR_002\",\"ts\":1,\"status\":\"OK\"}", ValidationErrorCode.IdMismatch, "$.id")]
    [InlineData("secret-not-json", ValidationErrorCode.InvalidJson, "$")]
    [InlineData("[]", ValidationErrorCode.InvalidType, "$")]
    [InlineData("{\"id\":\"AMR_001\",\"ts\":1,\"status\":\"OK\",\"secret-name\":\"secret-value\"}", ValidationErrorCode.UnexpectedField, "$")]
    public async Task Receive_RejectionDoesNotCallReceiverAndNextValidMessageIsDelivered(
        string json, ValidationErrorCode code, string field)
    {
        var client = new FakeMqttClient { IsConnected = true };
        var models = new List<HeartbeatMessage>();
        var rejected = new List<ValidationError>();
        var diagnostics = new List<HeartbeatDiagnostic>();
        using var subscription = new HeartbeatSubscription(client, models.Add, rejected.Add,
            _ => Assert.Fail("Receiver must not fail."), diagnostics.Add, showPayload: true);
        Approve(client);
        await subscription.SubscribeAsync();
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", json));
        Assert.Empty(models);
        var error = Assert.Single(rejected);
        Assert.Equal(code, error.Code);
        Assert.Equal(field, error.Field);
        Assert.DoesNotContain("secret", error.ToString());
        var diagnostic = Assert.Single(diagnostics);
        Assert.False(diagnostic.IsValid);
        Assert.Null(diagnostic.PayloadPreview);
        Assert.False(diagnostic.PayloadTruncated);

        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", ValidJson));
        Assert.Single(models);
        Assert.Single(rejected);
        Assert.Equal(2, diagnostics.Count);
    }

    [Fact]
    public async Task Receive_UsesFullOriginalBytesForUtf8SizeAndTrailingContent()
    {
        var client = new FakeMqttClient { IsConnected = true };
        var models = new List<HeartbeatMessage>();
        var rejected = new List<ValidationError>();
        using var subscription = new HeartbeatSubscription(client, models.Add, rejected.Add, _ => { });
        Approve(client);
        await subscription.SubscribeAsync();
        byte[][] badPayloads =
        [
            [.. Encoding.UTF8.GetBytes(ValidJson), 0xff],
            Encoding.UTF8.GetBytes(ValidJson.PadRight(300) + "garbage"),
            Encoding.UTF8.GetBytes(ValidJson.PadRight(65_537))
        ];
        foreach (byte[] payload in badPayloads)
        {
            await client.ReceiveAsync(new MqttApplicationMessageBuilder()
                .WithTopic("amr/AMR_001/heartbeat").WithPayload(payload).Build());
        }
        Assert.Empty(models);
        Assert.Equal(new[] { ValidationErrorCode.InvalidUtf8, ValidationErrorCode.InvalidJson,
            ValidationErrorCode.PayloadTooLarge }, rejected.Select(error => error.Code));
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", ValidJson.PadRight(65_536)));
        Assert.Single(models);
    }

    [Fact]
    public async Task Receive_ReceiverExceptionIsSeparateFromInputRejectionAndDoesNotRetry()
    {
        var client = new FakeMqttClient { IsConnected = true };
        var rejected = new List<ValidationError>();
        var receiverErrors = new List<string>();
        int calls = 0;
        using var subscription = new HeartbeatSubscription(client, _ =>
        {
            calls++;
            if (calls == 1) throw new InvalidOperationException("secret-receiver-detail");
        }, rejected.Add, receiverErrors.Add);
        Approve(client);
        await subscription.SubscribeAsync();
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", ValidJson));
        Assert.Equal(1, calls);
        Assert.Equal("InvalidOperationException", Assert.Single(receiverErrors));
        Assert.Empty(rejected);
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", ValidJson));
        Assert.Equal(2, calls);
        Assert.Single(receiverErrors);
        Assert.Empty(rejected);
    }

    [Fact]
    public async Task Receive_RejectsInvalidIdInsideSubscriptionFilter()
    {
        var client = new FakeMqttClient { IsConnected = true };
        var models = new List<HeartbeatMessage>();
        var rejected = new List<ValidationError>();
        using var subscription = new HeartbeatSubscription(client, models.Add, rejected.Add, _ => { });
        Approve(client);
        await subscription.SubscribeAsync();
        await client.ReceiveAsync(Message("amr/AMR 001/heartbeat", ValidJson));
        Assert.Equal(ValidationErrorCode.InvalidTopic, Assert.Single(rejected).Code);
        Assert.Empty(models);
        await client.ReceiveAsync(Message("amr/AMR_001/heartbeat", ValidJson));
        Assert.Single(models);
    }

    private static HeartbeatSubscription CreateSubscription(
        FakeMqttClient client, Action<HeartbeatDiagnostic> diagnostic, bool showPayload = false) =>
        new(client, _ => { }, _ => { }, _ => { }, diagnostic, showPayload);

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
