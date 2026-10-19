
using Amr.Communication.Models;

namespace Amr.Communication.Validation;

public sealed class AlarmValidationResult
{
    private AlarmValidationResult(AlarmMessage? message, ValidationError? error)
    {
        Message = message;
        Error = error;
    }

    public bool IsValid => Message is not null;
    public AlarmMessage? Message { get; }
    public ValidationError? Error { get; }

    internal static AlarmValidationResult Success(AlarmMessage message) => new(message, null);
    internal static AlarmValidationResult Failure(ValidationError error) => new(null, error);
}

