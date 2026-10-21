using Amr.Communication.Mqtt;
using MQTTnet;
using MQTTnet.Protocol;

namespace Amr.Simulator.Telemetry;

public sealed class MqttSimulationTransport : ISimulationTransport
{
    private readonly IMqttClient _client = new MqttClientFactory().CreateMqttClient();
    private readonly MqttConnection _connection;
    private readonly MqttConnectionSettings _settings;
    private bool _disposed;

    public MqttSimulationTransport(MqttConnectionSettings settings)
    {
        _settings = settings;
        _connection = new MqttConnection(_client);
        _client.DisconnectedAsync += OnDisconnected;
    }

    public event Action? ConnectionLost;
    private Task OnDisconnected(MqttClientDisconnectedEventArgs args)
    {
        ConnectionLost?.Invoke();
        return Task.CompletedTask;
    }

    public Task ConnectAsync(CancellationToken token) => _connection.ConnectAsync(_settings, token);

    public async Task PublishAsync(SimulationPublication message, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var packet = new MqttApplicationMessageBuilder().WithTopic(message.Topic).WithPayload(message.Payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce).WithRetainFlag(false).Build();
        try
        {
            var result = await _client.PublishAsync(packet, timeout.Token);
            if (!result.IsSuccess) throw new IOException("MQTT publication rejected.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("MQTT publication timed out; delivery is unknown.");
        }
    }

    public Task DisconnectAsync() => _connection.DisconnectAsync();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.DisconnectedAsync -= OnDisconnected;
        _connection.Dispose();
    }
}
