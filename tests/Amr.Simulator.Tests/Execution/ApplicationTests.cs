using System.Globalization;
using Amr.Simulator;
using Xunit;
using static Amr.Simulator.Tests.ConfigurationTests;

namespace Amr.Simulator.Tests;

public sealed class ApplicationTests
{
    [Fact]
    public async Task ConfiguredInitialization_PrintsValuesRouteAndIdleState()
    {
        using var file = new TemporaryConfig(ValidJson);
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = await SimulatorApplication.RunAsync(["--config", file.FilePath], output, error, clock: new TestSimulationClock(100));
        Assert.Equal(0, code);
        Assert.Equal("", error.ToString());
        string text = output.ToString();
        Assert.Contains("AmrId=AMR_001 InitialX=-2 InitialY=3.5 SpeedMps=1.25 UpdateIntervalMs=100 WaypointCount=2", text);
        Assert.Contains("Waypoint Index=0 X=1 Y=2", text);
        Assert.Contains("Waypoint Index=1 X=-4 Y=-5", text);
        Assert.Contains("AmrInitialized AmrId=AMR_001 X=-2 Y=3.5 State=IDLE CurrentSpeedMps=0", text);
        Assert.Contains("InitializationCompleted" + Environment.NewLine, text);
        Assert.Contains("SimulationCompleted AmrId=AMR_001 X=-4 Y=-5 State=IDLE CurrentSpeedMps=0", text);
    }

    [Fact]
    public async Task InvalidConfiguration_HasErrorExitAndNoCreatedAmr()
    {
        using var file = new TemporaryConfig(Change(root => root["speedMps"] = -1));
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = await SimulatorApplication.RunAsync(["--config", file.FilePath], output, error);
        Assert.Equal(1, code);
        Assert.Equal("", output.ToString());
        Assert.Contains("ConfigurationRejected Field=$.speedMps Reason=", error.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidUtf8InId_WithOrWithoutBom_HasErrorExitAndNoInitialization(bool withBom)
    {
        using var file = new TemporaryConfig(ValidJson);
        await File.WriteAllBytesAsync(file.FilePath, InvalidUtf8InId(withBom));
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = await SimulatorApplication.RunAsync(["--config", file.FilePath], output, error);
        Assert.Equal(1, code);
        Assert.Equal("", output.ToString());
        Assert.Equal("ConfigurationRejected Field=$file Reason=설정 파일의 문자 인코딩을 읽을 수 없습니다."
            + Environment.NewLine, error.ToString());
    }

    public static IEnumerable<object[]> InvalidArguments()
    {
        yield return [Array.Empty<string>()];
        yield return [new[] { "--config" }];
        yield return [new[] { "--config", "" }];
        yield return [new[] { "--unknown", "a.json" }];
        yield return [new[] { "--config", "a.json", "--config", "b.json" }];
        yield return [new[] { "--config", "a.json", "--extra" }];
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public async Task InvalidOptions_AreRejectedWithoutInitialization(string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, await SimulatorApplication.RunAsync(args, output, error));
        Assert.Equal("", output.ToString());
        Assert.Contains("Field=arguments", error.ToString());
    }

    [Fact]
    public async Task MissingFile_HasErrorExitAndNoCreatedAmr()
    {
        using var file = new TemporaryConfig(ValidJson);
        File.Delete(file.FilePath);
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, await SimulatorApplication.RunAsync(["--config", file.FilePath], output, error));
        Assert.Equal("", output.ToString());
        Assert.Contains("Field=$file", error.ToString());
    }

    [Fact]
    public async Task Cancellation_IsNotSuccessfulInitialization()
    {
        using var file = new TemporaryConfig(ValidJson);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, await SimulatorApplication.RunAsync(["--config", file.FilePath], output, error, cancellation.Token));
        Assert.Equal("InitializationCanceled" + Environment.NewLine, output.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public async Task Output_UsesInvariantNumbers()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            using var file = new TemporaryConfig(ValidJson);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, await SimulatorApplication.RunAsync(["--config", file.FilePath], output, error, clock: new TestSimulationClock(100)));
            Assert.Contains("InitialY=3.5 SpeedMps=1.25", output.ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task Help_DoesNotInitializeAmr()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, await SimulatorApplication.RunAsync(["--help"], output, error));
        Assert.Contains("Usage:", output.ToString());
        Assert.DoesNotContain("AmrInitialized", output.ToString());
        Assert.Equal("", error.ToString());
    }
}
