using Amr.Communication.Models;

namespace Amr.Communication.Validation;

public sealed class AckValidationResult
{
    private AckValidationResult(AckMessage? message, ValidationError? error)
    {
        Message = message;
        Error = error;
    }

    // 수신 계약의 유효성이다. 명령의 SUCCESS/FAILURE 판단과 구분한다.
    public bool IsValid => Message is not null;
    public AckMessage? Message { get; }
    public ValidationError? Error { get; }

    internal static AckValidationResult Success(AckMessage message) => new(message, null);
    internal static AckValidationResult Failure(ValidationError error) => new(null, error);
}
