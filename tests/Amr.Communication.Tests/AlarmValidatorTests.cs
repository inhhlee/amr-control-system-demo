using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Amr.Communication.Models;
using Amr.Communication.Validation;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class AlarmValidatorTests
{
    private const string Topic = "amr/AMR_001/alarm";
    private const string ValidJson = """{"id":"AMR_001","ts":1791195767000,"code":"A01","level":"WARN","alarmState":"ACTIVE"}""";
    private static readonly DateTimeOffset ReceivedAt = new(2026, 10, 19, 6, 0, 0, TimeSpan.Zero);
    private readonly AlarmValidator _validator = new();

    public static IEnumerable<object[]> AllowedCombinations()
    {
        foreach (string level in new[] { "INFO", "WARN", "ERR", "FATAL" })
        foreach (string state in new[] { "ACTIVE", "CLEARED" })
        foreach (bool includeMessage in new[] { false, true })
            yield return [level, state, includeMessage];
    }

    [Theory]
    [MemberData(nameof(AllowedCombinations))]
    public void ValidAlarm_PreservesEveryFieldWithOrWithoutMessage(string level, string state, bool includeMessage)
    {
        const string code = " A-01 ";
        const string text = " 진동 경보\n확인 \t";
        var fields = ValidFields();
        fields["code"] = JsonSerializer.Serialize(code);
        fields["level"] = JsonSerializer.Serialize(level);
        fields["alarmState"] = JsonSerializer.Serialize(state);
        if (includeMessage) fields["message"] = JsonSerializer.Serialize(text);

        var result = Validate(SerializeFields(fields));
        Assert.True(result.IsValid);
        Assert.Null(result.Error);
        Assert.Equal(new AlarmMessage("AMR_001", 1791195767000, code, level, state,
            includeMessage ? text : null, ReceivedAt), result.Message);
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(1791195767L)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Timestamp_PreservesInt64WithoutConversion(long timestamp)
    {
        var result = Validate(BuildJson("ts", timestamp.ToString(CultureInfo.InvariantCulture)));
        Assert.True(result.IsValid);
        Assert.Equal(timestamp, result.Message!.Timestamp);
        Assert.Equal(ReceivedAt, result.Message.ReceivedAtUtc);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("ts")]
    [InlineData("code")]
    [InlineData("level")]
    [InlineData("alarmState")]
    public void RequiredField_MustExist(string field)
    {
        var fields = ValidFields();
        fields.Remove(field);
        AssertFailure(Validate(SerializeFields(fields)), ValidationErrorCode.MissingField, "$." + field);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("ts")]
    [InlineData("code")]
    [InlineData("level")]
    [InlineData("alarmState")]
    [InlineData("message")]
    public void EveryAllowedField_RejectsDuplicates(string field)
    {
        string json = BuildJson("message", "\"text\"");
        AssertFailure(Validate(json[..^1] + $",\"{field}\":null}}"),
            ValidationErrorCode.DuplicateField, "$." + field);
    }

    [Theory]
    [InlineData("id", "null", ValidationErrorCode.InvalidType)]
    [InlineData("id", "1", ValidationErrorCode.InvalidType)]
    [InlineData("id", "\"\"", ValidationErrorCode.InvalidValue)]
    [InlineData("id", "\" \\t\"", ValidationErrorCode.InvalidValue)]
    [InlineData("id", "\"AMR_002\"", ValidationErrorCode.IdMismatch)]
    [InlineData("id", "\"amr_001\"", ValidationErrorCode.IdMismatch)]
    [InlineData("ts", "null", ValidationErrorCode.InvalidType)]
    [InlineData("ts", "\"1\"", ValidationErrorCode.InvalidType)]
    [InlineData("ts", "true", ValidationErrorCode.InvalidType)]
    [InlineData("ts", "1.0", ValidationErrorCode.InvalidType)]
    [InlineData("ts", "1e3", ValidationErrorCode.InvalidType)]
    [InlineData("ts", "1E+3", ValidationErrorCode.InvalidType)]
    [InlineData("ts", "9223372036854775808", ValidationErrorCode.OutOfRange)]
    [InlineData("ts", "-9223372036854775809", ValidationErrorCode.OutOfRange)]
    [InlineData("code", "null", ValidationErrorCode.InvalidType)]
    [InlineData("code", "1", ValidationErrorCode.InvalidType)]
    [InlineData("code", "false", ValidationErrorCode.InvalidType)]
    [InlineData("code", "[]", ValidationErrorCode.InvalidType)]
    [InlineData("code", "{}", ValidationErrorCode.InvalidType)]
    [InlineData("code", "\"\"", ValidationErrorCode.InvalidValue)]
    [InlineData("code", "\" \\t\\r\\n\"", ValidationErrorCode.InvalidValue)]
    [InlineData("code", "\"\\u3000\"", ValidationErrorCode.InvalidValue)]
    [InlineData("message", "null", ValidationErrorCode.InvalidType)]
    [InlineData("message", "1", ValidationErrorCode.InvalidType)]
    [InlineData("message", "true", ValidationErrorCode.InvalidType)]
    [InlineData("message", "[]", ValidationErrorCode.InvalidType)]
    [InlineData("message", "{}", ValidationErrorCode.InvalidType)]
    [InlineData("message", "\"\"", ValidationErrorCode.InvalidValue)]
    [InlineData("message", "\" \\t\\r\\n\"", ValidationErrorCode.InvalidValue)]
    [InlineData("message", "\"\\u3000\"", ValidationErrorCode.InvalidValue)]
    [InlineData("level", "null", ValidationErrorCode.InvalidType)]
    [InlineData("level", "1", ValidationErrorCode.InvalidType)]
    [InlineData("level", "true", ValidationErrorCode.InvalidType)]
    [InlineData("level", "[]", ValidationErrorCode.InvalidType)]
    [InlineData("level", "\"\"", ValidationErrorCode.InvalidValue)]
    [InlineData("level", "\"warn\"", ValidationErrorCode.InvalidValue)]
    [InlineData("level", "\" WARN \"", ValidationErrorCode.InvalidValue)]
    [InlineData("level", "\"ERROR\"", ValidationErrorCode.InvalidValue)]
    [InlineData("alarmState", "null", ValidationErrorCode.InvalidType)]
    [InlineData("alarmState", "1", ValidationErrorCode.InvalidType)]
    [InlineData("alarmState", "false", ValidationErrorCode.InvalidType)]
    [InlineData("alarmState", "{}", ValidationErrorCode.InvalidType)]
    [InlineData("alarmState", "\"\"", ValidationErrorCode.InvalidValue)]
    [InlineData("alarmState", "\"active\"", ValidationErrorCode.InvalidValue)]
    [InlineData("alarmState", "\"ACTIVE \"", ValidationErrorCode.InvalidValue)]
    [InlineData("alarmState", "\"ACKNOWLEDGED\"", ValidationErrorCode.InvalidValue)]
    public void InvalidValue_ReturnsFieldErrorAndAllowsNextValidInput(string field, string value, ValidationErrorCode code)
    {
        AssertFailure(Validate(BuildJson(field, value)), code, "$." + field);
        Assert.True(Validate(ValidJson).IsValid);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("Level")]
    [InlineData("Message")]
    [InlineData("secret-input")]
    public void AdditionalOrWrongCaseField_IsRejectedWithoutInputInReason(string field)
    {
        var result = Validate(BuildJson(field, "\"private-payload\""));
        AssertFailure(result, ValidationErrorCode.UnexpectedField, "$");
        Assert.Equal("Only id, ts, code, level, alarmState and message are allowed.", result.Error!.Reason);
    }

    [Fact]
    public void EscapedDuplicateName_IsRejected()
    {
        string json = BuildJson("message", "\"first\"");
        AssertFailure(Validate(json[..^1] + ",\"\\u006dessage\":\"second\"}"),
            ValidationErrorCode.DuplicateField, "$.message");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AMR/AMR_001/alarm")]
    [InlineData("amr/AMR_001/Alarm")]
    [InlineData("amr/AMR_001/heartbeat")]
    [InlineData("amr/AMR_001/alarm/extra")]
    [InlineData("amr//alarm")]
    [InlineData("amr/AMR 001/alarm")]
    [InlineData("amr/AMR\t001/alarm")]
    [InlineData("amr/AMR\0/alarm")]
    [InlineData("amr/AMR+001/alarm")]
    [InlineData("amr/AMR#001/alarm")]
    public void InvalidTopic_IsRejected(string? topic)
    {
        var result = _validator.Validate(topic, Bytes(ValidJson), ReceivedAt);
        Assert.False(result.IsValid);
        Assert.Null(result.Message);
        Assert.Equal(ValidationErrorCode.InvalidTopic, result.Error!.Code);
    }

    [Fact]
    public void UnicodeStrings_ArePreservedWithoutNormalization()
    {
        const string id = "장비-e\u0301";
        const string code = " 경고/+#-e\u0301 ";
        const string text = " 경보 e\u0301 🚨\n ";
        var fields = ValidFields();
        fields["id"] = JsonSerializer.Serialize(id);
        fields["code"] = JsonSerializer.Serialize(code);
        fields["message"] = JsonSerializer.Serialize(text);
        var result = _validator.Validate($"amr/{id}/alarm", Bytes(SerializeFields(fields)), ReceivedAt);
        Assert.True(result.IsValid);
        Assert.Equal(id, result.Message!.Id);
        Assert.Equal(code, result.Message.Code);
        Assert.Equal(text, result.Message.Message);
        AssertFailure(_validator.Validate("amr/장비-é/alarm", Bytes(SerializeFields(fields)), ReceivedAt),
            ValidationErrorCode.IdMismatch, "$.id");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"id\":NaN}")]
    [InlineData("{\"id\":Infinity}")]
    [InlineData("{\"id\":1,}")]
    [InlineData("{/* comment */\"id\":1}")]
    public void InvalidJson_IsRejected(string json) => AssertFailure(Validate(json), ValidationErrorCode.InvalidJson, "$");

    [Theory]
    [InlineData("code")]
    [InlineData("message")]
    public void InvalidUnicodeString_IsRejectedWithoutExposingInput(string field)
    {
        var result = Validate(BuildJson(field, "\"private-payload\\uD800\""));
        AssertFailure(result, ValidationErrorCode.InvalidJson, "$");
        Assert.DoesNotContain("private-payload", result.Error!.Reason);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("\"text\"")]
    public void Root_MustBeObject(string json) => AssertFailure(Validate(json), ValidationErrorCode.InvalidType, "$");

    [Theory]
    [InlineData("FF")]
    [InlineData("C080")]
    [InlineData("EAB0")]
    [InlineData("EDA080")]
    public void MalformedUtf8_IsRejected(string hex) =>
        AssertFailure(_validator.Validate(Topic, new(Convert.FromHexString(hex)), ReceivedAt), ValidationErrorCode.InvalidUtf8, "$");

    [Fact]
    public void SizeAndDepthLimits_ApplyToEntirePayloadBeforeFieldChecks()
    {
        Assert.True(Validate(ValidJson.PadRight(65_536)).IsValid);
        AssertFailure(Validate(ValidJson.PadRight(65_537)), ValidationErrorCode.PayloadTooLarge, "$");
        string large = BuildJson("message", "\"" + new string('가', 22_000) + "\"");
        Assert.True(large.Length < 65_536);
        AssertFailure(Validate(large), ValidationErrorCode.PayloadTooLarge, "$");
        string AtDepth(int depth) => BuildJson("message", new string('[', depth - 1) + "0" + new string(']', depth - 1));
        AssertFailure(Validate(AtDepth(16)), ValidationErrorCode.InvalidType, "$.message");
        AssertFailure(Validate(AtDepth(17)), ValidationErrorCode.InvalidJson, "$");
    }

    [Fact]
    public void FullPayload_IsUsedBeyondDiagnosticPreview()
    {
        string text = new('가', 300);
        Assert.Equal(text, Validate(BuildJson("message", JsonSerializer.Serialize(text))).Message!.Message);
        AssertFailure(Validate(ValidJson.PadRight(300) + "private-payload"), ValidationErrorCode.InvalidJson, "$");
    }

    [Fact]
    public void SegmentedPayload_CanSplitUnicodeAndNumericTokens()
    {
        string json = ValidJson.Replace("AMR_001", "장비");
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        var first = new Segment(bytes.AsMemory(0, 8));
        var second = first.Append(bytes.AsMemory(8, 17));
        var last = second.Append(bytes.AsMemory(25));
        var result = _validator.Validate("amr/장비/alarm", new(first, 0, last, last.Memory.Length), ReceivedAt);
        Assert.True(result.IsValid);
        Assert.Equal("장비", result.Message!.Id);
        Assert.Equal(1791195767000, result.Message.Timestamp);
    }

    [Fact]
    public void FirstFailureAndOptionalMessage_DoNotLeakAcrossCalls()
    {
        var fields = ValidFields();
        fields["code"] = "\"\"";
        fields["level"] = "\"private-payload\"";
        AssertFailure(Validate(SerializeFields(fields)), ValidationErrorCode.InvalidValue, "$.code");
        fields["code"] = "\"A01\"";
        var invalidLevel = Validate(SerializeFields(fields));
        AssertFailure(invalidLevel, ValidationErrorCode.InvalidValue, "$.level");
        Assert.DoesNotContain("private-payload", invalidLevel.Error!.Reason);
        var withMessage = Validate(BuildJson("message", "\"first\""));
        var omitted = Validate(ValidJson);
        Assert.True(omitted.IsValid);
        Assert.Null(omitted.Message!.Message);
        Assert.Equal("first", withMessage.Message!.Message);
    }

    private static Dictionary<string, string> ValidFields() => new()
    {
        ["id"] = "\"AMR_001\"", ["ts"] = "1791195767000", ["code"] = "\"A01\"",
        ["level"] = "\"WARN\"", ["alarmState"] = "\"ACTIVE\""
    };

    private static string BuildJson(string field, string value)
    {
        var fields = ValidFields();
        fields[field] = value;
        return SerializeFields(fields);
    }

    private static string SerializeFields(Dictionary<string, string> fields) =>
        "{" + string.Join(",", fields.Select(pair => $"\"{pair.Key}\":{pair.Value}")) + "}";
    private static ReadOnlySequence<byte> Bytes(string json) => new(Encoding.UTF8.GetBytes(json));
    private AlarmValidationResult Validate(string json) => _validator.Validate(Topic, Bytes(json), ReceivedAt);

    private static void AssertFailure(AlarmValidationResult result, ValidationErrorCode code, string field)
    {
        Assert.False(result.IsValid);
        Assert.Null(result.Message);
        Assert.NotNull(result.Error);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(field, result.Error.Field);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Reason));
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;
        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
