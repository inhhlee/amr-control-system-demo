using Amr.Communication.Validation;

namespace Amr.Communication.Command;

public sealed class CommandValidationResult
{
    private CommandValidationResult(CommandMessage? message, ValidationError? error)
        => (Message, Error) = (message, error);

    public bool IsValid => Message is not null;
    public CommandMessage? Message { get; }
    public ValidationError? Error { get; }
    internal static CommandValidationResult Success(CommandMessage message) => new(message, null);
    internal static CommandValidationResult Failure(ValidationError error) => new(null, error);
}

public sealed class CommandValidationException(ValidationError error)
    : ArgumentException($"{error.Code}: {error.Field}: {error.Reason}")
{
    public ValidationError Error { get; } = error;
}
