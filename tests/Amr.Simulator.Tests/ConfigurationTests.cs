using System.Text;
using System.Text.Json.Nodes;
using Amr.Simulator.Configuration;
using Amr.Simulator.Simulation;
using Xunit;

namespace Amr.Simulator.Tests;

public sealed class ConfigurationTests
{
    internal const string ValidJson = """
        {
          "amrId": "AMR_001",
          "initialPosition": { "x": -2, "y": 3.5 },
          "speedMps": 1.25,
          "updateIntervalMs": 100,
          "waypoints": [{ "x": 1, "y": 2 }, { "x": -4, "y": -5 }]
        }
        """;

    internal static string Change(Action<JsonObject> change)
    {
        var root = JsonNode.Parse(ValidJson)!.AsObject();
        change(root);
        return root.ToJsonString();
    }

    [Fact]
    public void ValidSettings_CreateIdleAmrWithZeroCurrentSpeedAndPreserveRoute()
    {
        var settings = SimulatorSettingsLoader.Parse(ValidJson);
        var amr = new SimulatedAmr(settings);
        Assert.Equal("AMR_001", amr.Id);
        Assert.Equal(new Position2D(-2, 3.5), amr.Position);
        Assert.Equal("IDLE", amr.State);
        Assert.Equal(0, amr.CurrentSpeedMps);
        Assert.Equal(1.25, settings.SpeedMps);
        Assert.Equal(100, settings.UpdateIntervalMs);
        Assert.Equal(new[] { new Position2D(1, 2), new Position2D(-4, -5) }, settings.Waypoints);
        var list = Assert.IsAssignableFrom<IList<Position2D>>(settings.Waypoints);
        Assert.Throws<NotSupportedException>(() => list[0] = new Position2D(99, 99));
    }

    [Fact]
    public void ChangedSettings_ChangeInitialAmrAndRetainConfiguredSpeedAndRoute()
    {
        var settings = SimulatorSettingsLoader.Parse(Change(root =>
        {
            root["amrId"] = "AMR_002";
            root["initialPosition"] = new JsonObject { ["x"] = 12, ["y"] = -7 };
            root["speedMps"] = 2.5;
            root["waypoints"] = new JsonArray(new JsonObject { ["x"] = -8, ["y"] = 9 });
        }));
        var amr = new SimulatedAmr(settings);
        Assert.Equal("AMR_002", amr.Id);
        Assert.Equal(new Position2D(12, -7), amr.Position);
        Assert.Equal(2.5, settings.SpeedMps);
        Assert.Equal(new Position2D(-8, 9), Assert.Single(settings.Waypoints));
        Assert.Equal("IDLE", amr.State);
        Assert.Equal(0, amr.CurrentSpeedMps);
    }

    [Fact]
    public void IntegerInterval_IsNotSilentlyClampedToInt32()
    {
        var settings = SimulatorSettingsLoader.Parse(Change(root => root["updateIntervalMs"] = 2147483648L));
        Assert.Equal(2147483648L, settings.UpdateIntervalMs);
    }

    [Fact]
    public void SamePositionWaypoints_AreValidConfiguration()
    {
        var settings = SimulatorSettingsLoader.Parse(Change(root =>
            root["waypoints"] = new JsonArray(new JsonObject { ["x"] = -2, ["y"] = 3.5 })));
        Assert.Equal(settings.InitialPosition, Assert.Single(settings.Waypoints));
    }

