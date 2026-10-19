using System.Buffers;
using System.Text.Json;
using Amr.Communication.Models;

namespace Amr.Communication.Validation;

/// <summary>Alarm 입력을 검사하고 정상 모델 또는 원문 없는 첫 실패를 반환한다.</summary>
public sealed class AlarmValidator
{
    public const int MaxPayloadBytes = MessageValidation.MaxPayloadBytes;
    public const int MaxJsonDepth = MessageValidation.MaxJsonDepth;
    private static readonly string[] AllowedFields = ["id", "ts", "code", "level", "alarmState", "message"];
    private static readonly string[] RequiredFields = ["id", "ts", "code", "level", "alarmState"];

    /// <summary>전체 원본 바이트를 검사하고 문자열·Int64 시각·수신 시각을 정상 모델에 보존한다.</summary>
    /// <remarks>MQTT 구독·로깅·큐 적재·AMR 상태 변경은 수행하지 않는다. 오류 뒤에도 같은 검사기를 사용할 수 있다.</remarks>
    public AlarmValidationResult Validate(
        string? topic, ReadOnlySequence<byte> payload, DateTimeOffset receivedAtUtc) =>
        MessageValidation.Validate(topic, payload, "alarm",
            (root, topicId) => ValidateObject(root, topicId, receivedAtUtc), AlarmValidationResult.Failure);

    private static AlarmValidationResult ValidateObject(JsonElement root, string topicId, DateTimeOffset receivedAtUtc)
    {
        var error = MessageValidation.CheckFields(root, AllowedFields, RequiredFields, "$",
            "Only id, ts, code, level, alarmState and message are allowed.");
        if (error is not null) return AlarmValidationResult.Failure(error);

        error = MessageValidation.ReadId(root.GetProperty("id"), topicId, out string id);
        if (error is not null) return AlarmValidationResult.Failure(error);

        error = MessageValidation.ReadTimestamp(root.GetProperty("ts"), out long timestamp);
        if (error is not null) return AlarmValidationResult.Failure(error);

        error = MessageValidation.ReadNonBlankString(root.GetProperty("code"), "$.code", out string code);
        if (error is not null) return AlarmValidationResult.Failure(error);

        JsonElement levelValue = root.GetProperty("level");
        if (levelValue.ValueKind != JsonValueKind.String)
        {
            return AlarmValidationResult.Failure(new(ValidationErrorCode.InvalidType, "$.level", "Level must be a string."));
        }
        string level = levelValue.GetString()!;
        if (level is not ("INFO" or "WARN" or "ERR" or "FATAL"))
        {
            return AlarmValidationResult.Failure(new(ValidationErrorCode.InvalidValue, "$.level", "Level must be INFO, WARN, ERR or FATAL."));
        }

        JsonElement alarmStateValue = root.GetProperty("alarmState");
        if (alarmStateValue.ValueKind != JsonValueKind.String)
        {
            return AlarmValidationResult.Failure(new(ValidationErrorCode.InvalidType, "$.alarmState", "Alarm state must be a string."));
        }
        string alarmState = alarmStateValue.GetString()!;
        if (alarmState is not ("ACTIVE" or "CLEARED"))
        {
            return AlarmValidationResult.Failure(new(ValidationErrorCode.InvalidValue, "$.alarmState", "Alarm state must be ACTIVE or CLEARED."));
        }

        // 생략과 JSON null을 구분한다. 값이 있으면 검사하고 Trim 없이 그대로 보존한다.
        string? message = null;
        if (root.TryGetProperty("message", out JsonElement messageValue))
        {
            error = MessageValidation.ReadNonBlankString(messageValue, "$.message", out string text);
            if (error is not null) return AlarmValidationResult.Failure(error);
            message = text;
        }

        return AlarmValidationResult.Success(new AlarmMessage(
            id, timestamp, code, level, alarmState, message, receivedAtUtc));
    }
}
