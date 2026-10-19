using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Amr.Communication.Validation;

// 수신 유형이 공유하는 입력 경계다. 모델 작성 외의 콜백·로깅·MQTT 처리는 포함하지 않는다.
internal static class MessageValidation
{
    internal const int MaxPayloadBytes = 65_536;
    internal const int MaxJsonDepth = 16;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static TResult Validate<TResult>(
        string? topic, ReadOnlySequence<byte> payload, string kind,
        Func<JsonElement, string, TResult> validateObject, Func<ValidationError, TResult> failure)
    {
        // 크기를 확인하기 전에는 전체 Payload를 복사하거나 디코딩하지 않는다.
        if (payload.Length > MaxPayloadBytes)
        {
            return failure(new(ValidationErrorCode.PayloadTooLarge, "$", "Payload exceeds 65536 bytes."));
        }

        string[] parts = topic?.Split('/') ?? [];
        if (parts.Length != 3 || parts[0] != "amr" || parts[2] != kind)
        {
            return failure(new(ValidationErrorCode.InvalidTopic, "$topic", $"Expected amr/{{amrId}}/{kind}."));
        }
        string topicId = parts[1];
        if (topicId.Length == 0 || topicId.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '+' or '#'))
        {
            return failure(new(ValidationErrorCode.InvalidTopic, "$topic.amrId", "Topic ID is empty or contains a forbidden character."));
        }

        string json;
        try
        {
            json = StrictUtf8.GetString(payload.ToArray());
        }
        catch (DecoderFallbackException)
        {
            return failure(new(ValidationErrorCode.InvalidUtf8, "$", "Payload must be strict UTF-8."));
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = MaxJsonDepth,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return failure(new(ValidationErrorCode.InvalidType, "$", "Payload must be a JSON object."));
            }
            return validateObject(document.RootElement, topicId);
        }
        catch (JsonException)
        {
            // 파서의 Message/Path에는 입력 일부가 포함될 수 있으므로 기록하지 않는다.
            return failure(new(ValidationErrorCode.InvalidJson, "$", "Invalid JSON or nesting exceeds depth 16."));
        }
        catch (InvalidOperationException)
        {
            // JsonElement의 이름·문자열 디코딩은 짝이 없는 Unicode escape를 거부한다.
            // 이 경계에는 입력 해석과 모델 작성만 포함된다. 수신자 오류는 여기서 처리하지 않는다.
            return failure(new(ValidationErrorCode.InvalidJson, "$", "JSON strings must contain valid Unicode."));
        }
    }

    // 모든 허용 필드가 필수인 Heartbeat·Telemetry는 기존 호출 형태를 유지한다.
    internal static ValidationError? CheckRequiredFields(
        JsonElement value, string[] requiredFields, string path, string unexpectedReason) =>
        CheckFields(value, requiredFields, requiredFields, path, unexpectedReason);

    // JSON object의 필드 구성만 검사한다. 선택 필드도 존재하면 자료형·값을 별도로 검사해야 한다.
    // allowedFields는 선택 필드까지 포함하며 requiredFields는 그 부분집합이다.
    // 오류 순서는 기존과 같이 추가/중복 → 필수 누락이다.
    internal static ValidationError? CheckFields(
        JsonElement value, string[] allowedFields, string[] requiredFields,
        string path, string unexpectedReason)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            string name = property.Name;
            if (Array.IndexOf(allowedFields, name) < 0)
            {
                return new(ValidationErrorCode.UnexpectedField, path, unexpectedReason);
            }
            if (!fields.Add(name))
            {
                return new(ValidationErrorCode.DuplicateField, path + "." + name, "Duplicate field is not allowed.");
            }
        }
        foreach (string name in requiredFields)
        {
            if (!fields.Contains(name))
            {
                return new(ValidationErrorCode.MissingField, path + "." + name, "Required field is missing.");
            }
        }
        return null;
    }

    internal static ValidationError? ReadId(JsonElement value, string topicId, out string id)
    {
        id = string.Empty;
        if (value.ValueKind != JsonValueKind.String)
        {
            return new(ValidationErrorCode.InvalidType, "$.id", "ID must be a string.");
        }
        id = value.GetString()!;
        if (string.IsNullOrWhiteSpace(id))
        {
            return new(ValidationErrorCode.InvalidValue, "$.id", "ID must not be empty or whitespace.");
        }
        if (!string.Equals(id, topicId, StringComparison.Ordinal))
        {
            return new(ValidationErrorCode.IdMismatch, "$.id", "Payload ID must exactly match Topic ID.");
        }
        return null;
    }

    internal static ValidationError? ReadTimestamp(JsonElement value, out long timestamp)
    {
        timestamp = default;
        if (value.ValueKind != JsonValueKind.Number || value.GetRawText().IndexOfAny(['.', 'e', 'E']) >= 0)
        {
            return new(ValidationErrorCode.InvalidType, "$.ts", "Timestamp must use JSON integer notation.");
        }
        if (!value.TryGetInt64(out timestamp))
        {
            return new(ValidationErrorCode.OutOfRange, "$.ts", "Timestamp must fit signed Int64.");
        }
        return null;
    }

    // code·message처럼 문자열의 내용은 보존하면서 빈 값만 차단할 때 사용한다.
    // 선택 필드의 생략 여부는 호출자가 TryGetProperty로 구분한다.
    internal static ValidationError? ReadNonBlankString(JsonElement value, string field, out string text)
    {
        text = string.Empty;
        if (value.ValueKind != JsonValueKind.String)
        {
            return new(ValidationErrorCode.InvalidType, field, "Value must be a string.");
        }
        text = value.GetString()!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new(ValidationErrorCode.InvalidValue, field, "Value must not be empty or whitespace.");
        }
        return null;
    }

    internal static ValidationError? ReadFiniteDouble(JsonElement value, string field, out double number)
    {
        number = default;
        if (value.ValueKind != JsonValueKind.Number)
        {
            return new(ValidationErrorCode.InvalidType, field, "Value must be a JSON number.");
        }
        if (!value.TryGetDouble(out number) || !double.IsFinite(number))
        {
            return new(ValidationErrorCode.OutOfRange, field, "Value must be a finite double.");
        }
        return null;
    }
}