    public static IEnumerable<object[]> InvalidConfigurations()
    {
        foreach (string key in new[] { "amrId", "initialPosition", "speedMps", "updateIntervalMs", "waypoints" })
            yield return [Change(root => root.Remove(key)), "$." + key];
        foreach (string key in new[] { "x", "y" })
        {
            yield return [Change(root => root["initialPosition"]!.AsObject().Remove(key)), "$.initialPosition." + key];
            yield return [Change(root => root["waypoints"]![0]!.AsObject().Remove(key)), "$.waypoints[0]." + key];
        }
        foreach (string id in new[] { "", " ", " AMR_001", "AMR 001", "A/B", "A+B", "A#B", "A\tB", "A\nB", "A\u0001B", "A\u00a0B" })
            yield return [Change(root => root["amrId"] = id), "$.amrId"];
        yield return [Change(root => root["amrId"] = 12), "$.amrId"];
        yield return [Change(root => root["amrId"] = null), "$.amrId"];
        yield return [ValidJson.Replace("AMR_001", @"\uD800"), "$.amrId"];

        foreach (string key in new[] { "initialPosition", "waypoints" })
            yield return [Change(root => root[key] = null), "$." + key];
        yield return [Change(root => root["initialPosition"] = new JsonArray()), "$.initialPosition"];
        yield return [Change(root => root["initialPosition"]!["x"] = "0"), "$.initialPosition.x"];
        yield return [Change(root => root["initialPosition"]!["y"] = true), "$.initialPosition.y"];
        yield return [Change(root => root["initialPosition"]!["x"] = null), "$.initialPosition.x"];
        yield return [ValidJson.Replace("\"x\": -2", "\"x\": 1e400"), "$.initialPosition.x"];
        foreach (double speed in new[] { 0d, -1d })
            yield return [Change(root => root["speedMps"] = speed), "$.speedMps"];
        foreach (string speed in new[] { "NaN", "Infinity", "1.25" })
            yield return [Change(root => root["speedMps"] = speed), "$.speedMps"];
        yield return [Change(root => root["speedMps"] = null), "$.speedMps"];
        yield return [Change(root => root["speedMps"] = false), "$.speedMps"];
        yield return [ValidJson.Replace("\"speedMps\": 1.25", "\"speedMps\": 1e400"), "$.speedMps"];
        foreach (double interval in new[] { 0d, -100d, 0.5 })
            yield return [Change(root => root["updateIntervalMs"] = interval), "$.updateIntervalMs"];
        yield return [Change(root => root["updateIntervalMs"] = "100"), "$.updateIntervalMs"];
        yield return [Change(root => root["updateIntervalMs"] = null), "$.updateIntervalMs"];
        yield return [ValidJson.Replace("\"updateIntervalMs\": 100", "\"updateIntervalMs\": 9223372036854775808"), "$.updateIntervalMs"];
        yield return [Change(root => root["waypoints"] = new JsonArray()), "$.waypoints"];
        yield return [Change(root => root["waypoints"] = new JsonObject()), "$.waypoints"];
        yield return [Change(root => root["waypoints"]![0] = null), "$.waypoints[0]"];
        yield return [Change(root => root["waypoints"]![1]!["x"] = "NaN"), "$.waypoints[1].x"];
        yield return [ValidJson.Replace("\"y\": -5", "\"y\": -1e400"), "$.waypoints[1].y"];
        yield return [Change(root => root["unexpected"] = 1), "$"];
        yield return [Change(root => root["initialPosition"]!["z"] = 1), "$.initialPosition"];
        yield return [Change(root => root["waypoints"]![0]!["z"] = 1), "$.waypoints[0]"];
        yield return [ValidJson.Replace("\"amrId\":", "\"AmrId\":"), "$"];
        yield return [ValidJson.Replace("\"amrId\":", "\"\\uD800\":"), "$"];
        yield return [ValidJson.Replace("\"amrId\": \"AMR_001\",", "\"amrId\": \"AMR_001\", \"amrId\": \"AMR_002\","), "$.amrId"];
        yield return [ValidJson.Replace("\"x\": -2", "\"x\": -2, \"x\": 4"), "$.initialPosition.x"];
        yield return [ValidJson.Replace("\"x\": 1", "\"x\": 1, \"x\": 2"), "$.waypoints[0].x"];
        foreach (string json in new[] { "", "{", "null", "[]", "3", """{"amrId": "A",}""" })
            yield return [json, "$"];
    }

    [Theory]
    [MemberData(nameof(InvalidConfigurations))]
    public void InvalidInput_IsRejectedWithFieldAndReason(string json, string expectedField)
    {
        var error = Assert.Throws<ConfigurationException>(() => SimulatorSettingsLoader.Parse(json));
        Assert.Equal(expectedField, error.Field);
        Assert.False(string.IsNullOrWhiteSpace(error.Reason));
    }

    [Fact]
    public async Task BomFile_LoadsWithoutChangingValues()
    {
        using var file = new TemporaryConfig(ValidJson, new UTF8Encoding(true));
        var settings = await SimulatorSettingsLoader.LoadAsync(file.FilePath);
        Assert.Equal("AMR_001", settings.AmrId);
        Assert.Equal(new Position2D(-2, 3.5), settings.InitialPosition);
    }

    [Fact]
    public async Task MissingFile_HasFileError()
    {
        using var file = new TemporaryConfig(ValidJson);
        File.Delete(file.FilePath);
        var error = await Assert.ThrowsAsync<ConfigurationException>(() => SimulatorSettingsLoader.LoadAsync(file.FilePath));
        Assert.Equal("$file", error.Field);
        Assert.Contains("FileNotFoundException", error.Reason);
    }

    [Fact]
    public async Task LockedFile_IsRejectedBeforeInitialization()
    {
        using var file = new TemporaryConfig(ValidJson);
        using var locked = new FileStream(file.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var error = await Assert.ThrowsAsync<ConfigurationException>(() => SimulatorSettingsLoader.LoadAsync(file.FilePath));
        Assert.Equal("$file", error.Field);
    }

    [Fact]
    public async Task InvalidByteEncoding_IsRejected()
    {
        using var file = new TemporaryConfig(ValidJson);
        await File.WriteAllBytesAsync(file.FilePath, [0xc3, 0x28]);
        var error = await Assert.ThrowsAsync<ConfigurationException>(() => SimulatorSettingsLoader.LoadAsync(file.FilePath));
        Assert.Equal("$file", error.Field);
    }
}

internal sealed class TemporaryConfig : IDisposable
{
    public TemporaryConfig(string json, Encoding? encoding = null)
    {
        FilePath = Path.Combine(Path.GetTempPath(), $"amr-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(FilePath, json, encoding ?? new UTF8Encoding(false));
    }

    public string FilePath { get; }
    public void Dispose() => File.Delete(FilePath);
}
