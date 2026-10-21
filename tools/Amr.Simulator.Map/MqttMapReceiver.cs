using System.Text.Json;
using Amr.Communication.Models;
using Amr.Communication.Mqtt;
using MQTTnet;

namespace Amr.Simulator.Map;

/// <summary>관제와 같은 구독·검사·큐를 소유한다. Simulator 송신 Client와 별도 연결이다.</summary>
public sealed class MqttMapReceiver : IAsyncDisposable
{
    private readonly IMqttClient _client = new MqttClientFactory().CreateMqttClient();
    private readonly MqttConnection _connection;
    private readonly MqttConnectionSettings _settings;
    private readonly AmrReceiveQueue _queue;
    private readonly MessageSubscription _subscription;
    private readonly Action<string> _status, _failed;
    private readonly TextWriter _log;
    private bool _approved, _closing;

    public MqttMapReceiver(string host, int port, string amrId, Action<ReceivedMessage> received,
        Action rejected, Action<string> status, Action<string> failed, TextWriter log)
    {
        _settings = new MqttConnectionSettings(host, port);
        _connection = new MqttConnection(_client);
        _status = status;
        _failed = failed;
        _log = TextWriter.Synchronized(log);
        _queue = new AmrReceiveQueue((message, token) =>
        {
            token.ThrowIfCancellationRequested();
            Write("Received", message);
            received(message);
            return Task.CompletedTask;
        }, report =>
        {
            if (report.ExceptionType is not null) _failed("ReceiverHandlerFailed");
        });
        _subscription = new MessageSubscription(_client, message =>
        {
            if (message.AmrId != amrId) { Write("IgnoredAmr", new { message.AmrId, message.Kind }); return; }
            var result = _queue.TryEnqueue(message);
            if (result != QueueAdmission.Accepted) _failed("ReceiveQueue" + result);
        }, error => { Write("Rejected", error); rejected(); },
            error => { Write("CallbackFailed", error); _failed("ReceiverCallbackFailed"); });
        _client.DisconnectedAsync += OnDisconnected;
    }

    private void Write(string name, object value) => _log.WriteLine(JsonSerializer.Serialize(
        new { @event = name, value }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private Task OnDisconnected(MqttClientDisconnectedEventArgs args)
    {
        if (Volatile.Read(ref _approved) && !Volatile.Read(ref _closing))
        {
            _status("Disconnected");
            _failed("BrokerDisconnected");
        }
        return Task.CompletedTask;
    }

    public async Task StartAsync(CancellationToken token)
    {
        _status("Connecting");
        await _connection.ConnectAsync(_settings, token);
        Volatile.Write(ref _approved, true);
        if (!_connection.IsConnected) throw new IOException("Receiver disconnected before subscription.");
        await _subscription.SubscribeAsync(token);
        Write("Subscribed", new { _settings.ClientId, _settings.Host, _settings.Port });
        _status("Subscribed");
    }

    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref _closing, true);
        try
        {
            await _queue.StopAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            try { await _subscription.StopAsync(); }
            finally
            {
                _client.DisconnectedAsync -= OnDisconnected;
                try { await _connection.DisconnectAsync(); }
                finally { _connection.Dispose(); _status("Closed"); }
            }
        }
    }
}
