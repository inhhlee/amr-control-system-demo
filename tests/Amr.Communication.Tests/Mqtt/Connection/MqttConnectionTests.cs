using Amr.Communication.Mqtt;
using MQTTnet;
using MQTTnet.Formatter;
using System.Net;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class MqttConnectionTests
{
    [Fact]
    public async Task Connect_WaitsForApprovalBeforeReportingConnected()
    {
        var client = new FakeMqttClient();
        using var connection = new MqttConnection(client);
        Assert.False(connection.IsConnected);

        Task pending = connection.ConnectAsync(new MqttConnectionSettings());
        // 라이브러리의 플래그만 먼저 바뀌어도 승인 결과를 기다려야 한다.
        client.IsConnected = true;
        Assert.False(pending.IsCompleted);
        Assert.False(connection.IsConnected);

        client.ConnectResponse.SetResult(new MqttClientConnectResult { ResultCode = MqttClientConnectResultCode.Success });
        await pending;
        Assert.True(connection.IsConnected);
    }

    [Fact]
    public async Task Connect_UsesRequestedEndpointAndDevelopmentSessionSettings()
    {
        var client = new FakeMqttClient();
        using var connection = new MqttConnection(client);
        var settings = new MqttConnectionSettings("test-broker", 2884, 7);
        Task pending = connection.ConnectAsync(settings);

        var tcp = Assert.IsType<MqttClientTcpOptions>(client.Options.ChannelOptions);
        var endpoint = Assert.IsType<DnsEndPoint>(tcp.RemoteEndpoint);
        Assert.Equal("test-broker", endpoint.Host);
        Assert.Equal(2884, endpoint.Port);
        Assert.Equal(settings.ClientId, client.Options.ClientId);
        Assert.Equal(MqttProtocolVersion.V500, client.Options.ProtocolVersion);
        Assert.True(client.Options.CleanSession);
        Assert.Equal(0u, client.Options.SessionExpiryInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), client.Options.KeepAlivePeriod);
        Assert.NotEqual(settings.ClientId, new MqttConnectionSettings().ClientId);

        client.ConnectResponse.SetResult(new MqttClientConnectResult { ResultCode = MqttClientConnectResultCode.Success });
        await pending;
    }

    [Fact]
    public async Task Connect_RejectsUnsuccessfulApprovalAndRemainsDisconnected()
    {
        var client = new FakeMqttClient();
        using var connection = new MqttConnection(client);
        Task pending = connection.ConnectAsync(new MqttConnectionSettings());

        client.ConnectResponse.SetResult(new MqttClientConnectResult { ResultCode = MqttClientConnectResultCode.NotAuthorized });
        var failure = await Assert.ThrowsAsync<MqttConnectionException>(() => pending);
        Assert.Equal(MqttConnectionFailure.MqttRejected, failure.Reason);
        Assert.Equal(MqttClientConnectResultCode.NotAuthorized, failure.ResultCode);
        Assert.False(connection.IsConnected);
        await connection.DisconnectAsync();
        Assert.Equal(0, client.DisconnectCalls);
    }

    [Fact]
    public async Task Connect_PropagatesFailureAndAllowsCleanup()
    {
        var client = new FakeMqttClient();
        var connection = new MqttConnection(client);
        Task pending = connection.ConnectAsync(new MqttConnectionSettings());

        client.ConnectResponse.SetException(new IOException("Test transport failure"));
        var failure = await Assert.ThrowsAsync<MqttConnectionException>(() => pending);
        Assert.Equal(MqttConnectionFailure.TransportFailure, failure.Reason);
        Assert.IsType<IOException>(failure.InnerException);
        Assert.False(connection.IsConnected);
        await connection.DisconnectAsync();
        connection.Dispose();
        Assert.Equal(0, client.DisconnectCalls);
        Assert.Equal(1, client.DisposeCalls);
    }

    [Fact]
    public async Task Disconnect_WaitsForCompletionAndRepeatedCleanupIsSafe()
    {
        var client = new FakeMqttClient();
        using var connection = new MqttConnection(client);
        Task connect = connection.ConnectAsync(new MqttConnectionSettings());
        client.ConnectResponse.SetResult(new MqttClientConnectResult { ResultCode = MqttClientConnectResultCode.Success });
        await connect;

        Task disconnect = connection.DisconnectAsync();
        Assert.False(disconnect.IsCompleted);
        Assert.True(connection.IsConnected);
        Assert.Equal(0, client.DisposeCalls);
        Assert.Equal(MqttClientDisconnectOptionsReason.NormalDisconnection, client.DisconnectOptions!.Reason);

        client.DisconnectCompletion.SetResult();
        await disconnect;
        Assert.False(connection.IsConnected);
        await connection.DisconnectAsync();
        connection.Dispose();
        connection.Dispose();
        await connection.DisconnectAsync();

        Assert.Equal(1, client.DisconnectCalls);
        Assert.Equal(1, client.DisposeCalls);
        Assert.False(connection.IsConnected);
    }

    [Fact]
    public async Task Cleanup_BeforeConnecting_IsSafeAndPreventsReuse()
    {
        var client = new FakeMqttClient();
        var connection = new MqttConnection(client);
        await connection.DisconnectAsync();
        connection.Dispose();
        connection.Dispose();

        Assert.False(connection.IsConnected);
        Assert.Equal(0, client.DisconnectCalls);
        Assert.Equal(1, client.DisposeCalls);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.ConnectAsync(new MqttConnectionSettings()));
    }
}
