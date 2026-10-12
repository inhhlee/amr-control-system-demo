using MQTTnet;
using MQTTnet.Diagnostics.PacketInspection;
using MQTTnet.Packets;

namespace Amr.Communication.Tests;

// 실제 네트워크 대신 시험이 승인·해제 완료 시점을 직접 결정한다.
internal sealed class FakeMqttClient : IMqttClient
{
    public TaskCompletionSource<MqttClientConnectResult> ConnectResponse { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource DisconnectCompletion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsConnected { get; set; }
    public MqttClientOptions Options { get; private set; } = new();
    public MqttClientDisconnectOptions? DisconnectOptions { get; private set; }
    public int DisconnectCalls { get; private set; }
    public int DisposeCalls { get; private set; }
    public TaskCompletionSource<MqttClientSubscribeResult> SubscribeResponse { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<MqttClientUnsubscribeResult> UnsubscribeResponse { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public MqttClientSubscribeOptions? SubscribeOptions { get; private set; }
    public MqttClientUnsubscribeOptions? UnsubscribeOptions { get; private set; }
    public int MessageHandlerCount => ApplicationMessageReceivedAsync?.GetInvocationList().Length ?? 0;

    public Task ReceiveAsync(MqttApplicationMessage message) => ApplicationMessageReceivedAsync?.Invoke(
        new MqttApplicationMessageReceivedEventArgs("test-client", message, new MqttPublishPacket(), null)) ?? Task.CompletedTask;

    public async Task<MqttClientConnectResult> ConnectAsync(MqttClientOptions options, CancellationToken cancellationToken)
    {
        Options = options;
        var result = await ConnectResponse.Task.WaitAsync(cancellationToken);
        IsConnected = result.ResultCode == MqttClientConnectResultCode.Success;
        return result;
    }

    public async Task DisconnectAsync(MqttClientDisconnectOptions options, CancellationToken cancellationToken)
    {
        DisconnectCalls++;
        DisconnectOptions = options;
        await DisconnectCompletion.Task.WaitAsync(cancellationToken);
        IsConnected = false;
    }

    public void Dispose()
    {
        DisposeCalls++;
        IsConnected = false;
    }

    public event Func<MqttApplicationMessageReceivedEventArgs, Task>? ApplicationMessageReceivedAsync;
    // 이번 시험에서는 사용하지 않는 MQTTnet 인터페이스 멤버다.
    public event Func<MqttClientConnectedEventArgs, Task>? ConnectedAsync { add { } remove { } }
    public event Func<MqttClientConnectingEventArgs, Task>? ConnectingAsync { add { } remove { } }
    public event Func<MqttClientDisconnectedEventArgs, Task>? DisconnectedAsync { add { } remove { } }
    public event Func<InspectMqttPacketEventArgs, Task>? InspectPacketAsync { add { } remove { } }

    public Task PingAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<MqttClientPublishResult> PublishAsync(MqttApplicationMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<MqttClientSubscribeResult> SubscribeAsync(MqttClientSubscribeOptions options, CancellationToken cancellationToken)
    {
        SubscribeOptions = options;
        return SubscribeResponse.Task.WaitAsync(cancellationToken);
    }
    public Task<MqttClientUnsubscribeResult> UnsubscribeAsync(MqttClientUnsubscribeOptions options, CancellationToken cancellationToken)
    {
        UnsubscribeOptions = options;
        return UnsubscribeResponse.Task.WaitAsync(cancellationToken);
    }
    public Task SendEnhancedAuthenticationExchangeDataAsync(MqttEnhancedAuthenticationExchangeData data, CancellationToken cancellationToken) => throw new NotSupportedException();
}
