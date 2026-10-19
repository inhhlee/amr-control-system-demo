using Amr.Communication.Models;

namespace Amr.Communication.Validation;

public sealed class HeartbeatValidationResult
{
    private HeartbeatValidationResult(HeartbeatMessage? message, ValidationError? error)
    {
        Message = message;
        Error = error;
    }

    public bool IsValid => Message is not null;
    public HeartbeatMessage? Message { get; }
    public ValidationError? Error { get; }

    internal static HeartbeatValidationResult Success(HeartbeatMessage message) => new(message, null);
    internal static HeartbeatValidationResult Failure(ValidationErrorCode code, string field, string reason) =>
        new(null, new ValidationError(code, field, reason));
}
