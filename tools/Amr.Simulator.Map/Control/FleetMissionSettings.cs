using System.Text;
using System.Text.Json;

namespace Amr.Simulator.Map.Control;

public sealed record FleetMissionSettings(int WebPort, IReadOnlyList<GridMissionSettings> Missions,
    IReadOnlyList<string> MissionPaths, byte[] SourceBytes)
{
    public static async Task<FleetMissionSettings> LoadAsync(string path)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path);
        string json = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
            !root.TryGetProperty("webPort", out var port) || port.ValueKind != JsonValueKind.Number || !port.TryGetInt32(out int number) || number is < 0 or > 65535 ||
            !root.TryGetProperty("missions", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() != 2)
            throw new ArgumentException("Fleet requires webPort and exactly two missions.");
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string[] paths = list.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())
            ? Path.GetFullPath(item.GetString()!, directory) : throw new ArgumentException("Mission path is required.")).ToArray();
        var missions = new List<GridMissionSettings>();
        foreach (string missionPath in paths) missions.Add(await GridMissionSettings.LoadAsync(missionPath));
        if (missions.Select(x => x.AmrId).Distinct(StringComparer.Ordinal).Count() != 2 || missions.Any(x => x.Mqtt is null))
            throw new ArgumentException("Fleet requires two distinct IDs and enabled MQTT.");
        var first = missions[0];
        var second = missions[1];
        if (first.Mqtt!.Connection.Host != second.Mqtt!.Connection.Host || first.Mqtt.Connection.Port != second.Mqtt.Connection.Port ||
            first.Map.GetLength(0) != second.Map.GetLength(0) || first.Map.GetLength(1) != second.Map.GetLength(1) ||
            !first.Map.Cast<int>().SequenceEqual(second.Map.Cast<int>()))
            throw new ArgumentException("Fleet missions must share one Broker and the same map.");
        return new(number, missions.AsReadOnly(), Array.AsReadOnly(paths), bytes);
    }
}
