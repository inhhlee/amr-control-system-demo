using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Channels;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Amr.Communication.ConsoleApp.Logging;

internal sealed record SeqLogStatistics(long Sent, long Failed, long Dropped);

/// <summary>
/// Best-effort diagnostics: bounded memory, one HTTP request at a time, no retry of failed batches.
/// A failed response can be ambiguous: Failed means unconfirmed, not proven absent from Seq.
/// The production SocketsHttpHandler observes cancellation; injected handlers must do so too.
/// </summary>
internal sealed class SeqLogSink : ILogEventSink
{
    private readonly Channel<string> _queue;
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _abort = new();
    private readonly Task _worker;
    private readonly object _stopGate = new();
    private Task? _stop;
    private long _sent, _failed, _dropped;
    private const int BatchSize = 128;
    private const int MaxEventBytes = 16 * 1024;
    private static readonly CompactJsonFormatter Formatter = new();

    internal SeqLogSink(Uri server, string? apiKey = null, HttpMessageHandler? handler = null,
        int capacity = 1024, TimeSpan? requestTimeout = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(server.AbsoluteUri.TrimEnd('/') + "/"),
            Timeout = requestTimeout ?? TimeSpan.FromSeconds(1)
        };
        if (!string.IsNullOrEmpty(apiKey)) _http.DefaultRequestHeaders.Add("X-Seq-ApiKey", apiKey);
        _worker = Task.Run(RunAsync);
    }

    public void Emit(LogEvent logEvent)
    {
        // Serialization has no network I/O. Never wait for capacity on an MQTT callback.
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Formatter.Format(logEvent, writer);
        string json = writer.ToString();
        if (Encoding.UTF8.GetByteCount(json) > MaxEventBytes || !_queue.Writer.TryWrite(json))
            Interlocked.Increment(ref _dropped);
    }

    internal SeqLogStatistics Statistics => new(Interlocked.Read(ref _sent),
        Interlocked.Read(ref _failed), Interlocked.Read(ref _dropped));

    internal Task StopAsync(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        lock (_stopGate) return _stop ??= StopCoreAsync(timeout);
    }

    private async Task StopCoreAsync(TimeSpan timeout)
    {
        _queue.Writer.TryComplete();
        _abort.CancelAfter(timeout);
        // Await actual worker termination, not just a timed-out wait that leaves sockets behind.
        await _worker.ConfigureAwait(false);
        _abort.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(_abort.Token).ConfigureAwait(false))
            {
                var batch = new StringBuilder();
                int count = 0;
                while (count < BatchSize && _queue.Reader.TryRead(out var json))
                {
                    batch.Append(json);
                    count++;
                }
                if (count == 0) continue;
                bool failed = false;
                try
                {
                    using var content = new StringContent(batch.ToString(), Encoding.UTF8);
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.serilog.clef");
                    using var request = new HttpRequestMessage(HttpMethod.Post, "ingest/clef") { Content = content };
                    // Do not buffer an arbitrary error response body or include it in diagnostics.
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                        _abort.Token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    Interlocked.Add(ref _sent, count);
                }
                catch (Exception)
                {
                    // Logging is an isolation boundary. Never copy server bodies, headers or exception messages.
                    Interlocked.Add(ref _failed, count);
                    failed = true;
                }
                // A dead endpoint must not generate a tight failure loop. Later events can recover.
                if (failed) await Task.Delay(TimeSpan.FromMilliseconds(250), _abort.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_abort.IsCancellationRequested) { }
        finally
        {
            _queue.Writer.TryComplete();
            while (_queue.Reader.TryRead(out _)) Interlocked.Increment(ref _dropped);
            _http.Dispose();
        }
    }
}
