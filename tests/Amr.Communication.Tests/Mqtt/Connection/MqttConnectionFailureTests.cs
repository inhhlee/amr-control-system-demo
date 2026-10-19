using System.Diagnostics;
using Amr.Communication.Mqtt;
using MQTTnet;
using Xunit;
using Xunit.Abstractions;

namespace Amr.Communication.Tests;

public sealed class MqttConnectionFailureTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Timeout_BoundsUncooperativeConnectAndRejectsLateApproval(int seconds)
    {
        var client = new FakeMqttClient { IgnoreConnectCancellation = true };
        using var connection = new MqttConnection(client);
        var elapsed = Stopwatch.StartNew();
        Task pending = connection.ConnectAsync(new MqttConnectionSettings(connectTimeoutSeconds: seconds));
        var failure = await Assert.ThrowsAsync<MqttConnectionException>(() => pending.WaitAsync(TimeSpan.FromSeconds(seconds + 3)));
        elapsed.Stop();
        output.WriteLine($"TimeoutSeconds={seconds} ElapsedMs={elapsed.Elapsed.TotalMilliseconds:F1} AllowedMs=[{seconds * 1000 - 50},{seconds * 1000 + 2000}]");
        Assert.Equal(MqttConnectionFailure.Timeout, failure.Reason);
        Assert.InRange(elapsed.Elapsed.TotalMilliseconds, seconds * 1000 - 50, seconds * 1000 + 2000);
        Assert.False(connection.IsConnected);
        Assert.Equal(1, client.DisposeCalls);
        client.ConnectResponse.SetResult(new() { ResultCode = MqttClientConnectResultCode.Success });
        await client.ConnectFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(connection.IsConnected);
        Assert.False(client.IsConnected);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.ConnectAsync(new()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UserCancellation_BoundsUncooperativeConnectAndObservesLateResult(bool lateFailure)
    {
        var client = new FakeMqttClient { IgnoreConnectCancellation = true };
        using var connection = new MqttConnection(client);
        using var user = new CancellationTokenSource();
        Task pending = connection.ConnectAsync(new(connectTimeoutSeconds: 30), user.Token);
        await client.ConnectEntered.Task;
        user.Cancel();
        var canceled = await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(user.Token, canceled.CancellationToken);
        Assert.False(connection.IsConnected);
        Assert.Equal(1, client.DisposeCalls);
        if (lateFailure) client.ConnectResponse.SetException(new IOException("late transport failure"));
        else client.ConnectResponse.SetResult(new() { ResultCode = MqttClientConnectResultCode.Success });
        await client.ConnectFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(connection.IsConnected);
        Assert.False(client.IsConnected);
        connection.Dispose();
        Assert.Equal(1, client.DisposeCalls);
    }

    [Fact]
    public async Task Cancellation_JustBeforeApprovalReturns_DoesNotReportConnected()
    {
        using var user = new CancellationTokenSource();
        var client = new FakeMqttClient { BeforeConnectReturn = user.Cancel };
        using var connection = new MqttConnection(client);
        Task pending = connection.ConnectAsync(new(), user.Token);
        client.ConnectResponse.SetResult(new() { ResultCode = MqttClientConnectResultCode.Success });
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.False(connection.IsConnected);
        Assert.False(client.IsConnected);
        Assert.Equal(1, client.DisposeCalls);
    }

    [Fact]
    public async Task AlreadyCanceled_DoesNotStartNetworkAttempt()
    {
        using var user = new CancellationTokenSource();
        user.Cancel();
        var client = new FakeMqttClient();
        using var connection = new MqttConnection(client);
        await Assert.ThrowsAsync<OperationCanceledException>(() => connection.ConnectAsync(new(), user.Token));
        Assert.Equal(0, client.ConnectCalls);
        Assert.Equal(1, client.DisposeCalls);
    }

    [Fact]
    public async Task UnrequestedOperationCanceled_IsTransportFailure()
    {
        var client = new FakeMqttClient();
        using var connection = new MqttConnection(client);
        Task pending = connection.ConnectAsync(new());
        client.ConnectResponse.SetException(new OperationCanceledException());
        var failure = await Assert.ThrowsAsync<MqttConnectionException>(() => pending);
        Assert.Equal(MqttConnectionFailure.TransportFailure, failure.Reason);
        Assert.Equal(1, client.DisposeCalls);
    }
}
