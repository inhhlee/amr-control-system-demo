namespace Amr.Communication.Validation;

public enum ValidationErrorCode
{
    PayloadTooLarge,
    InvalidTopic,
    InvalidUtf8,
    InvalidJson,
    InvalidType,
    MissingField,
    UnexpectedField,
    DuplicateField,
    InvalidValue,
    IdMismatch,
    OutOfRange
}

// Field와 Reason에는 검사기가 정한 문자열만 넣고 입력값·원문은 넣지 않는다.
public sealed record ValidationError(ValidationErrorCode Code, string Field, string Reason);
