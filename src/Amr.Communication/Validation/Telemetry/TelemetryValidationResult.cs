using Amr.Communication.Models;

namespace Amr.Communication.Validation;

public sealed class TelemetryValidationResult
{
    private TelemetryValidationResult(TelemetryMessage? message, ValidationError? error)
    {
        Message = message;
        Error = error;
    }

    public bool IsValid => Message is not null;
    public TelemetryMessage? Message { get; }
    public ValidationError? Error { get; }

    internal static TelemetryValidationResult Success(TelemetryMessage message) => new(message, null);
    internal static TelemetryValidationResult Failure(ValidationError error) => new(null, error);
}
