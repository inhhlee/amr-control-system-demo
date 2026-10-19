using System.Buffers;
using System.Text.Json;
using Amr.Communication.Models;

namespace Amr.Communication.Validation;

/// <summary>Telemetry 입력을 검사하고 정상 모델 또는 원문 없는 첫 실패를 반환한다.</summary>
public sealed class TelemetryValidator
{
    public const int MaxPayloadBytes = MessageValidation.MaxPayloadBytes;
    public const int MaxJsonDepth = MessageValidation.MaxJsonDepth;
    private static readonly string[] RequiredFields = ["id", "ts", "pos", "sp", "state"];
    private static readonly string[] PositionFields = ["x", "y"];

    /// <summary>전체 원본 바이트를 검사하고 전달받은 수신 시각을 정상 모델에 보존한다.</summary>
    /// <remarks>MQTT 구독·로깅·수신자 호출은 수행하지 않는다. 오류 뒤에도 같은 검사기를 사용할 수 있다.</remarks>
    public TelemetryValidationResult Validate(
        string? topic, ReadOnlySequence<byte> payload, DateTimeOffset receivedAtUtc) =>
        MessageValidation.Validate(topic, payload, "telemetry",
            (root, topicId) => ValidateObject(root, topicId, receivedAtUtc), TelemetryValidationResult.Failure);

    private static TelemetryValidationResult ValidateObject(JsonElement root, string topicId, DateTimeOffset receivedAtUtc)
    {
        var error = MessageValidation.CheckRequiredFields(root, RequiredFields, "$", "Only id, ts, pos, sp and state are allowed.");
        if (error is not null) return TelemetryValidationResult.Failure(error);

        error = MessageValidation.ReadId(root.GetProperty("id"), topicId, out string id);
        if (error is not null) return TelemetryValidationResult.Failure(error);

        error = MessageValidation.ReadTimestamp(root.GetProperty("ts"), out long timestamp);
        if (error is not null) return TelemetryValidationResult.Failure(error);

        JsonElement position = root.GetProperty("pos");
        if (position.ValueKind != JsonValueKind.Object)
        {
            return TelemetryValidationResult.Failure(new(ValidationErrorCode.InvalidType, "$.pos", "Position must be a JSON object."));
        }
        error = MessageValidation.CheckRequiredFields(position, PositionFields, "$.pos", "Only x and y are allowed in position.");
        if (error is not null) return TelemetryValidationResult.Failure(error);
        error = MessageValidation.ReadFiniteDouble(position.GetProperty("x"), "$.pos.x", out double x);
        if (error is not null) return TelemetryValidationResult.Failure(error);
        error = MessageValidation.ReadFiniteDouble(position.GetProperty("y"), "$.pos.y", out double y);
        if (error is not null) return TelemetryValidationResult.Failure(error);

        error = MessageValidation.ReadFiniteDouble(root.GetProperty("sp"), "$.sp", out double speed);
        if (error is not null) return TelemetryValidationResult.Failure(error);
        if (speed < 0)
        {
            return TelemetryValidationResult.Failure(new(ValidationErrorCode.InvalidValue, "$.sp", "Speed must be non-negative."));
        }

        JsonElement stateValue = root.GetProperty("state");
        if (stateValue.ValueKind != JsonValueKind.String)
        {
            return TelemetryValidationResult.Failure(new(ValidationErrorCode.InvalidType, "$.state", "State must be a string."));
        }
        string state = stateValue.GetString()!;
        if (state is not ("IDLE" or "BUSY" or "STOPPED" or "FAULT"))
        {
            return TelemetryValidationResult.Failure(new(ValidationErrorCode.InvalidValue, "$.state", "State must be IDLE, BUSY, STOPPED or FAULT."));
        }

        return TelemetryValidationResult.Success(new TelemetryMessage(
            id, timestamp, new TelemetryPosition(x, y), speed, state, receivedAtUtc));
    }
}
