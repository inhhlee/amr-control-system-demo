using System.Text;
using System.Text.Json;
using Amr.Simulator.Simulation;

namespace Amr.Simulator.Configuration;

public static class SimulatorSettingsLoader
{
    public static async Task<SimulatorSettings> LoadAsync(string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ConfigurationException("$file", "설정 파일 경로가 필요합니다.");

        string json;
        try
        {
            // BOM 자동 감지가 엄격한 디코더를 바꾸지 않도록 UTF-8 BOM만 직접 건너뛴다.
            // 나머지 바이트는 BOM 유무와 관계없이 대체 문자 보정 없이 읽는다.
            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            int offset = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
            json = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            throw new ConfigurationException("$file", "설정 파일의 문자 인코딩을 읽을 수 없습니다.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            throw new ConfigurationException("$file", $"설정 파일을 읽을 수 없습니다 ({exception.GetType().Name}).");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Parse(json);
    }

    public static SimulatorSettings Parse(string json)
    {
        using var document = ReadDocument(json);
        var root = document.RootElement;
        CheckMembers(root, "$", ["amrId", "initialPosition", "speedMps", "updateIntervalMs", "waypoints"]);

        var idElement = Required(root, "amrId", "$");
        if (idElement.ValueKind != JsonValueKind.String)
            throw new ConfigurationException("$.amrId", "AMR ID는 문자열이어야 합니다.");
        string id;
        try
        {
            id = idElement.GetString()!;
        }
        catch (InvalidOperationException)
        {
            throw new ConfigurationException("$.amrId", "AMR ID는 유효한 Unicode 문자열이어야 합니다.");
        }
        if (string.IsNullOrEmpty(id) || id.Any(character => char.IsWhiteSpace(character)
            || char.IsControl(character) || character is '/' or '+' or '#'))
            throw new ConfigurationException("$.amrId", "AMR ID는 비어 있을 수 없으며 공백·제어문자·/·+·#를 포함할 수 없습니다.");

        var initial = ReadPosition(Required(root, "initialPosition", "$"), "$.initialPosition");
        double speed = ReadFiniteNumber(Required(root, "speedMps", "$"), "$.speedMps");
        if (speed <= 0)
            throw new ConfigurationException("$.speedMps", "이동 속도는 0보다 커야 합니다 (m/s).");

        var intervalElement = Required(root, "updateIntervalMs", "$");
        if (intervalElement.ValueKind != JsonValueKind.Number || !intervalElement.TryGetInt64(out long interval)
            || interval <= 0)
            throw new ConfigurationException("$.updateIntervalMs", "위치 갱신 주기는 0보다 큰 Int64 JSON 정수여야 합니다 (ms).");

        var path = Required(root, "waypoints", "$");
        if (path.ValueKind != JsonValueKind.Array || path.GetArrayLength() == 0)
            throw new ConfigurationException("$.waypoints", "경유점이 1개 이상인 배열이 필요합니다.");
        var waypoints = new Position2D[path.GetArrayLength()];
        int index = 0;
        foreach (var waypoint in path.EnumerateArray())
        {
            waypoints[index] = ReadPosition(waypoint, $"$.waypoints[{index}]");
            index++;
        }

        return new SimulatorSettings(id, initial, speed, interval, waypoints);
    }

    private static JsonDocument ReadDocument(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new ConfigurationException("$", $"JSON 문법 오류입니다 (line={exception.LineNumber}, byte={exception.BytePositionInLine}).");
        }
    }

    private static JsonElement Required(JsonElement parent, string name, string parentPath)
    {
        if (!parent.TryGetProperty(name, out var value))
            throw new ConfigurationException($"{parentPath}.{name}", "필수 항목이 누락됐습니다.");
        return value;
    }

    private static Position2D ReadPosition(JsonElement element, string path)
    {
        CheckMembers(element, path, ["x", "y"]);
        return new Position2D(ReadFiniteNumber(Required(element, "x", path), $"{path}.x"),
            ReadFiniteNumber(Required(element, "y", path), $"{path}.y"));
    }

    private static double ReadFiniteNumber(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out double value)
            || !double.IsFinite(value))
            throw new ConfigurationException(path, "유한한 JSON 숫자가 필요합니다.");
        return value;
    }

    private static void CheckMembers(JsonElement element, string path, string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ConfigurationException(path, "JSON 객체가 필요합니다.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            string name;
            try
            {
                name = property.Name;
            }
            catch (InvalidOperationException)
            {
                throw new ConfigurationException(path, "설정 항목 이름은 유효한 Unicode 문자열이어야 합니다.");
            }
            // 오타나 중복 필드를 조용히 무시하지 않는다.
            // 알 수 없는 이름은 로그에 그대로 넣지 않아 제어문자 출력도 피한다.
            if (!allowed.Contains(name, StringComparer.Ordinal))
                throw new ConfigurationException(path, "지원하지 않는 설정 항목이 있습니다. 필드명과 대소문자를 확인하세요.");
            if (!names.Add(name))
                throw new ConfigurationException($"{path}.{name}", "설정 항목이 중복됐습니다.");
        }
    }
}
