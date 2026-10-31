using System.Text;
using System.Text.Json.Nodes;
using Amr.Simulator.Configuration;
using Amr.Simulator.Simulation;
using Xunit;

namespace Amr.Simulator.Tests;

public sealed class FleetSettingsTests
{
    internal static string FleetJson(int count = 2, Action<JsonObject>? change = null)
    {
        var items = new JsonArray();
        for (int i = 0; i < count; i++)
        {
            var item = JsonNode.Parse(ConfigurationTests.ValidJson)!.AsObject();
            item["amrId"] = $"AMR_{i + 1:D3}";
            item["initialPosition"] = new JsonObject { ["x"] = i * 2, ["y"] = -i };
            item["speedMps"] = i + 1;
            item["waypoints"] = new JsonArray(new JsonObject { ["x"] = i * 2 + 1, ["y"] = -i });
            item["faults"] = new JsonArray();
            items.Add(item);
        }
        var root = new JsonObject { ["amrs"] = items };
        change?.Invoke(root);
        return root.ToJsonString();
    }

    private static JsonObject Fault() => new()
    {
        ["code"] = "SIM_SENSOR", ["level"] = "ERR", ["activateAfterMs"] = 0,
        ["resolveAfterMs"] = 1000, ["message"] = "시험 장애"
    };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CountAndPerAmrConditions_AreTakenFromFile(int count)
    {
        var fleet = FleetSettingsLoader.Parse(FleetJson(count));
        Assert.Equal(count, fleet.Amrs.Count);
        for (int i = 0; i < count; i++)
        {
            var configured = fleet.Amrs[i];
            Assert.Equal($"AMR_{i + 1:D3}", configured.Settings.AmrId);
            Assert.Equal(new Position2D(i * 2, -i), configured.Settings.InitialPosition);
            Assert.Equal(i + 1, configured.Settings.SpeedMps);
            Assert.Empty(configured.Faults);
        }
        Assert.Throws<NotSupportedException>(() => ((IList<ConfiguredAmr>)fleet.Amrs).Clear());
    }

    [Fact]
    public void FaultTimesAndAlarmFields_ArePreservedAsReadOnlyConfiguration()
    {
        var fleet = FleetSettingsLoader.Parse(FleetJson(change: r =>
            r["amrs"]![1]!["faults"] = new JsonArray(Fault())));
        Assert.Empty(fleet.Amrs[0].Faults);
        Assert.Equal(new FaultScenario("SIM_SENSOR", "ERR", 0, 1000, "시험 장애"), Assert.Single(fleet.Amrs[1].Faults));
        Assert.Throws<NotSupportedException>(() => ((IList<FaultScenario>)fleet.Amrs[1].Faults).Clear());
    }

    public static IEnumerable<object[]> InvalidFleets()
    {
        yield return ["{}", "$.amrs"];
        yield return ["{\"amrs\":[]}", "$.amrs"];
        yield return ["{\"amrs\":null}", "$.amrs"];
        yield return ["{\"amrs\":[null]}", "$.amrs[0]"];
        yield return ["{\"amrs\":[],\"amrs\":[]}", "$.amrs"];
        yield return ["{\"amrs\":[],\"other\":0}", "$"];
        yield return [FleetJson(change: r => r["amrs"]![1]!["amrId"] = "AMR_001"), "$.amrs[1].amrId"];
        yield return [FleetJson(change: r => r["amrs"]![1]!["speedMps"] = -1), "$.amrs[1].speedMps"];
        yield return [FleetJson(change: r => r["amrs"]![1]!["initialPosition"]!.AsObject().Remove("x")), "$.amrs[1].initialPosition.x"];
        yield return [FleetJson(change: r => r["amrs"]![1]!.AsObject().Remove("faults")), "$.amrs[1].faults"];
        yield return [FleetJson(change: r => r["amrs"]![1]!["faults"] = null), "$.amrs[1].faults"];
        yield return [FleetJson(change: r => r["amrs"]![1]!["faults"] = new JsonArray((JsonNode?)null)), "$.amrs[1].faults[0]"];
        yield return [FleetJson().Replace("\"amrId\":\"AMR_001\"", "\"amrId\":\"AMR_001\",\"amrId\":\"OTHER\""), "$.amrs[0].amrId"];
        foreach (var (field, value) in new (string, JsonNode?)[]
        {
            ("code", ""), ("code", 1), ("level", "ERROR"), ("message", ""), ("message", null),
            ("activateAfterMs", -1), ("activateAfterMs", 0.5), ("activateAfterMs", "100"),
            ("resolveAfterMs", 0), ("resolveAfterMs", -1), ("resolveAfterMs", null)
        })
        {
            var f = Fault(); f[field] = value;
            yield return [FleetJson(change: r => r["amrs"]![0]!["faults"] = new JsonArray(f.DeepClone())), "$.amrs[0].faults[0]." + field];
        }
        foreach (string field in new[] { "code", "level", "activateAfterMs", "resolveAfterMs" })
        {
            var f = Fault(); f.Remove(field);
            yield return [FleetJson(change: r => r["amrs"]![0]!["faults"] = new JsonArray(f.DeepClone())), "$.amrs[0].faults[0]." + field];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidFleets))]
    public void InvalidFleet_IsRejectedAtSpecificField(string json, string field)
    {
        var e = Assert.Throws<ConfigurationException>(() => FleetSettingsLoader.Parse(json));
        Assert.Equal(field, e.Field);
        Assert.NotEmpty(e.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FleetFile_UsesStrictUtf8AndSupportsBom(bool bom)
    {
        using var file = new TemporaryConfig(FleetJson(), new UTF8Encoding(bom));
        Assert.Equal(2, (await FleetSettingsLoader.LoadAsync(file.FilePath)).Amrs.Count);
        await File.WriteAllBytesAsync(file.FilePath, bom ? [0xef, 0xbb, 0xbf, 0xc3, 0x28] : [0xc3, 0x28]);
        Assert.Equal("$file", (await Assert.ThrowsAsync<ConfigurationException>(() => FleetSettingsLoader.LoadAsync(file.FilePath))).Field);
    }
}
