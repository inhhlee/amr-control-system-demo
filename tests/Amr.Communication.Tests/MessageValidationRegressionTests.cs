using System.Buffers;
using System.Text;
using Amr.Communication.Validation;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class MessageValidationRegressionTests
{
    private const string Heartbeat = """{"id":"AMR_001","ts":1,"status":"OK"}""";
    private const string Telemetry = """{"id":"AMR_001","ts":1,"pos":{"x":1,"y":2},"sp":0,"state":"IDLE"}""";
    private const string Alarm = """{"id":"AMR_001","ts":1,"code":"A01","level":"WARN","alarmState":"ACTIVE"}""";
    private const string Ack = """{"id":"AMR_001","ts":1,"cmdId":"CMD_001","result":"FAILURE"}""";
    private static readonly DateTimeOffset ReceivedAt = new(2026, 10, 19, 5, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("missing", ValidationErrorCode.MissingField, "$.ts", "Required field is missing.")]
    [InlineData("duplicate", ValidationErrorCode.DuplicateField, "$.ts", "Duplicate field is not allowed.")]
    [InlineData("id-type", ValidationErrorCode.InvalidType, "$.id", "ID must be a string.")]
    [InlineData("id-empty", ValidationErrorCode.InvalidValue, "$.id", "ID must not be empty or whitespace.")]
    [InlineData("id-mismatch", ValidationErrorCode.IdMismatch, "$.id", "Payload ID must exactly match Topic ID.")]
    [InlineData("ts-format", ValidationErrorCode.InvalidType, "$.ts", "Timestamp must use JSON integer notation.")]
    [InlineData("ts-overflow", ValidationErrorCode.OutOfRange, "$.ts", "Timestamp must fit signed Int64.")]
    [InlineData("root", ValidationErrorCode.InvalidType, "$", "Payload must be a JSON object.")]
    [InlineData("json", ValidationErrorCode.InvalidJson, "$", "Invalid JSON or nesting exceeds depth 16.")]
    [InlineData("unicode", ValidationErrorCode.InvalidJson, "$", "JSON strings must contain valid Unicode.")]
    [InlineData("utf8", ValidationErrorCode.InvalidUtf8, "$", "Payload must be strict UTF-8.")]
    [InlineData("size", ValidationErrorCode.PayloadTooLarge, "$", "Payload exceeds 65536 bytes.")]
    [InlineData("topic-id", ValidationErrorCode.InvalidTopic, "$topic.amrId", "Topic ID is empty or contains a forbidden character.")]
    public void SharedErrors_PreserveHeartbeatCodeFieldAndReason(
        string scenario, ValidationErrorCode code, string field, string reason)
    {
        byte[] Input(string valid)
        {
            string json = scenario switch
            {
                "missing" => valid.Replace("\"ts\":1,", ""),
                "duplicate" => valid[..^1] + ",\"ts\":2}",
                "id-type" => valid.Replace("\"id\":\"AMR_001\"", "\"id\":null"),
                "id-empty" => valid.Replace("AMR_001", " "),
                "id-mismatch" => valid.Replace("AMR_001", "AMR_002"),
                "ts-format" => valid.Replace("\"ts\":1", "\"ts\":1e0"),
                "ts-overflow" => valid.Replace("\"ts\":1", "\"ts\":9223372036854775808"),
                "root" => "[]",
                "json" => "{",
                "unicode" => valid.Replace("AMR_001", "\\uD800"),
                "size" => valid.PadRight(65_537),
                _ => valid
            };
            return scenario == "utf8" ? [0xff] : Encoding.UTF8.GetBytes(json);
        }
        string id = scenario == "topic-id" ? "AMR 001" : "AMR_001";
        var heartbeat = new HeartbeatValidator().Validate($"amr/{id}/heartbeat", new(Input(Heartbeat)), ReceivedAt);
        var telemetry = new TelemetryValidator().Validate($"amr/{id}/telemetry", new(Input(Telemetry)), ReceivedAt);
        var alarm = new AlarmValidator().Validate($"amr/{id}/alarm", new(Input(Alarm)), ReceivedAt);
        var ack = new AckValidator().Validate($"amr/{id}/ack", new(Input(Ack)), ReceivedAt);
        var expected = new ValidationError(code, field, reason);
        Assert.Null(heartbeat.Message);
        Assert.Null(telemetry.Message);
        Assert.Null(alarm.Message);
        Assert.Null(ack.Message);
        Assert.Equal(expected, heartbeat.Error);
        Assert.Equal(expected, telemetry.Error);
        Assert.Equal(expected, alarm.Error);
        Assert.Equal(expected, ack.Error);
    }

    [Fact]
    public void MultipleErrors_PreserveFirstFailureAndRejectBeforeDecoding()
    {
        var heartbeat = new HeartbeatValidator();
        var telemetry = new TelemetryValidator();
        var alarm = new AlarmValidator();
        var ack = new AckValidator();
        var oversized = new ReadOnlySequence<byte>(new byte[65_537]);
        Assert.Equal(ValidationErrorCode.PayloadTooLarge, heartbeat.Validate(null, oversized, ReceivedAt).Error!.Code);
        Assert.Equal(ValidationErrorCode.PayloadTooLarge, telemetry.Validate(null, oversized, ReceivedAt).Error!.Code);
        Assert.Equal(ValidationErrorCode.PayloadTooLarge, alarm.Validate(null, oversized, ReceivedAt).Error!.Code);
        Assert.Equal(ValidationErrorCode.PayloadTooLarge, ack.Validate(null, oversized, ReceivedAt).Error!.Code);
        var invalidUtf8 = new ReadOnlySequence<byte>(new byte[] { 0xff });
        Assert.Equal(ValidationErrorCode.InvalidTopic, heartbeat.Validate(null, invalidUtf8, ReceivedAt).Error!.Code);
        Assert.Equal(ValidationErrorCode.InvalidTopic, telemetry.Validate(null, invalidUtf8, ReceivedAt).Error!.Code);
        Assert.Equal(ValidationErrorCode.InvalidTopic, alarm.Validate(null, invalidUtf8, ReceivedAt).Error!.Code);
        Assert.Equal(ValidationErrorCode.InvalidTopic, ack.Validate(null, invalidUtf8, ReceivedAt).Error!.Code);
        // 추가 필드는 필수 누락보다 먼저 검사하고, 오류 이유에 원문을 포함하지 않는다.
        const string extra = """{"secret":1}""";
        var hb = heartbeat.Validate("amr/AMR_001/heartbeat", new(Encoding.UTF8.GetBytes(extra)), ReceivedAt);
        Assert.Equal(new ValidationError(ValidationErrorCode.UnexpectedField, "$", "Only id, ts and status are allowed."), hb.Error);
        var tm = telemetry.Validate("amr/AMR_001/telemetry", new(Encoding.UTF8.GetBytes(extra)), ReceivedAt);
        Assert.Equal(ValidationErrorCode.UnexpectedField, tm.Error!.Code);
        Assert.DoesNotContain("secret", tm.Error.Reason);
        var al = alarm.Validate("amr/AMR_001/alarm", new(Encoding.UTF8.GetBytes(extra)), ReceivedAt);
        Assert.Equal(new ValidationError(ValidationErrorCode.UnexpectedField, "$",
            "Only id, ts, code, level, alarmState and message are allowed."), al.Error);
        var ak = ack.Validate("amr/AMR_001/ack", new(Encoding.UTF8.GetBytes(extra)), ReceivedAt);
        Assert.Equal(new ValidationError(ValidationErrorCode.UnexpectedField, "$",
            "Only cmdId, result, ts, id and reason are allowed."), ak.Error);
    }
}
