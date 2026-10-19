using System.Buffers;
using System.Text.Json;
using Amr.Communication.Models;

namespace Amr.Communication.Validation;

/// <summary>ACK 입력을 검사하고 정상 모델 또는 원문 없는 첫 실패를 반환한다.</summary>
public sealed class AckValidator
{
    public const int MaxPayloadBytes = MessageValidation.MaxPayloadBytes;
    public const int MaxJsonDepth = MessageValidation.MaxJsonDepth;
    private static readonly string[] AllowedFields = ["cmdId", "result", "ts", "id", "reason"];
    private static readonly string[] RequiredFields = ["cmdId", "result", "ts"];

    /// <summary>전체 원본 바이트를 검사하고 선택 필드의 생략 여부와 수신 값을 보존한다.</summary>
    /// <remarks>유효한 FAILURE도 정상 모델로 반환한다. 명령 대조·재시도·Core 업무 처리·MQTT 구독은 수행하지 않는다.</remarks>
    public AckValidationResult Validate(
        string? topic, ReadOnlySequence<byte> payload, DateTimeOffset receivedAtUtc) =>
        MessageValidation.Validate(topic, payload, "ack",
            (root, topicId) => ValidateObject(root, topicId, receivedAtUtc), AckValidationResult.Failure);

    private static AckValidationResult ValidateObject(JsonElement root, string topicId, DateTimeOffset receivedAtUtc)
    {
        var error = MessageValidation.CheckFields(root, AllowedFields, RequiredFields, "$",
            "Only cmdId, result, ts, id and reason are allowed.");
        if (error is not null) return AckValidationResult.Failure(error);

        // Topic 자체의 ID는 공통 경계에서 항상 검사한다. Payload ID는 존재할 때만 대조한다.
        string? id = null;
        if (root.TryGetProperty("id", out JsonElement idValue))
        {
            error = MessageValidation.ReadId(idValue, topicId, out string value);
            if (error is not null) return AckValidationResult.Failure(error);
            id = value;
        }

        error = MessageValidation.ReadTimestamp(root.GetProperty("ts"), out long timestamp);
        if (error is not null) return AckValidationResult.Failure(error);

        error = MessageValidation.ReadNonBlankString(root.GetProperty("cmdId"), "$.cmdId", out string commandId);
        if (error is not null) return AckValidationResult.Failure(error);

        JsonElement resultValue = root.GetProperty("result");
        if (resultValue.ValueKind != JsonValueKind.String)
        {
            return AckValidationResult.Failure(new(ValidationErrorCode.InvalidType, "$.result", "Result must be a string."));
        }
        string result = resultValue.GetString()!;
        if (result is not ("SUCCESS" or "FAILURE"))
        {
            return AckValidationResult.Failure(new(ValidationErrorCode.InvalidValue, "$.result", "Result must be SUCCESS or FAILURE."));
        }

        string? reason = null;
        if (root.TryGetProperty("reason", out JsonElement reasonValue))
        {
            error = MessageValidation.ReadNonBlankString(reasonValue, "$.reason", out string value);
            if (error is not null) return AckValidationResult.Failure(error);
            reason = value;
        }

        // Success는 검사 성공을 뜻한다. result == FAILURE도 변경 없이 정상 데이터로 전달한다.
        return AckValidationResult.Success(new AckMessage(commandId, result, timestamp, id, reason, receivedAtUtc));
    }
}
