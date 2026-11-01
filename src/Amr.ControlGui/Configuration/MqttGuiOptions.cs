using System.Globalization;
using Amr.Communication.Mqtt;

namespace Amr.ControlGui;

internal sealed record MqttGuiOptions(MqttConnectionSettings Connection, string AmrId, ReconnectOptions Reconnect,
    string? SimulatorConfig = null, int CommandTimeoutSeconds = 30)
{
    internal static MqttGuiOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] != "--mqtt") throw new ArgumentException("--mqtt를 지정하세요.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < args.Length; i++)
        {
            string key = args[i];
            if (key is not ("--host" or "--port" or "--amr-id" or "--connect-timeout-seconds" or
                "--reconnect-delay-seconds" or "--reconnect-attempts" or "--simulator-config" or "--command-timeout-seconds") || ++i >= args.Length ||
                string.IsNullOrWhiteSpace(args[i]) || !values.TryAdd(key, args[i]))
                throw new ArgumentException($"잘못된 실행 옵션: {key}");
        }
        int Number(string key, int fallback, int min, int max) => !values.TryGetValue(key, out string? text) ? fallback :
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= min && value <= max
                ? value : throw new ArgumentException($"숫자 범위를 확인하세요: {key}");
        string id = values.GetValueOrDefault("--amr-id", "AMR_001");
        if (id.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '/' or '+' or '#'))
            throw new ArgumentException("AMR ID에는 공백이나 MQTT 구분자를 사용할 수 없습니다.");
        return new(new(values.GetValueOrDefault("--host", "127.0.0.1"), Number("--port", 1884, 1, 65535),
            Number("--connect-timeout-seconds", 3, 1, 3600)), id,
            new() { Delay = TimeSpan.FromSeconds(Number("--reconnect-delay-seconds", 5, 1, 3600)),
                MaxAttempts = Number("--reconnect-attempts", 5, 0, 100) }, values.GetValueOrDefault("--simulator-config"),
                Number("--command-timeout-seconds", 30, 1, 3600));
    }
}
