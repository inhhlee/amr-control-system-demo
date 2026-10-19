using System.Buffers;
using System.Text.Json;
using Amr.Communication.ConsoleApp;
using Amr.Communication.ConsoleApp.Logging;
using MQTTnet;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class CommandConsoleTests
{
    private static readonly string[] StopArgs = ["--command", "STOP", "--amr-id", "AMR_001"];

    public static IEnumerable<object[]> InvalidOptions()
    {
        yield return [new[] { "--command", "MOVE", "--amr-id", "A" }];
        yield return [new[] { "--command", "MOVE", "--amr-id", "A", "--x", "1" }];
        foreach (string value in new[] { "NaN", "Infinity", "1e309", "1,2", "bad" })
            yield return [new[] { "--command", "MOVE", "--amr-id", "A", "--x", value, "--y", "1" }];
        yield return [new[] { "--command", "RESET", "--amr-id", "A", "--x", "1", "--y", "2" }];
        yield return [new[] { "--command", "stop", "--amr-id", "A" }];
        yield return [new[] { "--command", "STOP" }];
        yield return [new[] { "--command", "STOP", "--amr-id", "A/#" }];
        yield return [new[] { "--command", "STOP", "--amr-id", " " }];
        yield return [StopArgs.Concat(new[] { "--amr-id", "B" }).ToArray()];
        yield return [StopArgs.Concat(new[] { "--show-payload" }).ToArray()];
        foreach (string option in new[] { "--port", "--connect-timeout-seconds", "--publish-timeout-seconds" })
            foreach (string value in new[] { "0", "-1", "2147483647", "abc" })
                yield return [StopArgs.Concat(new[] { option, value }).ToArray()];
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public async Task InvalidOptions_DoNotEvenCreateClient(string[] args)
    {
        int factoryCalls = 0;
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await ConsoleRunner.RunAsync(args, () => { factoryCalls++; return new FakeMqttClient(); }, default, output, error);
        Assert.Equal(1, exit);
        Assert.Equal(0, factoryCalls);
        Assert.DoesNotContain("CommandPublished", output.ToString());
        Assert.NotEmpty(error.ToString());
    }

    [Theory]
    [InlineData("MOVE")]
    [InlineData("STOP")]
    [InlineData("RESET")]
    public async Task OneCommand_PreservesGeneratedIdToWireAndStructuredLogs_ThenDisconnects(string type)
    {
        var ids = new HashSet<string>();
        for (int i = 0; i < 2; i++)
        {
            var client = ReadyClient();
            var sink = new Capture();
            using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
            using var output = new StringWriter();
            using var error = new StringWriter();
            string[] args = ["--command", type, "--amr-id", "AMR_001"];
            if (type == "MOVE") args = [.. args, "--x", "-30.5", "--y", "15.25"];
            int exit = await ConsoleRunner.RunAsync(args, () => client, default, output, error, new StructuredLog(logger));
            Assert.Equal(0, exit);
            Assert.Empty(error.ToString());
            using var payload = JsonDocument.Parse(client.PublishedMessage!.Payload);
            var root = payload.RootElement;
            string id = root.GetProperty("cmdId").GetString()!;
            Assert.StartsWith("CMD-", id);
            Assert.True(Guid.TryParseExact(id[4..], "N", out _));
            Assert.True(ids.Add(id));
            Assert.Equal(type, root.GetProperty("type").GetString());
            Assert.InRange(root.GetProperty("ts").GetInt64(), DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (type == "MOVE") Assert.Equal(-30.5, root.GetProperty("param").GetProperty("x").GetDouble());
            else Assert.Empty(root.GetProperty("param").EnumerateObject());
            Assert.Equal("amr/AMR_001/command", client.PublishedMessage.Topic);
            Assert.Equal(1, client.PublishCalls);
            Assert.Equal(1, client.DisconnectCalls);
            Assert.Equal(1, client.DisposeCalls);
            Assert.Null(client.SubscribeOptions);
            Assert.Contains("Execution=NotTracked", output.ToString());
            Assert.DoesNotContain("SUCCESS", output.ToString());
            foreach (string eventType in new[] { "CommandRequested", "CommandPublished" })
            {
                var entry = Assert.Single(sink.Events, e => Value(e, "EventType") == eventType);
                Assert.Equal(id, Value(entry, "CommandId"));
                Assert.Equal("AMR_001", Value(entry, "AmrId"));
                Assert.Equal(type, Value(entry, "CommandType"));
                Assert.False(entry.Properties.ContainsKey("Payload"));
            }
        }
    }

    [Theory]
    [InlineData("connect-fail", "ConnectFailed", 1)]
    [InlineData("publish-fail", "CommandPublishFailed", 1)]
    [InlineData("cancel", "CommandCanceled", 0)]
    [InlineData("timeout", "CommandPublishFailed", 1)]
    public async Task FailuresAndCancellation_AreDistinctAndCleanClient(string scenario, string expected, int expectedExit)
    {
        var client = new FakeMqttClient();
        client.DisconnectCompletion.SetResult();
        if (scenario == "connect-fail") client.ConnectResponse.SetException(new IOException("secret network detail"));
        else client.ConnectResponse.SetResult(new MqttClientConnectResult { ResultCode = MqttClientConnectResultCode.Success });
        if (scenario == "publish-fail") client.PublishResponse.SetException(new IOException("secret network detail"));
        using var user = new CancellationTokenSource();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var pending = ConsoleRunner.RunAsync([.. StopArgs, "--publish-timeout-seconds", "1"], () => client, user.Token, output, error);
        if (scenario == "cancel")
        {
            await client.PublishEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            user.Cancel();
        }
        Assert.Equal(expectedExit, await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        string text = output + error.ToString();
        Assert.Contains(expected, text);
        Assert.DoesNotContain("CommandPublished", text);
        Assert.DoesNotContain("SUCCESS", text);
        Assert.DoesNotContain("secret", text);
        if (scenario == "timeout") Assert.Contains("Reason=Timeout Delivery=Unknown", text);
        if (scenario == "cancel") Assert.Contains("Reason=UserCanceled Delivery=Unknown", text);
        Assert.Equal(scenario == "connect-fail" ? 0 : 1, client.PublishCalls);
        Assert.Equal(1, client.DisposeCalls);
    }

    [Fact]
    public async Task DisconnectFailure_DoesNotEraseFactOfPublication()
    {
        var client = new FakeMqttClient();
        client.ConnectResponse.SetResult(new MqttClientConnectResult { ResultCode = MqttClientConnectResultCode.Success });
        client.PublishResponse.SetResult(CommandPublisherTests.Result());
        client.DisconnectCompletion.SetException(new IOException());
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, await ConsoleRunner.RunAsync(StopArgs, () => client, default, output, error));
        Assert.Contains("CommandPublished", output.ToString());
        Assert.Contains("DisconnectFailed", error.ToString());
        Assert.Equal(1, client.PublishCalls);
        Assert.Equal(1, client.DisposeCalls);
    }

    private static FakeMqttClient ReadyClient()
    {
        var client = new FakeMqttClient();
        client.ConnectResponse.SetResult(new MqttClientConnectResult { ResultCode = MqttClientConnectResultCode.Success });
        client.PublishResponse.SetResult(CommandPublisherTests.Result());
        client.DisconnectCompletion.SetResult();
        return client;
    }
    private static string? Value(LogEvent e, string name) => ((ScalarValue)e.Properties[name]).Value?.ToString();
    private sealed class Capture : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
