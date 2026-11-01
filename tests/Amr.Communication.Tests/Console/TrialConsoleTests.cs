using System.Collections.Concurrent;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Amr.Communication.ConsoleApp;
using MQTTnet;
using Xunit;
using static Amr.Communication.Tests.ReconnectingCommunicationSessionTests;

namespace Amr.Communication.Tests;

public sealed class TrialConsoleTests
{
    private sealed class Output : TextWriter
    {
        private readonly ConcurrentQueue<string> _lines = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value) => _lines.Enqueue(value ?? "");
        public override string ToString() => string.Join('\n', _lines);
    }

    [Fact]
    public async Task ConsoleAcceptsCommandsShowsActualAckAndExitsWithoutBusinessSuccessClaim()
    {
        var client = Client(); var output = new Output(); var error = new StringWriter();
        var input = Channel.CreateBounded<string>(10);
        var run = TrialConsoleRunner.RunAsync(["--trial", "--no-color"], () => client, default, output, error,
            readLine: async token => await input.Reader.ReadAsync(token));
        await Until(() => output.ToString().Contains("[접속완료]"));
        await input.Writer.WriteAsync("move 10 20");
        await Until(() => output.ToString().Contains("[명령발행]"));
        using var payload = JsonDocument.Parse(client.PublishedMessage!.Payload.ToArray());
        var root = payload.RootElement;
        Assert.Equal("MOVE", root.GetProperty("type").GetString());
        Assert.Equal(10, root.GetProperty("param").GetProperty("x").GetDouble());
        string id = root.GetProperty("cmdId").GetString()!;
        await client.ReceiveAsync(new MqttApplicationMessageBuilder().WithTopic("amr/AMR_001/ack")
            .WithPayload($$"""{"cmdId":"{{id}}","result":"SUCCESS","ts":1}""").Build());
        await Until(() => output.ToString().Contains("result=SUCCESS"));
        Assert.Contains("(응답 수신값)", output.ToString());
        Assert.Contains("AMR 수행 미확인", output.ToString());
        await input.Writer.WriteAsync("move NaN 1");
        await Until(() => output.ToString().Contains("[입력거부]"));
        Assert.Equal(1, client.PublishCalls);
        await input.Writer.WriteAsync("status");
        await Until(() => output.ToString().Contains("Broker=Connected"));
        await input.Writer.WriteAsync("/exit");
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(4)));
        Assert.Contains("ExitCode=0", output.ToString());
        Assert.Equal("", error.ToString());
        Assert.Equal(1, client.DisposeCalls);
    }

    [Fact]
    public async Task CommandsDuringDisconnectedWaitAreRejectedAndNeverQueued()
    {
        var first = new FakeMqttClient(); first.ConnectResponse.SetException(new IOException());
        var output = new Output(); var input = Channel.CreateBounded<string>(5);
        var run = TrialConsoleRunner.RunAsync(["--trial", "--no-color", "--reconnect-delay-seconds", "60"], () => first,
            default, output, new StringWriter(), readLine: async token => await input.Reader.ReadAsync(token));
        await Until(() => output.ToString().Contains("[재접속대기]"));
        await input.Writer.WriteAsync("stop");
        await Until(() => output.ToString().Contains("[발행거부]"));
        Assert.Equal(0, first.PublishCalls);
        await input.Writer.WriteAsync("exit");
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(1, first.ConnectCalls);
    }

    [Fact]
    public async Task ExhaustionCancelsPendingInputAndReturnsFailureWithoutExtraEnter()
    {
        var c = new FakeMqttClient(); c.ConnectResponse.SetException(new IOException());
        var output = new Output(); var input = Channel.CreateBounded<string>(1);
        bool inputEnded = false;
        var run = TrialConsoleRunner.RunAsync(["--trial", "--no-color", "--reconnect-attempts", "0"], () => c,
            default, output, new StringWriter(), readLine: async token => {
                try { return await input.Reader.ReadAsync(token); }
                finally { inputEnded = true; }
            });
        Assert.Equal(1, await run.WaitAsync(TimeSpan.FromSeconds(4)));
        Assert.True(inputEnded);
        Assert.Contains("[복구실패]", output.ToString());
        Assert.Contains("ExitCode=1", output.ToString());
    }

    [Theory]
    [InlineData("--reconnect-attempts", "-1")]
    [InlineData("--reconnect-attempts", "101")]
    [InlineData("--reconnect-delay-seconds", "0")]
    [InlineData("--port", "65536")]
    [InlineData("--amr-id", "bad/id")]
    public async Task BadOptionsDoNotCreateClient(string key, string value)
    {
        int created = 0;
        int code = await TrialConsoleRunner.RunAsync(["--trial", key, value], () => { created++; return Client(); },
            default, new StringWriter(), new StringWriter(), readLine: _ => ValueTask.FromResult<string?>(null));
        Assert.Equal(1, code); Assert.Equal(0, created);
    }

    [Fact]
    public void LogTextEscapesControlSequencesAndBoundsPayloadDisplay()
    {
        var output = new StringWriter(); var display = new TrialConsoleDisplay(output, false);
        display.Line("수신", "test\u001b[2J\nforged" + new string('a', 2000));
        string text = output.ToString();
        Assert.DoesNotContain('\u001b', text);
        Assert.Contains("\\u001b", text); Assert.Contains("\\u000a", text);
        Assert.Contains("일부 표시", text); Assert.True(text.Length < 1300);
    }
}
