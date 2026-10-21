using System.Globalization;
using Amr.Communication.Mqtt;

namespace Amr.Simulator.Telemetry;

public sealed record SimulatorMqttOptions(MqttConnectionSettings Connection, int TelemetryMs, int HeartbeatMs)
{
    public static (string Config, SimulatorMqttOptions? Mqtt) Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool mqtt = false;
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (key == "--mqtt")
            {
                if (mqtt) throw new ArgumentException("Duplicate --mqtt.");
                mqtt = true;
                continue;
            }
            if (key is not ("--config" or "--host" or "--port" or "--telemetry-ms" or "--heartbeat-ms") ||
                ++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || !values.TryAdd(key, args[i]))
                throw new ArgumentException("Invalid or duplicate option.");
        }
        if (!values.TryGetValue("--config", out string? config)) throw new ArgumentException("--config is required.");
        if (!mqtt)
        {
            if (values.Count != 1) throw new ArgumentException("MQTT options require --mqtt.");
            return (config, null);
        }
        if (values.Count != 5) throw new ArgumentException("Specify --host, --port, --telemetry-ms and --heartbeat-ms with --mqtt.");
        int Positive(string key) => int.TryParse(values[key], NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value > 0
            ? value : throw new ArgumentException("MQTT intervals and port must be positive integers.");
        return (config, new(new MqttConnectionSettings(values["--host"], Positive("--port")),
            Positive("--telemetry-ms"), Positive("--heartbeat-ms")));
    }
}
