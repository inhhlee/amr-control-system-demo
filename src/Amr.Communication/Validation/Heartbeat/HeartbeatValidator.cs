using System.Buffers;
using System.Text.Json;
using Amr.Communication.Models;

namespace Amr.Communication.Validation;

// 공개 API와 Heartbeat 판정을 유지하고 공통 입력 검사를 MessageValidation에 맡긴다.
public sealed class HeartbeatValidator
{
    public const int MaxPayloadBytes = MessageValidation.MaxPayloadBytes;
    public const int MaxJsonDepth = MessageValidation.MaxJsonDepth;
    private static readonly string[] RequiredFields = ["id", "ts", "status"];

    public HeartbeatValidationResult Validate(
        string? topic, ReadOnlySequence<byte> payload, DateTimeOffset receivedAtUtc) =>
        MessageValidation.Validate(topic, payload, "heartbeat",
            (root, topicId) => ValidateObject(root, topicId, receivedAtUtc), Fail);

    private static HeartbeatValidationResult ValidateObject(JsonElement root, string topicId, DateTimeOffset receivedAtUtc)
    {
        var error = MessageValidation.CheckRequiredFields(root, RequiredFields, "$", "Only id, ts and status are allowed.");
        if (error is not null) return Fail(error);

        error = MessageValidation.ReadId(root.GetProperty("id"), topicId, out string id);
        if (error is not null) return Fail(error);

        error = MessageValidation.ReadTimestamp(root.GetProperty("ts"), out long timestamp);
        if (error is not null) return Fail(error);

        JsonElement statusValue = root.GetProperty("status");
        if (statusValue.ValueKind != JsonValueKind.String)
        {
            return Fail(new(ValidationErrorCode.InvalidType, "$.status", "Status must be a string."));
        }
        string status = statusValue.GetString()!;
        if (status != "OK")
        {
            return Fail(new(ValidationErrorCode.InvalidValue, "$.status", "Status must be OK."));
        }

        return HeartbeatValidationResult.Success(new HeartbeatMessage(id, timestamp, status, receivedAtUtc));
    }

    private static HeartbeatValidationResult Fail(ValidationError error) =>
        HeartbeatValidationResult.Failure(error.Code, error.Field, error.Reason);
}
