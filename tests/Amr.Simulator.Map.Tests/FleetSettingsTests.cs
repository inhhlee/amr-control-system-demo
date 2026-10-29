using System.Text.Json;
using System.Text.Json.Nodes;
using Amr.Simulator.Map.Control;
using Xunit;

namespace Amr.Simulator.Map.Tests;

public sealed class FleetSettingsTests
{
    [Fact]
    public async Task TwoDistinctMissionsLoadAndUnstartedHostDoesNotInventReceivedPositions()
    {
        await using var files = new Files();
        var settings = await FleetMissionSettings.LoadAsync(files.Fleet);
        Assert.Equal(2, settings.Missions.Count);
        await using var session = new FleetMapSession(settings, Path.Combine(files.Root, "evidence"));
        var state = JsonSerializer.SerializeToElement(session.Read());
        Assert.Equal(2, state.GetProperty("fleet").GetArrayLength());
        Assert.Equal(2, state.GetProperty("domain").GetProperty("Statuses").GetArrayLength());
        Assert.Empty(state.GetProperty("domain").GetProperty("Devices").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("position").ValueKind);
        Assert.False(session.Cancel("A"));
        Assert.False(session.Reset("A"));
    }

    [Theory]
    [InlineData("duplicate-id")] [InlineData("different-broker")] [InlineData("different-map")]
    [InlineData("no-mqtt")] [InlineData("one-mission")]
    public async Task InvalidFleetIsRejectedBeforeAnyConnection(string scenario)
    {
        await using var files = new Files();
        var second = JsonNode.Parse(await File.ReadAllTextAsync(files.B))!;
        switch (scenario)
        {
            case "duplicate-id": second["amrId"] = "A"; break;
            case "different-broker": second["mqtt"]!["port"] = 1885; break;
            case "different-map": second["map"]![0]![2] = 1; break;
            case "no-mqtt": second["mqtt"]!["enabled"] = false; break;
            case "one-mission": await File.WriteAllTextAsync(files.Fleet, """{"webPort":0,"missions":["a.json"]}"""); break;
        }
        await File.WriteAllTextAsync(files.B, second.ToJsonString());
        await Assert.ThrowsAsync<ArgumentException>(() => FleetMissionSettings.LoadAsync(files.Fleet));
    }

    private sealed class Files : IAsyncDisposable
    {
        public string Root = Path.Combine(Path.GetTempPath(), "amr-fleet-config-" + Guid.NewGuid().ToString("N"));
        public string Fleet => Path.Combine(Root, "fleet.json");
        public string B => Path.Combine(Root, "b.json");
        public Files()
        {
            Directory.CreateDirectory(Root);
            foreach (string id in new[] { "A", "B" }) File.WriteAllText(Path.Combine(Root, id.ToLowerInvariant() + ".json"), $$$"""
                {"amrId":"{{{id}}}","start":{"row":0,"col":0},"waypoints":[{"row":1,"col":2}],"map":[[0,0,0],[0,0,0]],
                "webPort":0,"mqtt":{"enabled":true,"host":"127.0.0.1","port":1884,"telemetryMs":200,"heartbeatMs":1000}}
                """
            );
            File.WriteAllText(Fleet, """{"webPort":0,"missions":["a.json","b.json"]}""");
        }
        public ValueTask DisposeAsync() { Directory.Delete(Root, true); return ValueTask.CompletedTask; }
    }
}
