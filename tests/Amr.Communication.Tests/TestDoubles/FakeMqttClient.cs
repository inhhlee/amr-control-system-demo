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
    public int ConnectCalls { get; private set; }
    public bool IgnoreConnectCancellation { get; init; }
    public Action? BeforeConnectReturn { get; set; }
    public TaskCompletionSource ConnectEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ConnectFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<MqttClientSubscribeResult> SubscribeResponse { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource SubscribeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
        ConnectCalls++;
        ConnectEntered.TrySetResult();
        try
        {
            var result = await ConnectResponse.Task.WaitAsync(IgnoreConnectCancellation ? CancellationToken.None : cancellationToken);
            // Dispose는 실제 Client처럼 전송 자원을 닫는다. 완료 통지만 늦게 돌아올 수 있다.
            IsConnected = DisposeCalls == 0 && result.ResultCode == MqttClientConnectResultCode.Success;
            BeforeConnectReturn?.Invoke();
            return result;
        }
        finally { ConnectFinished.TrySetResult(); }
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
    public event Func<MqttClientDisconnectedEventArgs, Task>? DisconnectedAsync;
    public Task NotifyDisconnectedAsync(MqttClientDisconnectedEventArgs args) =>
        DisconnectedAsync?.Invoke(args) ?? Task.CompletedTask;
    public event Func<InspectMqttPacketEventArgs, Task>? InspectPacketAsync { add { } remove { } }

    public Task PingAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    public TaskCompletionSource PublishEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<MqttClientPublishResult> PublishResponse { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IgnorePublishCancellation { get; init; }
    public int PublishCalls { get; private set; }
    public MqttApplicationMessage? PublishedMessage { get; private set; }
    public Task<MqttClientPublishResult> PublishAsync(MqttApplicationMessage message, CancellationToken cancellationToken)
    {
        PublishedMessage = message;
        PublishCalls++;
        PublishEntered.TrySetResult();
        return PublishResponse.Task.WaitAsync(IgnorePublishCancellation ? CancellationToken.None : cancellationToken);
    }
    public Task<MqttClientSubscribeResult> SubscribeAsync(MqttClientSubscribeOptions options, CancellationToken cancellationToken)
    {
        SubscribeOptions = options;
        SubscribeEntered.TrySetResult();
        return SubscribeResponse.Task.WaitAsync(cancellationToken);
    }
    public Task<MqttClientUnsubscribeResult> UnsubscribeAsync(MqttClientUnsubscribeOptions options, CancellationToken cancellationToken)
    {
        UnsubscribeOptions = options;
        return UnsubscribeResponse.Task.WaitAsync(cancellationToken);
    }
    public Task SendEnhancedAuthenticationExchangeDataAsync(MqttEnhancedAuthenticationExchangeData data, CancellationToken cancellationToken) => throw new NotSupportedException();
}
