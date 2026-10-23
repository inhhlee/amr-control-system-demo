using System.Text;
using System.Text.Json;
using Amr.Communication.Mqtt;
using Amr.Simulator.Telemetry;

namespace Amr.Simulator.Map.Navigation;

public sealed record GridMissionSettings(string AmrId, GridCell Start, IReadOnlyList<GridCell> Waypoints)
{
    private int[,] _map = GridNavigator.CreateDefaultMap();
    public int[,] Map => (int[,])_map.Clone();
    public int StepIntervalMs { get; private init; } = GridNavigationSession.DefaultStepIntervalMs;
    public int WebPort { get; private init; } = 5088;
    public SimulatorMqttOptions? Mqtt { get; private init; }
    internal ReadOnlyMemory<byte> SourceBytes { get; private init; }

    public static async Task<GridMissionSettings> LoadAsync(string path)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path);
        string json = new UTF8Encoding(false, true).GetString(bytes);
        if (json.StartsWith('\uFEFF')) json = json[1..];
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Properties(root, ["amrId", "start", "waypoints"], ["map", "stepIntervalMs", "webPort", "mqtt"]);
        var idValue = root.GetProperty("amrId");
        if (idValue.ValueKind != JsonValueKind.String) throw new ArgumentException("amrId는 문자열이어야 합니다.");
        string id = idValue.GetString()!;
        if (string.IsNullOrWhiteSpace(id) || id.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '/' or '+' or '#') ||
            Encoding.UTF8.GetByteCount($"amr/{id}/telemetry") > ushort.MaxValue)
            throw new ArgumentException("amrId가 MQTT 식별 기준에 맞지 않습니다.");
        var start = Cell(root.GetProperty("start"));
        var list = root.GetProperty("waypoints");
        if (list.ValueKind != JsonValueKind.Array) throw new ArgumentException("waypoints는 배열이어야 합니다.");
        var waypoints = list.EnumerateArray().Select(Cell).ToArray();
        int[,] map = root.TryGetProperty("map", out var mapValue) ? ReadMap(mapValue) : GridNavigator.CreateDefaultMap();
        _ = new GridNavigator(start, waypoints, map);
        int step = root.TryGetProperty("stepIntervalMs", out var stepValue) ? Integer(stepValue, "stepIntervalMs", 1, int.MaxValue) : GridNavigationSession.DefaultStepIntervalMs;
        int port = root.TryGetProperty("webPort", out var portValue) ? Integer(portValue, "webPort", 0, 65535) : 5088;
        SimulatorMqttOptions? mqtt = null;
        if (root.TryGetProperty("mqtt", out var mqttValue))
        {
            Properties(mqttValue, "enabled", "host", "port", "telemetryMs", "heartbeatMs");
            var enabled = mqttValue.GetProperty("enabled");
            if (enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException("mqtt.enabled는 true 또는 false여야 합니다.");
            var hostValue = mqttValue.GetProperty("host");
            if (hostValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(hostValue.GetString())) throw new ArgumentException("mqtt.host는 비어 있지 않은 문자열이어야 합니다.");
            var connection = new MqttConnectionSettings(hostValue.GetString()!, Integer(mqttValue.GetProperty("port"), "mqtt.port", 1, 65535));
            int telemetry = Integer(mqttValue.GetProperty("telemetryMs"), "mqtt.telemetryMs", 1, int.MaxValue);
            int heartbeat = Integer(mqttValue.GetProperty("heartbeatMs"), "mqtt.heartbeatMs", 1, int.MaxValue);
            if (enabled.GetBoolean()) mqtt = new(connection, telemetry, heartbeat);
        }
        return new(id, start, Array.AsReadOnly(waypoints)) { _map = map, StepIntervalMs = step, WebPort = port, Mqtt = mqtt, SourceBytes = bytes };
    }

    private static int[,] ReadMap(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0) throw new ArgumentException("map은 행이 하나 이상인 2차원 배열이어야 합니다.");
        var rows = value.EnumerateArray().ToArray();
        if (rows[0].ValueKind != JsonValueKind.Array || rows[0].GetArrayLength() == 0) throw new ArgumentException("map의 행은 비어 있을 수 없습니다.");
        int cols = rows[0].GetArrayLength();
        var map = new int[rows.Length, cols];
        for (int row = 0; row < rows.Length; row++)
        {
            if (rows[row].ValueKind != JsonValueKind.Array || rows[row].GetArrayLength() != cols) throw new ArgumentException("map의 모든 행은 열 수가 같아야 합니다.");
            for (int col = 0; col < cols; col++) map[row, col] = Integer(rows[row][col], $"map[{row}][{col}]", 0, 1);
        }
        return map;
    }

    private static int Integer(JsonElement value, string name, int min, int max) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) && number >= min && number <= max
            ? number : throw new ArgumentException($"{name}은 {min}~{max} 범위의 정수여야 합니다.");

    private static GridCell Cell(JsonElement value)
    {
        Properties(value, "row", "col");
        if (value.GetProperty("row").ValueKind != JsonValueKind.Number || value.GetProperty("col").ValueKind != JsonValueKind.Number ||
            !value.GetProperty("row").TryGetInt32(out int row) || !value.GetProperty("col").TryGetInt32(out int col))
            throw new ArgumentException("row·col은 정수여야 합니다.");
        return new(row, col);
    }

    private static void Properties(JsonElement value, params string[] names) => Properties(value, names, []);

    private static void Properties(JsonElement value, string[] required, string[] optional)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException("설정 항목은 JSON 객체여야 합니다.");
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if ((!required.Contains(property.Name, StringComparer.Ordinal) && !optional.Contains(property.Name, StringComparer.Ordinal)) || !found.Add(property.Name))
                throw new ArgumentException("알 수 없거나 중복된 필드: " + property.Name);
        if (required.Any(name => !found.Contains(name))) throw new ArgumentException("설정 필수 필드가 누락됐습니다.");
    }
}
