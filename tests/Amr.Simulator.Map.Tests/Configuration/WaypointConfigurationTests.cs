using Amr.Simulator.Navigation;
using System.Text;
using Amr.Simulator.Map.Control;
using Xunit;

namespace Amr.Simulator.Map.Tests;

public sealed class WaypointConfigurationTests
{
    private static readonly GridCell[] Route = [new(2, 3), new(4, 3), new(4, 5)];
    private static GridNavigator Mission() => new(new(0, 0), Route);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissionConfigurationPreservesOrderAndAcceptsUtf8Bom(bool bom)
    {
        await WithFile("""{"amrId":"AMR_001","start":{"row":0,"col":0},"waypoints":[{"row":2,"col":3},{"row":4,"col":3},{"row":4,"col":5}]}""", async path =>
        {
            if (bom)
            {
                var original = await File.ReadAllBytesAsync(path);
                await File.WriteAllBytesAsync(path, new byte[] { 0xef, 0xbb, 0xbf }.Concat(original).ToArray());
            }
            var settings = await GridMissionSettings.LoadAsync(path);
            Assert.Equal("AMR_001", settings.AmrId);
            Assert.Equal(Route, settings.Waypoints);
        });
    }

    [Theory]
    [InlineData("{\"amrId\":\"AMR_001\",\"start\":{\"row\":0,\"col\":0},\"waypoints\":[]}")]
    [InlineData("{\"amrId\":\"AMR_001\",\"start\":{\"row\":0},\"waypoints\":[{\"row\":4,\"col\":5}]}")]
    [InlineData("{\"amrId\":\"bad/id\",\"start\":{\"row\":0,\"col\":0},\"waypoints\":[{\"row\":4,\"col\":5}]}")]
    [InlineData("{\"amrId\":\"AMR_001\",\"start\":{\"row\":0,\"col\":0},\"waypoints\":[{\"row\":0,\"col\":2}]}")]
    [InlineData("{\"amrId\":\"AMR_001\",\"amrId\":\"other\",\"start\":{\"row\":0,\"col\":0},\"waypoints\":[{\"row\":4,\"col\":5}]}")]
    [InlineData("{\"amrId\":\"AMR_001\",\"start\":{\"row\":\"0\",\"col\":0},\"waypoints\":[{\"row\":4,\"col\":5}]}")]
    [InlineData("{\"amrId\":\"AMR_001\",\"start\":{\"row\":0,\"col\":0},\"waypoints\":[{\"row\":4,\"col\":5}],\"unknown\":1}")]
    public async Task InvalidMissionConfigurationRejected(string json) =>
        await WithFile(json, path => Assert.ThrowsAsync<ArgumentException>(() => GridMissionSettings.LoadAsync(path)));

    [Fact]
    public async Task InvalidUtf8NotSilentlyReplaced()
    {
        await WithFile("{}", async path =>
        {
            await File.WriteAllBytesAsync(path, [0xef, 0xbb, 0xbf, 0xff]);
            await Assert.ThrowsAsync<DecoderFallbackException>(() => GridMissionSettings.LoadAsync(path));
        });
    }

    private static async Task WithFile(string json, Func<string, Task> action)
    {
        string path = Path.Combine(Path.GetTempPath(), "mission-config-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));
        try { await action(path); }
        finally { File.Delete(path); }
    }
}
