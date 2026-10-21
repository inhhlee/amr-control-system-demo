using Amr.Communication.Mqtt;
using Amr.Simulator.Configuration;
using Amr.Simulator.Simulation;
using Amr.Simulator.Telemetry;
using MQTTnet;
using MQTTnet.Diagnostics.PacketInspection;
using Xunit;
using static Amr.Simulator.Tests.ConfigurationTests;

namespace Amr.Simulator.Tests;

public sealed class MqttTransportConnectionTests
{
    [Theory]
    [InlineData(true, "ConnectionLost")]
    [InlineData(false, "MqttConnectionException")]
    public async Task DisconnectNotificationDuringConnect_DistinguishesEstablishedLossFromFailedAttempt(
        bool clientWasConnected, string expectedReason)
    {
        using var config = new TemporaryConfig("""
            {"amrId":"AMR_001","initialPosition":{"x":0,"y":0},"speedMps":1,
             "updateIntervalMs":100,"waypoints":[{"x":1,"y":0}]}
            """);
        var amr = new SimulatedAmr(await SimulatorSettingsLoader.LoadAsync(config.FilePath));
        var client = new DisconnectingClient(clientWasConnected);
        var settings = new MqttConnectionSettings();
        var transport = new MqttSimulationTransport(settings, client);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int code = await MqttSimulationSession.RunAsync(amr, new(settings, 20, 30), transport,
            output, error, default, new TestSimulationClock(10)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, code);
        Assert.Contains($"Reason={expectedReason}", error.ToString());
        if (!clientWasConnected) Assert.DoesNotContain("Reason=ConnectionLost", error.ToString());
        Assert.DoesNotContain("MqttConnected", output.ToString());
        Assert.DoesNotContain("SimulationStarted", output.ToString());
        Assert.Equal(0, client.PublishCalls);
        Assert.Equal(1, client.DisposeCalls);
        Assert.Equal(0, client.DisconnectedHandlerCount);
    }

    // MQTTnet의 알림 순서를 고정해 네트워크 타이밍에 의존하지 않고 접속 경계를 재현한다.
    private sealed class DisconnectingClient(bool clientWasConnected) : IMqttClient
    {
        public bool IsConnected { get; private set; }
        public MqttClientOptions Options { get; private set; } = new();
        public int PublishCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public int DisconnectedHandlerCount => DisconnectedAsync?.GetInvocationList().Length ?? 0;

        public async Task<MqttClientConnectResult> ConnectAsync(MqttClientOptions options, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Options = options;
            IsConnected = false;
            if (DisconnectedAsync is { } disconnected)
                await disconnected(new MqttClientDisconnectedEventArgs(clientWasConnected, null,
                    MqttClientDisconnectReason.UnspecifiedError, null, null, null));
            return new MqttClientConnectResult { ResultCode = clientWasConnected
                ? MqttClientConnectResultCode.Success : MqttClientConnectResultCode.ServerUnavailable };
        }

        public Task<MqttClientPublishResult> PublishAsync(MqttApplicationMessage message, CancellationToken token)
        {
            PublishCalls++;
            throw new InvalidOperationException("A disconnected client cannot publish.");
        }
        public void Dispose() { DisposeCalls++; IsConnected = false; }
        public Task DisconnectAsync(MqttClientDisconnectOptions options, CancellationToken token) => Task.CompletedTask;
        public event Func<MqttClientDisconnectedEventArgs, Task>? DisconnectedAsync;
        public event Func<MqttApplicationMessageReceivedEventArgs, Task>? ApplicationMessageReceivedAsync { add { } remove { } }
        public event Func<MqttClientConnectedEventArgs, Task>? ConnectedAsync { add { } remove { } }
        public event Func<MqttClientConnectingEventArgs, Task>? ConnectingAsync { add { } remove { } }
        public event Func<InspectMqttPacketEventArgs, Task>? InspectPacketAsync { add { } remove { } }
        public Task PingAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<MqttClientSubscribeResult> SubscribeAsync(MqttClientSubscribeOptions options, CancellationToken token) => throw new NotSupportedException();
        public Task<MqttClientUnsubscribeResult> UnsubscribeAsync(MqttClientUnsubscribeOptions options, CancellationToken token) => throw new NotSupportedException();
        public Task SendEnhancedAuthenticationExchangeDataAsync(MqttEnhancedAuthenticationExchangeData data, CancellationToken token) => throw new NotSupportedException();
    }
}
