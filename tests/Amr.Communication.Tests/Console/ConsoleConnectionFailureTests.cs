using Amr.Communication.ConsoleApp;
using MQTTnet;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class ConsoleConnectionFailureTests
{
    [Theory]
    [InlineData("--host", "")]
    [InlineData("--host", "   ")]
    [InlineData("--port", "0")]
    [InlineData("--port", "65536")]
    [InlineData("--connect-timeout-seconds", "0")]
    [InlineData("--connect-timeout-seconds", "-1")]
    [InlineData("--port", "not-a-number")]
    [InlineData("--connect-timeout-seconds", "999999999999")]
    public async Task InvalidInput_DoesNotCreateClientOrAttemptConnect(string option, string value)
    {
        var client = new FakeMqttClient();
        int factoryCalls = 0;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        int exit = await ConsoleRunner.RunAsync([option, value], () => { factoryCalls++; return client; },
            default, stdout, stderr);
        Assert.Equal(1, exit);
        Assert.Equal(0, factoryCalls);
        Assert.Equal(0, client.ConnectCalls);
        Assert.Contains("InputRejected Reason=InvalidSettings", stderr.ToString());
        Assert.DoesNotContain(" Connecting", stdout.ToString());
        Assert.DoesNotContain(" Connected", stdout.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialFailure_IsClassifiedAndNeverBecomesUserCancellation(bool mqttRejected)
    {
        var client = new FakeMqttClient();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Task<int> run = ConsoleRunner.RunAsync([], () => client, default, stdout, stderr);
        await client.ConnectEntered.Task;
        await client.NotifyDisconnectedAsync(new(false, null, MqttClientDisconnectReason.UnspecifiedError, null, null, null));
        if (mqttRejected)
            client.ConnectResponse.SetResult(new() { ResultCode = MqttClientConnectResultCode.NotAuthorized });
        else client.ConnectResponse.SetException(new IOException("DO_NOT_LOG_CREDENTIALS",
            new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused)));
        Assert.Equal(1, await run.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Contains(mqttRejected ? "Reason=MqttRejected ResultCode=NotAuthorized" : "Reason=TransportFailure", stderr.ToString());
        Assert.DoesNotContain("DO_NOT_LOG_CREDENTIALS", stderr.ToString());
        if (!mqttRejected) Assert.Contains("SocketError=ConnectionRefused", stderr.ToString());
        Assert.DoesNotContain("ConnectionLost", stdout.ToString());
        Assert.DoesNotContain("ConnectCanceled", stdout.ToString());
        AssertCleanFailure(client, stdout.ToString());
    }

    [Fact]
    public async Task Timeout_IsFailureEvenWhenUserCancelsAfterward()
    {
        var client = new FakeMqttClient { IgnoreConnectCancellation = true };
        using var user = new CancellationTokenSource();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Task<int> run = ConsoleRunner.RunAsync(["--connect-timeout-seconds", "1"], () => client, user.Token, stdout, stderr);
        Assert.Equal(1, await run.WaitAsync(TimeSpan.FromSeconds(4)));
        user.Cancel();
        client.ConnectResponse.SetResult(new() { ResultCode = MqttClientConnectResultCode.Success });
        await client.ConnectFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("ConnectTimeoutSeconds=1", stdout.ToString());
        Assert.Contains("ConnectFailed Reason=Timeout", stderr.ToString());
        Assert.DoesNotContain("ConnectCanceled", stdout.ToString());
        AssertCleanFailure(client, stdout.ToString());
    }

    [Fact]
    public async Task UserCancellation_IsExitZeroAndLateApprovalCannotLogConnected()
    {
        var client = new FakeMqttClient { IgnoreConnectCancellation = true };
        using var user = new CancellationTokenSource();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Task<int> run = ConsoleRunner.RunAsync(["--connect-timeout-seconds", "30"], () => client, user.Token, stdout, stderr);
        await client.ConnectEntered.Task;
        user.Cancel();
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(2)));
        client.ConnectResponse.SetResult(new() { ResultCode = MqttClientConnectResultCode.Success });
        await client.ConnectFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("ConnectCanceled Reason=UserCanceled", stdout.ToString());
        Assert.Equal("", stderr.ToString());
        AssertCleanFailure(client, stdout.ToString());
    }

    private static void AssertCleanFailure(FakeMqttClient client, string stdout)
    {
        Assert.DoesNotContain(" Connected", stdout);
        Assert.Contains("Disposed", stdout);
        Assert.False(client.IsConnected);
        Assert.Equal(1, client.DisposeCalls);
        Assert.Equal(0, client.MessageHandlerCount);
        Assert.Equal(0, client.DisconnectCalls);
    }
}
