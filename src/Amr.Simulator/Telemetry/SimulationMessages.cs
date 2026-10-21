using System.Buffers;
using System.Text;
using System.Text.Json;
using Amr.Communication.Validation;
using Amr.Simulator.Simulation;

namespace Amr.Simulator.Telemetry;

public sealed record SimulationPublication(string Kind, string Topic, byte[] Payload);

public static class SimulationMessages
{
    public static SimulationPublication Create(SimulationSnapshot snapshot, bool heartbeat, long timestamp)
    {
        string kind = heartbeat ? "Heartbeat" : "Telemetry";
        string topic = $"amr/{snapshot.Id}/{kind.ToLowerInvariant()}";
        if (new UTF8Encoding(false, true).GetByteCount(topic) > ushort.MaxValue)
            throw new ArgumentException("MQTT topic exceeds UTF-8 length limit.");
        byte[] payload = heartbeat
            ? JsonSerializer.SerializeToUtf8Bytes(new { id = snapshot.Id, ts = timestamp, status = "OK" })
            : JsonSerializer.SerializeToUtf8Bytes(new { id = snapshot.Id, ts = timestamp,
                pos = new { x = snapshot.X, y = snapshot.Y }, sp = snapshot.Speed, state = snapshot.State });
        var bytes = new ReadOnlySequence<byte>(payload);
        bool valid = heartbeat
            ? new HeartbeatValidator().Validate(topic, bytes, DateTimeOffset.UtcNow).IsValid
            : new TelemetryValidator().Validate(topic, bytes, DateTimeOffset.UtcNow).IsValid;
        if (!valid) throw new ArgumentException("Generated message does not satisfy the communication contract.");
        return new(kind, topic, payload);
    }
}
