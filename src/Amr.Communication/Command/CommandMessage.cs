using System.Text.Json;
using System.Text.Json.Serialization;

namespace Amr.Communication.Command;

public sealed record CommandParameters(
    [property: JsonPropertyName("x"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? X = null,
    [property: JsonPropertyName("y"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Y = null);

/// <summary>요청 측이 생성한 ID와 발생 시각을 보존한다. 직렬화와 입력 검사는 별도 단계다.</summary>
public sealed record CommandMessage(
    [property: JsonPropertyName("cmdId")] string CommandId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("ts")] long Timestamp,
    [property: JsonPropertyName("param")] CommandParameters Parameters)
{
    public byte[] ToPayload() => JsonSerializer.SerializeToUtf8Bytes(this);
}
