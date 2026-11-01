using Amr.Communication.Models;

namespace Amr.Simulator.Map;

/// <summary>검사된 관제 모델만 받는다. 설정 좌표나 Simulator stdout으로 위치를 채우지 않는다.</summary>
public sealed class MqttMapProjection(string amrId)
{
    private readonly object _gate = new();
    private TelemetryMessage? _telemetry;
    private HeartbeatMessage? _heartbeat;
    private int _telemetryCount, _heartbeatCount, _ignoredCount;

    public bool Apply(object model)
    {
        lock (_gate)
        {
            switch (model)
            {
                // Payload ts는 UTC 발생 시각이다. 시계 보정으로 작아질 수 있으므로 수신 큐 순서를 따른다.
                case TelemetryMessage telemetry when telemetry.Id == amrId:
                    _telemetry = telemetry;
                    _telemetryCount++;
                    return true;
                case HeartbeatMessage heartbeat when heartbeat.Id == amrId:
                    _heartbeat = heartbeat;
                    _heartbeatCount++;
                    return true;
                default:
                    _ignoredCount++;
                    return false;
            }
        }
    }

    public MqttMapData Read()
    {
        lock (_gate) return new(_telemetry, _heartbeat, _telemetryCount, _heartbeatCount, _ignoredCount);
    }

    public bool MatchesFinal(long timestamp, double x, double y)
    {
        lock (_gate) return _telemetry is { } t && t.Timestamp == timestamp &&
            t.Position.X == x && t.Position.Y == y && t.Speed == 0 && t.State == "IDLE";
    }
}

public sealed record MqttMapData(TelemetryMessage? Telemetry, HeartbeatMessage? Heartbeat,
    int TelemetryCount, int HeartbeatCount, int IgnoredCount);
