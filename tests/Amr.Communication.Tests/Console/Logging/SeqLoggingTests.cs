using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Amr.Communication.ConsoleApp;
using Amr.Communication.ConsoleApp.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class SeqLoggingTests
{
    private static readonly Uri Endpoint = new("http://localhost:5341/");

    [Fact]
    public async Task ConcurrentEvents_AreStructuredAndFlushedWithoutMixingAmrs()
    {
        var requests = new ConcurrentQueue<string>();
        var handler = new Handler(async (request, token) =>
        {
            Assert.Equal("/ingest/clef", request.RequestUri!.AbsolutePath);
            Assert.Equal("application/vnd.serilog.clef", request.Content!.Headers.ContentType!.MediaType);
            requests.Enqueue(await request.Content.ReadAsStringAsync(token));
            return new(HttpStatusCode.Created);
        });
        var sink = new SeqLogSink(Endpoint, handler: handler);
        using var logger = new LoggerConfiguration().Enrich.WithProperty("RunId", "run-26").WriteTo.Sink(sink).CreateLogger();
        var log = new StructuredLog(logger);
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() =>
            log.Event("Received", properties: [("AmrId", $"AMR_{i}"), ("Sequence", i)]))));
        log.Event("ExitCode", properties: [("ExitCode", 0)]);
        await sink.StopAsync(TimeSpan.FromSeconds(3));
        Assert.True(handler.Disposed);
        Assert.Equal(new SeqLogStatistics(101, 0, 0), sink.Statistics);
        var events = requests.SelectMany(r => r.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            .Select(line => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line)!).ToArray();
        Assert.Equal(101, events.Length);
        Assert.All(events, e => Assert.Equal("run-26", e["RunId"].GetString()));
        foreach (var e in events.Where(e => e["EventType"].GetString() == "Received"))
            Assert.Equal($"AMR_{e["Sequence"].GetInt32()}", e["AmrId"].GetString());
        Assert.Equal("ExitCode", events[^1]["EventType"].GetString());
    }

    [Theory]
    [InlineData(401)]
    [InlineData(500)]
    public async Task HttpFailure_IsCountedAndLaterEventsCanRecover(int status)
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var handler = new Handler((_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                first.SetResult();
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
        });
        var sink = new SeqLogSink(Endpoint, handler: handler);
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        logger.Information("Before");
        await first.Task.WaitAsync(TimeSpan.FromSeconds(2));
        logger.Information("After");
        await sink.StopAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new SeqLogStatistics(1, 1, 0), sink.Statistics);
        Assert.True(handler.Disposed);
    }

    [Fact]
    public async Task NetworkFailure_IsIsolatedAndResourcesAreDisposed()
    {
        var handler = new Handler((_, _) => throw new HttpRequestException("SECRET_SERVER_MESSAGE"));
        var sink = new SeqLogSink(Endpoint, handler: handler);
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        logger.Information("Connecting");
        await sink.StopAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(new SeqLogStatistics(0, 1, 0), sink.Statistics);
        Assert.True(handler.Disposed);
    }

    [Fact]
    public async Task OverloadAndSlowServer_DoNotBlockProducer_StopCancelsActualHttpOperation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool canceled = false;
        var handler = new Handler(async (_, token) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { canceled = true; throw; }
            return new(HttpStatusCode.Created);
        });
        var sink = new SeqLogSink(Endpoint, handler: handler, capacity: 8, requestTimeout: TimeSpan.FromSeconds(30));
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        logger.Information("First");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Run(() => { for (int i = 0; i < 1000; i++) logger.Information("Event {Index}", i); })
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(992, sink.Statistics.Dropped);
        var watch = Stopwatch.StartNew();
        await Task.WhenAll(sink.StopAsync(TimeSpan.FromMilliseconds(100)), sink.StopAsync(TimeSpan.FromMilliseconds(100)))
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
        Assert.True(canceled);
        Assert.True(handler.Disposed);
        Assert.Equal(new SeqLogStatistics(0, 1, 1000), sink.Statistics);
        logger.Information("Late");
        Assert.Equal(1001, sink.Statistics.Dropped);
    }

    [Fact]
    public async Task RequestDeadline_WorksBeforeShutdown()
    {
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async (_, token) =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { canceled.TrySetResult(); throw; }
            return new(HttpStatusCode.Created);
        });
        var sink = new SeqLogSink(Endpoint, handler: handler, requestTimeout: TimeSpan.FromMilliseconds(100));
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        logger.Information("Deadline");
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await sink.StopAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, sink.Statistics.Failed);
    }

    [Fact]
    public async Task OversizedEvent_IsDroppedWithoutHttpRequest()
    {
        var handler = new Handler((_, _) => throw new InvalidOperationException("Must not send"));
        var sink = new SeqLogSink(Endpoint, handler: handler);
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        logger.Information("Large {Value}", new string('x', 17000));
        await sink.StopAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(new SeqLogStatistics(0, 0, 1), sink.Statistics);
    }

    [Theory]
    [InlineData("http://user:secret@localhost:5341")]
    [InlineData("http://localhost:5341/?apiKey=secret")]
    [InlineData("file:///secret")]
    [InlineData("not-a-url")]
    public void InvalidConfiguration_IsDisabledWithoutPrintingValues(string url)
    {
        using var error = new StringWriter();
        Assert.Null(SeqLogging.FromEnvironment(error, key => key == "AMR_SEQ_URL" ? url : null));
        Assert.Equal("SeqDisabled Reason=InvalidConfiguration" + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void UnconfiguredSeq_HasNoOutputOrSideEffects()
    {
        using var error = new StringWriter();
        Assert.Null(SeqLogging.FromEnvironment(error, _ => null));
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public async Task ConnectionFailure_ConsoleContractAndSafeStructuredReasonArePreserved()
    {
        var events = new CaptureSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(events).CreateLogger();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var client = new FakeMqttClient();
        client.ConnectResponse.SetException(new IOException("SECRET_PAYLOAD"));
        int code = await ConsoleRunner.RunAsync([], () => client, default, output, error, new StructuredLog(logger));
        Assert.Equal(1, code);
        Assert.Contains("ConnectFailed Reason=TransportFailure", error.ToString());
        Assert.Contains("ExitCode=1", output.ToString());
        var failure = Assert.Single(events.Events, e => e.Properties["EventType"].ToString() == "\"ConnectFailed\"");
        Assert.Equal("\"TransportFailure\"", failure.Properties["Reason"].ToString());
        Assert.All(events.Events, e => Assert.Null(e.Exception));
        Assert.DoesNotContain("SECRET_PAYLOAD", string.Join("\n", events.Events.Select(e => e.RenderMessage() + string.Join(',', e.Properties))));
    }

    [Fact]
    public void LongProperty_IsBoundedAndExplicitlyMarked()
    {
        var sink = new CaptureSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        new StructuredLog(logger).Event("Received", properties: [("AmrId", new string('a', 513))]);
        var e = Assert.Single(sink.Events);
        Assert.Equal(512, ((ScalarValue)e.Properties["AmrId"]).Value!.ToString()!.Length);
        Assert.Equal(true, ((ScalarValue)e.Properties["AmrIdTruncated"]).Value);
    }

    private sealed class CaptureSink : ILogEventSink
    {
        internal ConcurrentQueue<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal bool Disposed { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
