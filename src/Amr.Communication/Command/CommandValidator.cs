using System.Buffers;
using System.Text;
using System.Text.Json;
using Amr.Communication.Validation;

namespace Amr.Communication.Command;

/// <summary>DN-08 Command v1. 발행 전과 시험 수신 측에서 같은 순수 검사를 사용할 수 있다.</summary>
public sealed class CommandValidator
{
    public const int MaxPayloadBytes = MessageValidation.MaxPayloadBytes;
    public const int MaxJsonDepth = MessageValidation.MaxJsonDepth;
    private static readonly string[] Fields = ["cmdId", "type", "ts", "param"];
    private static readonly string[] Coordinates = ["x", "y"];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public CommandValidationResult Validate(string? topic, ReadOnlySequence<byte> payload)
    {
        // MQTT Topic Name은 UTF-8의 2바이트 길이 필드를 사용한다.
        try
        {
            if (topic is not null && StrictUtf8.GetByteCount(topic) > ushort.MaxValue)
                return CommandValidationResult.Failure(new(ValidationErrorCode.InvalidTopic, "$topic", "Topic exceeds MQTT UTF-8 length limit."));
        }
        catch (EncoderFallbackException)
        {
            return CommandValidationResult.Failure(new(ValidationErrorCode.InvalidTopic, "$topic", "Topic must contain valid Unicode."));
        }
        return MessageValidation.Validate(topic, payload, "command", ValidateObject, CommandValidationResult.Failure);
    }

    private static CommandValidationResult ValidateObject(JsonElement root, string topicId)
    {
        var error = MessageValidation.CheckRequiredFields(root, Fields, "$", "Only cmdId, type, ts and param are allowed.");
        if (error is not null) return CommandValidationResult.Failure(error);
        error = MessageValidation.ReadNonBlankString(root.GetProperty("cmdId"), "$.cmdId", out string id);
        if (error is not null) return CommandValidationResult.Failure(error);
        error = MessageValidation.ReadTimestamp(root.GetProperty("ts"), out long timestamp);
        if (error is not null) return CommandValidationResult.Failure(error);
        error = MessageValidation.ReadNonBlankString(root.GetProperty("type"), "$.type", out string type);
        if (error is not null) return CommandValidationResult.Failure(error);
        if (type is not ("MOVE" or "STOP" or "RESET"))
            return CommandValidationResult.Failure(new(ValidationErrorCode.InvalidValue, "$.type", "Type must be MOVE, STOP or RESET."));
        var param = root.GetProperty("param");
        if (param.ValueKind != JsonValueKind.Object)
            return CommandValidationResult.Failure(new(ValidationErrorCode.InvalidType, "$.param", "Parameters must be an object."));
        error = MessageValidation.CheckRequiredFields(param, type == "MOVE" ? Coordinates : [], "$.param",
            type == "MOVE" ? "MOVE allows only x and y." : "STOP and RESET require an empty object.");
        if (error is not null) return CommandValidationResult.Failure(error);
        var parameters = new CommandParameters();
        if (type == "MOVE")
        {
            error = MessageValidation.ReadFiniteDouble(param.GetProperty("x"), "$.param.x", out double x);
            if (error is not null) return CommandValidationResult.Failure(error);
            error = MessageValidation.ReadFiniteDouble(param.GetProperty("y"), "$.param.y", out double y);
            if (error is not null) return CommandValidationResult.Failure(error);
            parameters = new(x, y);
        }
        return CommandValidationResult.Success(new(id, type, timestamp, parameters));
    }
}
