using System.Text;
using System.Text.Json;
using Amr.Communication.Models;
using Amr.Communication.Mqtt;
using Amr.Communication.Validation;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class MessageSubscriptionTests
{
    private static readonly string[] Kinds = ["heartbeat", "telemetry", "alarm", "ack"];

    [Fact]
    public async Task Subscribe_RequiresAllFourApprovalsBeforeDelivery()
    {
        var client = new FakeMqttClient { IsConnected = true };
        var messages = new List<ReceivedMessage>();
        using var subscription = Create(client, messages.Add);
        var pending = subscription.SubscribeAsync();
        Assert.False(pending.IsCompleted);
        Assert.Equal(MessageSubscription.TopicFilters, client.SubscribeOptions!.TopicFilters.Select(x => x.Topic));
        Assert.All(client.SubscribeOptions.TopicFilters, x =>
        {
            Assert.Equal(MqttQualityOfServiceLevel.AtMostOnce, x.QualityOfServiceLevel);
            Assert.Equal(MqttRetainHandling.DoNotSendOnSubscribe, x.RetainHandling);
        });
        foreach (string kind in Kinds) await client.ReceiveAsync(Message(kind));
        Assert.Empty(messages);
        Approve(client);
        await pending;
        await client.ReceiveAsync(Message("heartbeat"));
        Assert.Single(messages);
        await Assert.ThrowsAsync<InvalidOperationException>(() => subscription.SubscribeAsync());
        Assert.Equal(1, client.MessageHandlerCount);
    }

    [Fact]
    public async Task TwoAmrs_AllKindsPreserveModelsAndCallbackTimeExactlyOnce()
    {
        var client = new FakeMqttClient { IsConnected = true };
        var messages = new List<ReceivedMessage>();
        var diagnostics = new List<MessageDiagnostic>();
        using var subscription = new MessageSubscription(client, messages.Add,
            _ => Assert.Fail("Valid input rejected"), _ => Assert.Fail("Callback failed"), diagnostics.Add);
        Approve(client);
        await subscription.SubscribeAsync();
        var before = DateTimeOffset.UtcNow;
        foreach (string kind in Kinds)
        foreach (string id in new[] { "AMR_001", "AMR_002" })
        {
            await client.ReceiveAsync(Message(kind, id));
            var actual = messages[^1];
            Assert.Equal(id, actual.AmrId);
            Assert.Equal(kind, actual.Kind);
            Assert.Equal(diagnostics[^1].ReceivedAtUtc, actual.ReceivedAtUtc);
            Assert.InRange(actual.ReceivedAtUtc, before, DateTimeOffset.UtcNow);
            object expected = kind switch
            {
                "heartbeat" => new HeartbeatMessage(id, 101, "OK", actual.ReceivedAtUtc),
                "telemetry" => new TelemetryMessage(id, 102, new(1.2, -3.4), 0.5, "BUSY", actual.ReceivedAtUtc),
                "alarm" => new AlarmMessage(id, 103, " A01 ", "WARN", "CLEARED", " 경고 ", actual.ReceivedAtUtc),
                _ => new AckMessage("CMD_1", "FAILURE", 104, null, " 이동 불가 ", actual.ReceivedAtUtc)
            };
            Assert.Equal(expected, actual.Model);
        }
        Assert.Equal(8, messages.Count);
        Assert.Equal(8, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Null(d.PayloadPreview));
        // 동일 ts의 별도 수신도 보존한다. 이 수신부는 MQTT 중복 제거 기능이 아니다.
        await client.ReceiveAsync(Message("heartbeat"));
        Assert.Equal(9, messages.Count);
    }

    public static IEnumerable<object[]> AckCases()
    {
        foreach (string id in new[] { "AMR_001", "장비-e\u0301" })
        foreach (string result in new[] { "SUCCESS", "FAILURE" })
        foreach (bool includeId in new[] { false, true })
        foreach (bool includeReason in new[] { false, true })
            yield return [id, result, includeId, includeReason];
    }

    [Theory]
    [MemberData(nameof(AckCases))]
    public async Task Ack_UsesTopicTargetWithoutChangingOptionalPayloadValues(
        string id, string result, bool includeId, bool includeReason)
    {
        var client = new FakeMqttClient { IsConnected = true };
        var messages = new List<ReceivedMessage>();
        using var subscription = Create(client, messages.Add);
        Approve(client);
        await subscription.SubscribeAsync();
        var fields = new Dictionary<string, object> { ["cmdId"] = " CMD_1 ", ["ts"] = long.MinValue, ["result"] = result };
        if (includeId) fields["id"] = id;
        if (includeReason) fields["reason"] = " 원형\n보존 ";
        await client.ReceiveAsync(Message("ack", id, JsonSerializer.Serialize(fields)));
        var envelope = Assert.Single(messages);
        Assert.Equal(id, envelope.AmrId);
        Assert.Equal(new AckMessage(" CMD_1 ", result, long.MinValue, includeId ? id : null,
            includeReason ? " 원형\n보존 " : null, envelope.ReceivedAtUtc), Assert.IsType<AckMessage>(envelope.Model));
    }

    public static IEnumerable<object[]> RejectionCases()
    {
        foreach (string kind in Kinds)
        foreach (string scenario in new[] { "missing", "mismatch", "utf8", "size", "json", "id", "trailing" })
            yield return [kind, scenario];
    }

    [Theory]
    [MemberData(nameof(RejectionCases))]
    public async Task InvalidInput_IsNeverDeliveredAndFollowingValidInputStillWorks(string kind, string scenario)
    {
        var client = new FakeMqttClient { IsConnected = true };
        var messages = new List<ReceivedMessage>();
        var errors = new List<ValidationError>();
        var diagnostics = new List<MessageDiagnostic>();
        using var subscription = new MessageSubscription(client, messages.Add, errors.Add,
            _ => Assert.Fail("Unexpected callback failure"), diagnostics.Add, true);
        Approve(client);
        await subscription.SubscribeAsync();
        string json = Payload(kind, "AMR_002");
        if (kind == "ack") json = json[..^1] + ",\"id\":\"AMR_002\"}";
        byte[] bytes = scenario switch
        {
            "missing" => Encoding.UTF8.GetBytes("{}"),
            "mismatch" => Encoding.UTF8.GetBytes(json),
            "utf8" => [0xff],
            "size" => new byte[65_537],
            "json" => Encoding.UTF8.GetBytes("private-payload"),
            "trailing" => Encoding.UTF8.GetBytes(Payload(kind, "AMR_001").PadRight(300) + "private-payload"),
            _ => Encoding.UTF8.GetBytes(Payload(kind, "AMR_001"))
        };
        string id = scenario == "id" ? "AMR 001" : "AMR_001";
        await client.ReceiveAsync(new MqttApplicationMessageBuilder().WithTopic($"amr/{id}/{kind}").WithPayload(bytes).Build());
        Assert.Empty(messages);
        var error = Assert.Single(errors);
        Assert.Equal(scenario switch
        {
            "missing" => ValidationErrorCode.MissingField, "mismatch" => ValidationErrorCode.IdMismatch,
            "utf8" => ValidationErrorCode.InvalidUtf8, "size" => ValidationErrorCode.PayloadTooLarge,
            "id" => ValidationErrorCode.InvalidTopic, _ => ValidationErrorCode.InvalidJson
        }, error.Code);
        Assert.DoesNotContain("private-payload", error.ToString());
        Assert.Null(Assert.Single(diagnostics).PayloadPreview);
        await client.ReceiveAsync(Message(kind, "AMR_001", new string(' ', 300) + Payload(kind, "AMR_001")));
        Assert.Single(messages);
        Assert.Single(errors);
        Assert.Equal(256, diagnostics[^1].PayloadPreview!.Length);
        Assert.True(diagnostics[^1].PayloadTruncated);
    }

    [Theory]
    [InlineData("amr/AMR_001/command")]
    [InlineData("amr/AMR_001/unknown")]
    [InlineData("amr/AMR_001/ACK")]
    [InlineData("AMR/AMR_001/ack")]
    [InlineData("amr/AMR_001/telemetry/extra")]
    [InlineData("amr/area/AMR_001/alarm")]
    public async Task OutsideFilters_IsIgnoredWithoutDiagnosticOrRejection(string topic)
    {
        var client = new FakeMqttClient { IsConnected = true };
        var messages = new List<ReceivedMessage>();
        using var subscription = new MessageSubscription(client, messages.Add,
            _ => Assert.Fail("Outside filter"), _ => Assert.Fail("Callback failure"), _ => Assert.Fail("Outside filter"));
        Approve(client);
        await subscription.SubscribeAsync();
        await client.ReceiveAsync(new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload("ignored").Build());
        Assert.Empty(messages);
    }

    [Theory]
    [InlineData("Received")]
    [InlineData("Rejected")]
    [InlineData("Diagnostic")]
    public async Task CallbackError_IsSeparateAndDoesNotPreventNextInput(string stage)
    {
        var client = new FakeMqttClient { IsConnected = true };
        var failures = new List<CallbackFailure>();
        int received = 0, rejected = 0, diagnostic = 0;
        using var subscription = new MessageSubscription(client,
            _ => { if (++received == 1 && stage == "Received") throw new InvalidOperationException("secret"); },
            _ => { if (++rejected == 1 && stage == "Rejected") throw new InvalidOperationException("secret"); }, failures.Add,
            _ => { if (++diagnostic == 1 && stage == "Diagnostic") throw new InvalidOperationException("secret"); });
        Approve(client);
        await subscription.SubscribeAsync();
        await client.ReceiveAsync(Message("ack", json: stage == "Rejected" ? "{}" : null));
        await client.ReceiveAsync(Message("ack"));
        Assert.Equal(new CallbackFailure(stage, "InvalidOperationException"), Assert.Single(failures));
        Assert.Equal(stage == "Rejected" ? 1 : 2, received);
        Assert.Equal(stage == "Rejected" ? 1 : 0, rejected);
    }

    [Fact]
    public async Task ErrorReporterException_PropagatesAsDocumented()
    {
        var client = new FakeMqttClient { IsConnected = true };
        using var subscription = new MessageSubscription(client, _ => throw new Exception("receiver"), _ => { },
            _ => throw new ApplicationException("reporter"));
        Approve(client);
        await subscription.SubscribeAsync();
        await Assert.ThrowsAsync<ApplicationException>(() => client.ReceiveAsync(Message("ack")));
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("missing")]
    [InlineData("qos1")]
    [InlineData("cancel")]
    [InlineData("exception")]
    public async Task FailedSubscription_StopsAllPossiblyInstalledFilters(string scenario)
    {
        var client = new FakeMqttClient { IsConnected = true };
        var messages = new List<ReceivedMessage>();
        using var subscription = Create(client, messages.Add);
        using var cancel = new CancellationTokenSource();
        Task pending = subscription.SubscribeAsync(cancel.Token);
        switch (scenario)
        {
            case "cancel": cancel.Cancel(); break;
            case "exception": client.SubscribeResponse.SetException(new IOException("lost response")); break;
            default: Approve(client, scenario); break;
        }
        await Assert.ThrowsAnyAsync<Exception>(() => pending);
        await client.ReceiveAsync(Message("heartbeat"));
        Assert.Empty(messages);
        CompleteUnsubscribe(client);
        await subscription.StopAsync();
        Assert.Equal(MessageSubscription.TopicFilters, client.UnsubscribeOptions!.TopicFilters);
        Assert.Equal(0, client.MessageHandlerCount);
        Assert.Equal(0, client.DisposeCalls);
    }

    [Fact]
    public async Task Stop_BlocksDeliveryDuringAndAfterCleanupAndAllowsNewSession()
    {
        var client = new FakeMqttClient { IsConnected = true };
        var messages = new List<ReceivedMessage>();
        using var subscription = Create(client, messages.Add);
        Approve(client);
        await subscription.SubscribeAsync();
        await client.ReceiveAsync(Message("alarm"));
        var stopping = subscription.StopAsync();
        Assert.False(stopping.IsCompleted);
        await client.ReceiveAsync(Message("alarm"));
        CompleteUnsubscribe(client);
        await stopping;
        await subscription.StopAsync();
        await client.ReceiveAsync(Message("alarm"));
        Assert.Single(messages);
        Assert.Equal(0, client.MessageHandlerCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => subscription.SubscribeAsync());
        using var next = Create(client, messages.Add);
        await next.SubscribeAsync();
        await client.ReceiveAsync(Message("alarm", "AMR_002"));
        Assert.Equal(2, messages.Count);
        Assert.Equal("AMR_002", messages[^1].AmrId);
        Assert.Equal(1, client.MessageHandlerCount);
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("missing")]
    [InlineData("exception")]
    public async Task FailedUnsubscribe_StillDetachesAndOwnerCanDisposeClient(string scenario)
    {
        var client = new FakeMqttClient { IsConnected = true };
        using var owner = new MqttConnection(client);
        using var subscription = Create(client, _ => Assert.Fail("Must be blocked"));
        Approve(client);
        await subscription.SubscribeAsync();
        if (scenario == "exception") client.UnsubscribeResponse.SetException(new IOException("lost response"));
        else CompleteUnsubscribe(client, scenario);
        await Assert.ThrowsAnyAsync<Exception>(() => subscription.StopAsync());
        Assert.Equal(0, client.MessageHandlerCount);
        await client.ReceiveAsync(Message("ack"));
        Assert.Equal(0, client.DisposeCalls);
        owner.Dispose();
        Assert.Equal(1, client.DisposeCalls);
    }

    [Fact]
    public async Task StopWithoutConnectionOrAttempt_OnlyDetaches()
    {
        var client = new FakeMqttClient();
        using var subscription = Create(client, _ => Assert.Fail("Must not receive"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => subscription.SubscribeAsync());
        await subscription.StopAsync();
        Assert.Null(client.UnsubscribeOptions);
        Assert.Equal(0, client.MessageHandlerCount);
        Assert.Equal(0, client.DisposeCalls);
    }

    private static MessageSubscription Create(FakeMqttClient client, Action<ReceivedMessage> received) =>
        new(client, received, _ => Assert.Fail("Unexpected rejection"), _ => Assert.Fail("Unexpected callback error"));

    private static MqttApplicationMessage Message(string kind, string id = "AMR_001", string? json = null) =>
        new MqttApplicationMessageBuilder().WithTopic($"amr/{id}/{kind}").WithPayload(json ?? Payload(kind, id)).Build();

    private static string Payload(string kind, string id) => kind switch
    {
        "heartbeat" => $$"""{"id":"{{id}}","ts":101,"status":"OK"}""",
        "telemetry" => $$"""{"id":"{{id}}","ts":102,"pos":{"x":1.2,"y":-3.4},"sp":0.5,"state":"BUSY"}""",
        "alarm" => $$"""{"id":"{{id}}","ts":103,"code":" A01 ","level":"WARN","alarmState":"CLEARED","message":" 경고 "}""",
        _ => """{"cmdId":"CMD_1","result":"FAILURE","ts":104,"reason":" 이동 불가 "}"""
    };

    private static void Approve(FakeMqttClient client, string scenario = "ok")
    {
        var items = MessageSubscription.TopicFilters.Select((topic, index) => new MqttClientSubscribeResultItem(
            new MqttTopicFilter { Topic = topic }, index == 2 ? scenario switch
            {
                "partial" => MqttClientSubscribeResultCode.NotAuthorized,
                "qos1" => MqttClientSubscribeResultCode.GrantedQoS1,
                _ => MqttClientSubscribeResultCode.GrantedQoS0
            } : MqttClientSubscribeResultCode.GrantedQoS0)).ToList();
        if (scenario == "missing") items.RemoveAt(3);
        client.SubscribeResponse.SetResult(new MqttClientSubscribeResult(1, items, "", []));
    }

    private static void CompleteUnsubscribe(FakeMqttClient client, string scenario = "ok")
    {
        var items = MessageSubscription.TopicFilters.Select((topic, index) => new MqttClientUnsubscribeResultItem(topic,
            scenario == "denied" && index == 1 ? MqttClientUnsubscribeResultCode.NotAuthorized
                : index % 2 == 0 ? MqttClientUnsubscribeResultCode.Success : MqttClientUnsubscribeResultCode.NoSubscriptionExisted)).ToList();
        if (scenario == "missing") items.RemoveAt(3);
        client.UnsubscribeResponse.SetResult(new MqttClientUnsubscribeResult(2, items, "", []));
    }
}
