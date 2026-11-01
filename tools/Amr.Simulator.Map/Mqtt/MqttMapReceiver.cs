using System.Text.Json;
using Amr.Communication.Models;
using Amr.Communication.Monitoring;
using Amr.Communication.Mqtt;

namespace Amr.Simulator.Map;

/// <summary>정식 관제 통신 세션의 수신 결과를 지도에 전달하고 시연 로그를 기록한다.</summary>
public sealed class MqttMapReceiver : IAsyncDisposable
{
    private readonly ControlCommunicationSession _session;

    public MqttMapReceiver(string host, int port, string amrId, Action<ReceivedMessage> received,
        Action rejected, Action<string> status, Action<string> failed, TextWriter log,
        HeartbeatMonitorOptions? heartbeatOptions = null)
    {
        var writer = TextWriter.Synchronized(log);
        _session = new(new(host, port), amrId, (message, token) => {
            token.ThrowIfCancellationRequested();
            received(message);
            return Task.CompletedTask;
        }, entry => {
            writer.WriteLine(JsonSerializer.Serialize(new { @event = entry.Name, value = entry.Value },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            if (entry.Name == "Rejected") rejected();
            else if (entry.Name == "Failed") failed((string)entry.Value!);
            else if (entry.Name is "Connecting" or "Subscribed" or "Closed") status(entry.Name);
        }, heartbeatOptions);
    }

    public HeartbeatObservation Heartbeat => _session.Heartbeat;
    public Task StartAsync(CancellationToken token) => _session.StartAsync(token);
    public ValueTask DisposeAsync() => _session.DisposeAsync();
}
