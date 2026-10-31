using System.Buffers;
using System.Text.Json;
using Amr.Communication.Validation;

namespace Amr.Simulator.Configuration;

public static class FleetSettingsLoader
{
    private static readonly string[] AmrFields =
        ["amrId", "initialPosition", "speedMps", "updateIntervalMs", "waypoints", "faults"];

    public static async Task<FleetSettings> LoadAsync(string path, CancellationToken token = default) =>
        Parse(await SimulatorSettingsLoader.ReadUtf8Async(path, token));

    public static FleetSettings Parse(string json)
    {
        using var document = SimulatorSettingsLoader.ReadDocument(json);
        var root = document.RootElement;
        Members(root, "$", ["amrs"]);
        var items = Required(root, "amrs", "$");
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            throw new ConfigurationException("$.amrs", "AMR 설정이 1개 이상인 배열이 필요합니다.");

        var amrs = new List<ConfiguredAmr>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.EnumerateArray())
        {
            string path = $"$.amrs[{amrs.Count}]";
            Members(item, path, AmrFields);
            // 기존 단일 설정 검사와 허용값을 그대로 재사용한다.
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                writer.WriteStartObject();
                foreach (var property in item.EnumerateObject())
                    if (property.Name != "faults") property.WriteTo(writer);
                writer.WriteEndObject();
            }
            SimulatorSettings settings;
            try { settings = SimulatorSettingsLoader.Parse(System.Text.Encoding.UTF8.GetString(bytes.ToArray())); }
            catch (ConfigurationException e)
            { throw new ConfigurationException(path + e.Field[1..], e.Reason); }
            if (!ids.Add(settings.AmrId))
                throw new ConfigurationException(path + ".amrId", "AMR ID가 중복됐습니다.");

            var faults = Required(item, "faults", path);
            if (faults.ValueKind != JsonValueKind.Array)
                throw new ConfigurationException(path + ".faults", "장애 시나리오 배열이 필요합니다. 장애 없음은 빈 배열입니다.");
            var scenarios = new List<FaultScenario>();
            foreach (var fault in faults.EnumerateArray())
            {
                string faultPath = $"{path}.faults[{scenarios.Count}]";
                Members(fault, faultPath, ["code", "level", "activateAfterMs", "resolveAfterMs", "message"]);
                var code = Required(fault, "code", faultPath);
                var level = Required(fault, "level", faultPath);
                long activate = Milliseconds(fault, "activateAfterMs", faultPath);
                long resolve = Milliseconds(fault, "resolveAfterMs", faultPath);
                if (resolve <= activate)
                    throw new ConfigurationException(faultPath + ".resolveAfterMs", "원인 해소 시점은 발생 시점보다 뒤여야 합니다.");
                // Alarm의 문자열·등급 규칙을 설정 검사에서 중복 구현하지 않는다.
                var payload = new Dictionary<string, object?>
                {
                    ["id"] = settings.AmrId, ["ts"] = 0, ["code"] = code,
                    ["level"] = level, ["alarmState"] = "ACTIVE"
                };
                if (fault.TryGetProperty("message", out var message)) payload["message"] = message;
                var validated = new AlarmValidator().Validate($"amr/{settings.AmrId}/alarm",
                    new ReadOnlySequence<byte>(JsonSerializer.SerializeToUtf8Bytes(payload)), DateTimeOffset.UnixEpoch);
                if (!validated.IsValid)
                    throw new ConfigurationException(faultPath + validated.Error!.Field[1..], validated.Error.Reason);
                var model = validated.Message!;
                scenarios.Add(new(model.Code, model.Level, activate, resolve, model.Message));
            }
            amrs.Add(new(settings, scenarios.AsReadOnly()));
        }
        return new(amrs);
    }

    private static long Milliseconds(JsonElement element, string field, string path)
    {
        var value = Required(element, field, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long number) || number < 0)
            throw new ConfigurationException(path + "." + field, "0 이상의 Int64 JSON 정수(ms)가 필요합니다.");
        return number;
    }

    private static JsonElement Required(JsonElement element, string field, string path) =>
        element.TryGetProperty(field, out var value) ? value :
            throw new ConfigurationException(path + "." + field, "필수 항목이 누락됐습니다.");

    private static void Members(JsonElement element, string path, string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ConfigurationException(path, "JSON 객체가 필요합니다.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            string name;
            try { name = property.Name; }
            catch (InvalidOperationException) { throw new ConfigurationException(path, "유효한 Unicode 필드명이 필요합니다."); }
            if (!allowed.Contains(name, StringComparer.Ordinal))
                throw new ConfigurationException(path, "지원하지 않는 설정 항목입니다.");
            if (!seen.Add(name)) throw new ConfigurationException(path + "." + name, "설정 항목이 중복됐습니다.");
        }
    }
}
