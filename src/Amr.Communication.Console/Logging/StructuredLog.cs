using Serilog;
using Serilog.Events;

namespace Amr.Communication.ConsoleApp.Logging;

/// <summary>Only explicit diagnostic properties enter Seq; never serialize a message model or exception.</summary>
internal sealed class StructuredLog(ILogger logger)
{
    internal void Event(string eventType, LogEventLevel level = LogEventLevel.Information,
        params (string Name, object? Value)[] properties)
    {
        var context = logger.ForContext("EventType", eventType);
        foreach (var (name, value) in properties)
        {
            object? bounded = value is string text && text.Length > 512 ? text[..512] : value;
            context = context.ForContext(name, bounded);
            if (value is string original && original.Length > 512)
                context = context.ForContext(name + "Truncated", true);
        }
        context.Write(level, "AMR {EventType}", eventType);
    }
}
