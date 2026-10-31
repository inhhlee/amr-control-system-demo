using Amr.Communication.Mqtt;
using System.Globalization;

namespace Amr.Communication.ConsoleApp;

internal sealed record TrialConsoleOptions(MqttConnectionSettings Connection, string AmrId,
    ReconnectOptions Reconnect, bool ShowPayload, bool NoColor)
{
    public static TrialConsoleOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (key is "--trial" or "--show-payload" or "--no-color")
            { if (!flags.Add(key)) throw new ArgumentException("Duplicate option."); continue; }
            if (key is not ("--host" or "--port" or "--amr-id" or "--connect-timeout-seconds" or
                "--reconnect-delay-seconds" or "--reconnect-attempts") || ++i >= args.Length ||
                string.IsNullOrWhiteSpace(args[i]) || !values.TryAdd(key, args[i]))
                throw new ArgumentException("Invalid option.");
        }
        if (!flags.Contains("--trial")) throw new ArgumentException("Specify --trial.");
        int Number(string key, int fallback, int min, int max) => !values.TryGetValue(key, out string? text) ? fallback :
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= min && value <= max
                ? value : throw new ArgumentException("Invalid numeric option.");
        string id = values.GetValueOrDefault("--amr-id", "AMR_001");
        if (id.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '/' or '+' or '#')) throw new ArgumentException("Invalid AMR ID.");
        return new(new(values.GetValueOrDefault("--host", "127.0.0.1"), Number("--port", 1884, 1, 65535),
            Number("--connect-timeout-seconds", 3, 1, 3600)), id,
            new() { Delay = TimeSpan.FromSeconds(Number("--reconnect-delay-seconds", 5, 1, 3600)),
                MaxAttempts = Number("--reconnect-attempts", 5, 0, 100) },
            flags.Contains("--show-payload"), flags.Contains("--no-color"));
    }
}
