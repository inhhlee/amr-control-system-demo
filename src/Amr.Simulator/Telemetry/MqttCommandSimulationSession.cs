using Amr.Communication.Mqtt;
using Amr.Simulator.Configuration;
using MQTTnet;
using MQTTnet.Protocol;

namespace Amr.Simulator.Telemetry;

/// <summary>장비 한 대의 MQTT 연결과 구독만 소유한다. 도메인 상태는 실행 컨텍스트가 변경한다.</summary>
public sealed class MqttCommandSimulationSession : IAmrDeviceTransport
{
    private readonly IMqttClient _client = new MqttClientFactory().CreateMqttClient();
    private readonly MqttConnection _connection;
    private readonly SimulatorMqttOptions _options;
    private readonly string _commandTopic;
    private bool _disposed;

    public MqttCommandSimulationSession(string amrId, SimulatorMqttOptions options)
    {
        _commandTopic = $"amr/{amrId}/command";
        _options = options;
        _connection = new(_client);
        _client.ApplicationMessageReceivedAsync += Receive;
        _client.DisconnectedAsync += Disconnected;
    }

    public event DeviceCommandReceived? CommandReceived;
    public event Action? ConnectionLost;

    private Task Receive(MqttApplicationMessageReceivedEventArgs args)
    {
        CommandReceived?.Invoke(args.ApplicationMessage.Topic, args.ApplicationMessage.Payload, args.ApplicationMessage.Retain);
        return Task.CompletedTask;
    }

    private Task Disconnected(MqttClientDisconnectedEventArgs args)
    {
        if (args.ClientWasConnected) ConnectionLost?.Invoke();
        return Task.CompletedTask;
    }

    public Task ConnectAsync(CancellationToken token) => _connection.ConnectAsync(_options.Connection, token);

    public async Task SubscribeAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(_options.Connection.ConnectTimeout);
        var result = await _client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(_commandTopic, MqttQualityOfServiceLevel.AtMostOnce).Build(), timeout.Token);
        if (result.Items.Count != 1 || result.Items.Single().ResultCode != MqttClientSubscribeResultCode.GrantedQoS0)
            throw new IOException("CommandSubscriptionRejected");
    }

    public async Task PublishAsync(SimulationPublication message, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var result = await _client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(message.Topic)
            .WithPayload(message.Payload).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
            .WithRetainFlag(false).Build(), timeout.Token);
        if (!result.IsSuccess) throw new IOException("PublicationRejected");
    }

    public async Task UnsubscribeAsync(CancellationToken token)
    {
        if (_connection.IsConnected)
            await _client.UnsubscribeAsync(new MqttClientUnsubscribeOptionsBuilder().WithTopicFilter(_commandTopic).Build(), token);
    }

    public Task DisconnectAsync() => _connection.DisconnectAsync();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.ApplicationMessageReceivedAsync -= Receive;
        _client.DisconnectedAsync -= Disconnected;
        _connection.Dispose();
    }

    // 기존 단일 명령 모드도 Fleet과 같은 실행 엔진을 사용한다.
    public static async Task<int> RunAsync(SimulatorSettings settings, SimulatorMqttOptions options,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        await using var context = new AmrDeviceContext(new(settings, settings.Faults, AmrOperationMode.Command),
            options, output, error);
        return await context.RunAsync(cancellationToken);
    }
}
