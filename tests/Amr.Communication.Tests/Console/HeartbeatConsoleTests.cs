using Amr.Communication.ConsoleApp;
using Amr.Communication.Mqtt;
using MQTTnet;
using MQTTnet.Packets;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class HeartbeatConsoleTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("invalid")]
    [InlineData("unknown")]
    public async Task InvalidMonitorArgumentsNeverCreateClient(string scenario)
    {
        string[] args = scenario switch {
            "missing" => ["--monitor-amr"],
            "duplicate" => ["--monitor-amr", "ONE", "--monitor-amr", "TWO"],
            "invalid" => ["--monitor-amr", "bad/id"],
            _ => ["--monitor-amr", "ONE", "--unknown", "1"]
        };
        var output = new StringWriter();
        var error = new StringWriter();
        int calls = 0;
        var result = await ConsoleRunner.RunAsync(args, () => { calls++; return new FakeMqttClient(); },
            default, output, error);
        Assert.Equal(1, result);
        Assert.Equal(0, calls);
        Assert.Contains("InputRejected", error.ToString());
    }

    [Fact]
    public async Task MonitorModeRunsFormalSessionWithoutMapAndCancelsNormally()
    {
        var client = new FakeMqttClient();
        client.ConnectResponse.SetResult(new() { ResultCode = MqttClientConnectResultCode.Success });
        client.SubscribeResponse.SetResult(new(1, MessageSubscription.TopicFilters.Select(topic =>
            new MqttClientSubscribeResultItem(new MqttTopicFilter { Topic = topic }, MqttClientSubscribeResultCode.GrantedQoS0)).ToList(), "", []));
        client.UnsubscribeResponse.SetResult(new(2, MessageSubscription.TopicFilters.Select(topic =>
            new MqttClientUnsubscribeResultItem(topic, MqttClientUnsubscribeResultCode.Success)).ToList(), "", []));
        client.DisconnectCompletion.SetResult();
        using var owner = new CancellationTokenSource();
        var output = new StringWriter();
        var error = new StringWriter();
        var completion = ConsoleRunner.RunAsync(["--monitor-amr", "ONE"], () => client, owner.Token, output, error);
        // Pre-completed transport approvals make this runner reach its shutdown wait synchronously.
        Assert.Contains("Subscribed", output.ToString());
        await client.ReceiveAsync(new MqttApplicationMessageBuilder().WithTopic("amr/ONE/heartbeat")
            .WithPayload("""{"id":"ONE","ts":123,"status":"OK"}""").Build());
        Assert.Contains("HeartbeatMonitoringStarted", output.ToString());
        owner.Cancel();
        Assert.Equal(0, await completion.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Contains("Closed", output.ToString());
        Assert.Empty(error.ToString());
        Assert.Equal(1, client.DisposeCalls);
        Assert.Equal(0, client.MessageHandlerCount);
        Assert.Equal(0, client.DisconnectedHandlerCount);
    }
}
