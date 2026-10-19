using System.Buffers;
using System.Text;
using System.Text.Json;
using Amr.Communication.Models;

namespace Amr.Communication.Validation;

// COM04-A 전용 검사 경계. 로깅·수신자 호출·MQTT 수명주기는 이 클래스 밖에서 처리한다.
public sealed class HeartbeatValidator
{
    public const int MaxPayloadBytes = 65_536;
    public const int MaxJsonDepth = 16;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public HeartbeatValidationResult Validate(
        string? topic, ReadOnlySequence<byte> payload, DateTimeOffset receivedAtUtc)
    {
        // 크기를 확인하기 전에는 전체 Payload를 복사하거나 디코딩하지 않는다.
        if (payload.Length > MaxPayloadBytes)
        {
            return Fail(ValidationErrorCode.PayloadTooLarge, "$", "Payload exceeds 65536 bytes.");
        }

        string[] parts = topic?.Split('/') ?? [];
        if (parts.Length != 3 || parts[0] != "amr" || parts[2] != "heartbeat")
        {
            return Fail(ValidationErrorCode.InvalidTopic, "$topic", "Expected amr/{amrId}/heartbeat.");
        }
        string topicId = parts[1];
        if (topicId.Length == 0 || topicId.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '+' or '#'))
        {
            return Fail(ValidationErrorCode.InvalidTopic, "$topic.amrId", "Topic ID is empty or contains a forbidden character.");
        }

        string json;
        try
        {
            json = StrictUtf8.GetString(payload.ToArray());
        }
        catch (DecoderFallbackException)
        {
            return Fail(ValidationErrorCode.InvalidUtf8, "$", "Payload must be strict UTF-8.");
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = MaxJsonDepth,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            return ValidateObject(document.RootElement, topicId, receivedAtUtc);
        }
        catch (JsonException)
        {
            // 파서의 Message/Path에는 입력 일부가 포함될 수 있으므로 기록하지 않는다.
            return Fail(ValidationErrorCode.InvalidJson, "$", "Invalid JSON or nesting exceeds depth 16.");
        }
        catch (InvalidOperationException)
        {
            // JsonElement의 이름·문자열 디코딩은 짝이 없는 Unicode escape를 거부한다.
            // 입력 해석만 이 경계에 포함된다. 수신자 오류를 검사 실패로 바꾸지 않는다.
            return Fail(ValidationErrorCode.InvalidJson, "$", "JSON strings must contain valid Unicode.");
        }
    }

    private static HeartbeatValidationResult ValidateObject(JsonElement root, string topicId, DateTimeOffset receivedAtUtc)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Fail(ValidationErrorCode.InvalidType, "$", "Payload must be a JSON object.");
        }

        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            string name = property.Name;
            if (name is not ("id" or "ts" or "status"))
            {
                return Fail(ValidationErrorCode.UnexpectedField, "$", "Only id, ts and status are allowed.");
            }
            if (!fields.Add(name))
            {
                return Fail(ValidationErrorCode.DuplicateField, "$." + name, "Duplicate field is not allowed.");
            }
        }

        foreach (string name in new[] { "id", "ts", "status" })
        {
            if (!fields.Contains(name))
            {
                return Fail(ValidationErrorCode.MissingField, "$." + name, "Required field is missing.");
            }
        }

        JsonElement idValue = root.GetProperty("id");
        if (idValue.ValueKind != JsonValueKind.String)
        {
            return Fail(ValidationErrorCode.InvalidType, "$.id", "ID must be a string.");
        }
        string id = idValue.GetString()!;
        if (string.IsNullOrWhiteSpace(id))
        {
            return Fail(ValidationErrorCode.InvalidValue, "$.id", "ID must not be empty or whitespace.");
        }
        if (!string.Equals(id, topicId, StringComparison.Ordinal))
        {
            return Fail(ValidationErrorCode.IdMismatch, "$.id", "Payload ID must exactly match Topic ID.");
        }

        JsonElement tsValue = root.GetProperty("ts");
        if (tsValue.ValueKind != JsonValueKind.Number || tsValue.GetRawText().IndexOfAny(['.', 'e', 'E']) >= 0)
        {
            return Fail(ValidationErrorCode.InvalidType, "$.ts", "Timestamp must use JSON integer notation.");
        }
        if (!tsValue.TryGetInt64(out long timestamp))
        {
            return Fail(ValidationErrorCode.OutOfRange, "$.ts", "Timestamp must fit signed Int64.");
        }

        JsonElement statusValue = root.GetProperty("status");
        if (statusValue.ValueKind != JsonValueKind.String)
        {
            return Fail(ValidationErrorCode.InvalidType, "$.status", "Status must be a string.");
        }
        string status = statusValue.GetString()!;
        if (status != "OK")
        {
            return Fail(ValidationErrorCode.InvalidValue, "$.status", "Status must be OK.");
        }

        return HeartbeatValidationResult.Success(new HeartbeatMessage(id, timestamp, status, receivedAtUtc));
    }

    private static HeartbeatValidationResult Fail(ValidationErrorCode code, string field, string reason) =>
        HeartbeatValidationResult.Failure(code, field, reason);
}
