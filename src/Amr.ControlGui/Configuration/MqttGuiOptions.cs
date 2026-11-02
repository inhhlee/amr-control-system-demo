using System.Globalization;
using Amr.Communication.Mqtt;
using Amr.Core;
using Amr.Infrastructure;
using Amr.Simulator.Configuration;

namespace Amr.ControlGui;

internal sealed record MqttGuiOptions(MqttConnectionSettings Connection, string AmrId, ReconnectOptions Reconnect,
    string? SimulatorConfig = null, int CommandTimeoutSeconds = 30, string? FleetConfig = null)
{
    internal static MqttGuiOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] != "--mqtt") throw new ArgumentException("--mqtt를 지정하세요.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < args.Length; i++)
        {
            string key = args[i];
            if (key is not ("--host" or "--port" or "--amr-id" or "--connect-timeout-seconds" or
                "--reconnect-delay-seconds" or "--reconnect-attempts" or "--simulator-config" or "--command-timeout-seconds" or "--fleet-config") || ++i >= args.Length ||
                string.IsNullOrWhiteSpace(args[i]) || !values.TryAdd(key, args[i]))
                throw new ArgumentException($"잘못된 실행 옵션: {key}");
        }
        int Number(string key, int fallback, int min, int max) => !values.TryGetValue(key, out string? text) ? fallback :
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= min && value <= max
                ? value : throw new ArgumentException($"숫자 범위를 확인하세요: {key}");
        string id = values.GetValueOrDefault("--amr-id", "AMR_001");
        if (values.ContainsKey("--fleet-config") && (values.ContainsKey("--amr-id") || values.ContainsKey("--simulator-config")))
            throw new ArgumentException("--fleet-config는 --amr-id 또는 --simulator-config와 함께 사용할 수 없습니다.");
        if (id.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '/' or '+' or '#'))
            throw new ArgumentException("AMR ID에는 공백이나 MQTT 구분자를 사용할 수 없습니다.");
        return new(new(values.GetValueOrDefault("--host", "127.0.0.1"), Number("--port", 1884, 1, 65535),
            Number("--connect-timeout-seconds", 3, 1, 3600)), id,
            new() { Delay = TimeSpan.FromSeconds(Number("--reconnect-delay-seconds", 5, 1, 3600)),
                MaxAttempts = Number("--reconnect-attempts", 5, 0, 100) }, values.GetValueOrDefault("--simulator-config"),
                Number("--command-timeout-seconds", 30, 1, 3600), values.GetValueOrDefault("--fleet-config"));
    }

    // 설정은 장비 등록/목적지에만 사용한다. 초기 좌표를 수신 위치로 만들어 넣지 않는다.
    internal (IAmrDataProvider Provider, Action Start) CreateProvider()
    {
        if (FleetConfig is not null)
        {
            var fleet = FleetSettingsLoader.LoadAsync(FleetConfig).GetAwaiter().GetResult();
            var devices = fleet.Amrs.Select(x => new MqttFleetDevice(x.Settings.AmrId,
                x.Settings.Waypoints.Count == 0 ? null : new MapStation("설정 목적지", x.Settings.Waypoints[^1].X, x.Settings.Waypoints[^1].Y)));
            var provider = new FleetMqttAmrDataProvider(Connection, devices, Reconnect, TimeSpan.FromSeconds(CommandTimeoutSeconds));
            return (provider, provider.Start);
        }
        MapStation? destination = null;
        if (SimulatorConfig is not null)
        {
            var settings = SimulatorSettingsLoader.LoadAsync(SimulatorConfig).GetAwaiter().GetResult();
            if (settings.AmrId != AmrId) throw new ArgumentException("설정 파일의 AMR ID와 관리 대상이 다릅니다.");
            if (settings.Waypoints.Count != 0)
            { var goal = settings.Waypoints[^1]; destination = new("설정 목적지", goal.X, goal.Y); }
        }
        var single = new MqttAmrDataProvider(Connection, AmrId, Reconnect,
            commandTimeout: TimeSpan.FromSeconds(CommandTimeoutSeconds), destination: destination);
        return (single, single.Start);
    }
}
