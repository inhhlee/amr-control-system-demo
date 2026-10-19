using System.Text.Json;
using Amr.Communication.Validation;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class MessageFieldValidationTests
{
    private const string RequiredOnly = """{"id":"AMR_001","ts":1,"code":"A01","level":"WARN","alarmState":"ACTIVE"}""";
    private const string UnexpectedReason = "Only id, ts, code, level, alarmState and message are allowed.";
    private static readonly string[] AllowedFields = ["id", "ts", "code", "level", "alarmState", "message"];
    private static readonly string[] RequiredFields = ["id", "ts", "code", "level", "alarmState"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalMessage_MayBeOmittedOrIncluded(bool includeMessage)
    {
        string json = includeMessage ? RequiredOnly[..^1] + ",\"message\":\" 경고 발생 \"}" : RequiredOnly;
        Assert.Null(Check(json));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("ts")]
    [InlineData("code")]
    [InlineData("level")]
    [InlineData("alarmState")]
    public void EachRequiredField_IsStillRequired(string missingField)
    {
        using var document = JsonDocument.Parse(RequiredOnly);
        var values = document.RootElement.EnumerateObject()
            .Where(property => property.Name != missingField)
            .ToDictionary(property => property.Name, property => property.Value);

        Assert.Equal(new ValidationError(ValidationErrorCode.MissingField,
            "$." + missingField, "Required field is missing."), Check(JsonSerializer.Serialize(values)));
    }

    [Theory]
    [InlineData("""{"message":"first","message":"second"}""", ValidationErrorCode.DuplicateField, "$.message")]
    [InlineData("""{"message":"first","\u006dessage":"second"}""", ValidationErrorCode.DuplicateField, "$.message")]
    [InlineData("""{"id":"AMR_001","id":"AMR_002"}""", ValidationErrorCode.DuplicateField, "$.id")]
    [InlineData("""{"secret":"do-not-log"}""", ValidationErrorCode.UnexpectedField, "$")]
    [InlineData("""{"Message":"wrong case"}""", ValidationErrorCode.UnexpectedField, "$")]
    public void ExtraAndDuplicateFields_AreRejectedBeforeMissingFields(
        string json, ValidationErrorCode code, string field)
    {
        string reason = code == ValidationErrorCode.DuplicateField
            ? "Duplicate field is not allowed." : UnexpectedReason;
        Assert.Equal(new ValidationError(code, field, reason), Check(json));
    }

    private static ValidationError? Check(string json)
    {
        using var document = JsonDocument.Parse(json);
        return MessageValidation.CheckFields(document.RootElement, AllowedFields, RequiredFields, "$", UnexpectedReason);
    }
}
