using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Amr.Communication.Models;
using Amr.Communication.Validation;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class AckValidatorTests
{
    private const string Topic = "amr/AMR_001/ack";
    private const string ValidJson = """{"cmdId":"CMD_001","result":"FAILURE","ts":1791195767000}""";
    private static readonly DateTimeOffset ReceivedAt = new(2026, 10, 19, 6, 0, 0, TimeSpan.Zero);
    private readonly AckValidator _validator = new();

    public static IEnumerable<object[]> AllowedCombinations()
    {
        foreach (string result in new[] { "SUCCESS", "FAILURE" })
        foreach (bool includeId in new[] { false, true })
        foreach (bool includeReason in new[] { false, true })
            yield return [result, includeId, includeReason];
    }

    [Theory]
    [MemberData(nameof(AllowedCombinations))]
    public void ValidResults_PreserveSuccessOrFailureAndOptionalFields(string result, bool includeId, bool includeReason)
    {
        const string reason = " 이동 불가\n확인 ";
        var fields = ValidFields();
        fields["result"] = JsonSerializer.Serialize(result);
        if (includeId) fields["id"] = "\"AMR_001\"";
        if (includeReason) fields["reason"] = JsonSerializer.Serialize(reason);

        var actual = Validate(SerializeFields(fields));
        Assert.True(actual.IsValid);
        Assert.Null(actual.Error);
        Assert.Equal(new AckMessage("CMD_001", result, 1791195767000,
            includeId ? "AMR_001" : null, includeReason ? reason : null, ReceivedAt), actual.Message);
    }

    [Theory]
    [InlineData("SUCCESS")]
    [InlineData("FAILURE")]
    public void Strings_ArePreservedWithoutTrimmingOrNormalization(string result)
    {
        const string id = "장비-e\u0301";
        const string commandId = " 명령/+#-e\u0301 ";
        const string reason = " 사유 e\u0301 🚨\n ";
        var fields = ValidFields();
        fields["cmdId"] = JsonSerializer.Serialize(commandId);
        fields["result"] = JsonSerializer.Serialize(result);
        fields["id"] = JsonSerializer.Serialize(id);
        fields["reason"] = JsonSerializer.Serialize(reason);
        var actual = _validator.Validate($"amr/{id}/ack", Bytes(SerializeFields(fields)), ReceivedAt);
        Assert.True(actual.IsValid);
        Assert.Equal(new AckMessage(commandId, result, 1791195767000, id, reason, ReceivedAt), actual.Message);
        AssertFailure(_validator.Validate("amr/장비-é/ack", Bytes(SerializeFields(fields)), ReceivedAt),
            ValidationErrorCode.IdMismatch, "$.id");
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
    }

    [Theory]
    [InlineData("cmdId")]
    [InlineData("result")]
    [InlineData("ts")]
    public void RequiredField_MustExist(string field)
    {
        var fields = ValidFields();
        fields.Remove(field);
        AssertFailure(Validate(SerializeFields(fields)), ValidationErrorCode.MissingField, "$." + field);
    }

    [Theory]
    [InlineData("cmdId")]
    [InlineData("result")]
    [InlineData("ts")]
    [InlineData("id")]
    [InlineData("reason")]
    public void EveryAllowedField_RejectsDuplicates(string field)
    {
        var fields = ValidFields();
        fields["id"] = "\"AMR_001\"";
        fields["reason"] = "\"text\"";
        string json = SerializeFields(fields);
        AssertFailure(Validate(json[..^1] + $",\"{field}\":null}}"), ValidationErrorCode.DuplicateField, "$." + field);
    }

    public static IEnumerable<object[]> InvalidValues()
    {
        foreach (string field in new[] { "cmdId", "reason" })
        {
            foreach (string value in new[] { "null", "1", "true", "[]", "{}" })
                yield return [field, value, ValidationErrorCode.InvalidType];
            foreach (string value in new[] { "\"\"", "\" \\t\\r\\n\"", "\"\\u3000\"" })
                yield return [field, value, ValidationErrorCode.InvalidValue];
        }
        foreach (string value in new[] { "null", "1", "true", "[]", "{}" })
            yield return ["id", value, ValidationErrorCode.InvalidType];
        foreach (string value in new[] { "\"\"", "\" \\t\"" })
            yield return ["id", value, ValidationErrorCode.InvalidValue];
        foreach (string value in new[] { "\"AMR_002\"", "\"amr_001\"" })
            yield return ["id", value, ValidationErrorCode.IdMismatch];
        foreach (string value in new[] { "null", "true", "[]", "\"1\"", "1.0", "1e3", "1E+3" })
            yield return ["ts", value, ValidationErrorCode.InvalidType];
        foreach (string value in new[] { "9223372036854775808", "-9223372036854775809" })
            yield return ["ts", value, ValidationErrorCode.OutOfRange];
        foreach (string value in new[] { "null", "1", "true", "[]", "{}" })
            yield return ["result", value, ValidationErrorCode.InvalidType];
        foreach (string value in new[] { "\"\"", "\"success\"", "\"FAILURE \"", "\"FAILED\"" })
            yield return ["result", value, ValidationErrorCode.InvalidValue];
    }

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public void InvalidValue_IsRejectedAndNextFailureAckIsStillValid(string field, string value, ValidationErrorCode code)
    {
        AssertFailure(Validate(BuildJson(field, value)), code, "$." + field);
        var next = Validate(ValidJson);
        Assert.True(next.IsValid);
        Assert.Equal("FAILURE", next.Message!.Result);
        Assert.Null(next.Error);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("CmdId")]
    [InlineData("Reason")]
    [InlineData("secret-input")]
    public void ExtraOrWrongCaseField_IsRejectedWithoutEcho(string field)
    {
        var result = Validate(BuildJson(field, "\"private-payload\""));
        AssertFailure(result, ValidationErrorCode.UnexpectedField, "$");
        Assert.Equal("Only cmdId, result, ts, id and reason are allowed.", result.Error!.Reason);
    }

    [Fact]
    public void EscapedDuplicateOptionalField_IsRejected()
    {
        string json = BuildJson("reason", "\"first\"");
        AssertFailure(Validate(json[..^1] + ",\"\\u0072eason\":\"second\"}"), ValidationErrorCode.DuplicateField, "$.reason");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AMR/AMR_001/ack")]
    [InlineData("amr/AMR_001/ACK")]
    [InlineData("amr/AMR_001/alarm")]
    [InlineData("amr/AMR_001/ack/extra")]
    [InlineData("amr//ack")]
    [InlineData("amr/AMR 001/ack")]
    [InlineData("amr/AMR\t001/ack")]
    [InlineData("amr/AMR\0/ack")]
    [InlineData("amr/AMR+001/ack")]
    [InlineData("amr/AMR#001/ack")]
    public void TopicIsAlwaysChecked_EvenWithoutPayloadId(string? topic)
    {
        var result = _validator.Validate(topic, Bytes(ValidJson), ReceivedAt);
        Assert.False(result.IsValid);
        Assert.Null(result.Message);
        Assert.Equal(ValidationErrorCode.InvalidTopic, result.Error!.Code);
    }

    [Theory]
    [InlineData("AMR_002")]
    [InlineData("장비-e\u0301")]
    public void OmittedId_IsNotFilledFromTopic(string id)
    {
        var result = _validator.Validate($"amr/{id}/ack", Bytes(ValidJson), ReceivedAt);
        Assert.True(result.IsValid);
        Assert.Null(result.Message!.Id);
        Assert.Null(result.Message.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"ts\":NaN}")]
    [InlineData("{\"ts\":Infinity}")]
    [InlineData("{\"ts\":1,}")]
    [InlineData("{/* comment */\"ts\":1}")]
    public void InvalidJson_IsRejected(string json) => AssertFailure(Validate(json), ValidationErrorCode.InvalidJson, "$");

    [Theory]
    [InlineData("cmdId")]
    [InlineData("reason")]
    public void InvalidUnicodeString_DoesNotExposeInput(string field)
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
    public void SizeAndDepthLimits_ApplyToEntirePayload()
    {
        Assert.True(Validate(ValidJson.PadRight(65_536)).IsValid);
        AssertFailure(Validate(ValidJson.PadRight(65_537)), ValidationErrorCode.PayloadTooLarge, "$");
        string large = BuildJson("reason", "\"" + new string('가', 22_000) + "\"");
        Assert.True(large.Length < 65_536);
        AssertFailure(Validate(large), ValidationErrorCode.PayloadTooLarge, "$");
        string AtDepth(int depth) => BuildJson("reason", new string('[', depth - 1) + "0" + new string(']', depth - 1));
        AssertFailure(Validate(AtDepth(16)), ValidationErrorCode.InvalidType, "$.reason");
        AssertFailure(Validate(AtDepth(17)), ValidationErrorCode.InvalidJson, "$");
    }

    [Fact]
    public void FullPayload_IsUsedBeyondDiagnosticPreview()
    {
        string reason = new('가', 300);
        Assert.Equal(reason, Validate(BuildJson("reason", JsonSerializer.Serialize(reason))).Message!.Reason);
        var result = Validate(ValidJson.PadRight(300) + "private-payload");
        AssertFailure(result, ValidationErrorCode.InvalidJson, "$");
        Assert.DoesNotContain("private-payload", result.Error!.Reason);
    }

    [Fact]
    public void SegmentedPayload_PreservesSplitUnicodeAndNumericTokens()
    {
        string json = BuildJson("cmdId", "\"명령\"");
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        // 한 바이트씩 나눠 UTF-8 문자와 ts 숫자 토큰의 경계를 모두 통과한다.
        var first = new Segment(bytes.AsMemory(0, 1));
        var last = first;
        for (int i = 1; i < bytes.Length; i++) last = last.Append(bytes.AsMemory(i, 1));
        var result = _validator.Validate(Topic, new(first, 0, last, last.Memory.Length), ReceivedAt);
        Assert.True(result.IsValid);
        Assert.Equal(new AckMessage("명령", "FAILURE", 1791195767000, null, null, ReceivedAt), result.Message);
    }

    [Fact]
    public void FirstFailureAndOptionalValues_DoNotLeakAcrossCalls()
    {
        var fields = ValidFields();
        fields["id"] = "\"WRONG\"";
        fields["ts"] = "1.0";
        fields["cmdId"] = "\"\"";
        AssertFailure(Validate(SerializeFields(fields)), ValidationErrorCode.IdMismatch, "$.id");
        fields.Remove("id");
        AssertFailure(Validate(SerializeFields(fields)), ValidationErrorCode.InvalidType, "$.ts");
        fields["ts"] = "1";
        AssertFailure(Validate(SerializeFields(fields)), ValidationErrorCode.InvalidValue, "$.cmdId");
        fields["cmdId"] = "\"CMD_001\"";
        fields["result"] = "\"private-payload\"";
        var invalidResult = Validate(SerializeFields(fields));
        AssertFailure(invalidResult, ValidationErrorCode.InvalidValue, "$.result");
        Assert.DoesNotContain("private-payload", invalidResult.Error!.Reason);
        fields["result"] = "\"SUCCESS\"";
        fields["id"] = "\"AMR_001\"";
        fields["reason"] = "\"done\"";
        var previous = Validate(SerializeFields(fields));
        var next = Validate(ValidJson);
        Assert.True(previous.IsValid);
        Assert.True(next.IsValid);
        Assert.Equal("FAILURE", next.Message!.Result);
        Assert.Null(next.Message.Id);
        Assert.Null(next.Message.Reason);
        Assert.Equal("SUCCESS", previous.Message!.Result);
        Assert.Equal("AMR_001", previous.Message.Id);
        Assert.Equal("done", previous.Message.Reason);
    }

    private static Dictionary<string, string> ValidFields() => new()
    {
        ["cmdId"] = "\"CMD_001\"", ["result"] = "\"FAILURE\"", ["ts"] = "1791195767000"
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
    private AckValidationResult Validate(string json) => _validator.Validate(Topic, Bytes(json), ReceivedAt);

    private static void AssertFailure(AckValidationResult result, ValidationErrorCode code, string field)
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
