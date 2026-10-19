using Serilog;
using Serilog.Core;

namespace Amr.Communication.ConsoleApp.Logging;

internal sealed class SeqLogging
{
    private readonly SeqLogSink _sink;
    private readonly Logger _logger;
    internal StructuredLog Log { get; }
    internal string RunId { get; }

    private SeqLogging(SeqLogSink sink, string runId)
    {
        _sink = sink;
        RunId = runId;
        _logger = new LoggerConfiguration().Enrich.WithProperty("Application", "Amr.Communication.Console")
            .Enrich.WithProperty("RunId", runId).WriteTo.Sink(sink).CreateLogger();
        Log = new StructuredLog(_logger);
    }

    internal static SeqLogging? FromEnvironment(TextWriter error, Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        string? address = read("AMR_SEQ_URL");
        if (string.IsNullOrWhiteSpace(address)) return null;
        string runId = read("AMR_LOG_RUN_ID") ?? Guid.NewGuid().ToString("N");
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            runId.Length is 0 or > 128 || runId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
        {
            error.WriteLine("SeqDisabled Reason=InvalidConfiguration");
            return null;
        }
        string? key = read("AMR_SEQ_API_KEY");
        if (key?.Any(char.IsControl) == true)
        {
            error.WriteLine("SeqDisabled Reason=InvalidConfiguration");
            return null;
        }
        return new SeqLogging(new SeqLogSink(uri, key), runId);
    }

    internal async Task StopAsync(TextWriter error)
    {
        _logger.Dispose();
        await _sink.StopAsync(TimeSpan.FromSeconds(3));
        var stats = _sink.Statistics;
        if (stats.Failed != 0 || stats.Dropped != 0)
            error.WriteLine($"SeqDeliverySummary RunId={RunId} Sent={stats.Sent} Failed={stats.Failed} Dropped={stats.Dropped}");
    }
}
