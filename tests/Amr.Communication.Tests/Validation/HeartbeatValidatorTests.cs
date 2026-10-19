using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Amr.Communication.Validation;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class HeartbeatValidatorTests
{
    private const string Topic = "amr/AMR_001/heartbeat";
    private const string ValidJson = "{\"id\":\"AMR_001\",\"ts\":1791195767000,\"status\":\"OK\"}";
    private static readonly DateTimeOffset ReceivedAt = new(2026, 10, 19, 1, 2, 3, TimeSpan.Zero);
    private readonly HeartbeatValidator _validator = new();

    [Fact]
    public void ValidHeartbeat_PreservesEveryFieldAndSuppliedReceptionTime()
    {
        var result = Validate(ValidJson);
        Assert.True(result.IsValid);
        Assert.Null(result.Error);
        var message = Assert.IsType<Amr.Communication.Models.HeartbeatMessage>(result.Message);
        Assert.Equal("AMR_001", message.Id);
        Assert.Equal(1791195767000, message.Timestamp);
        Assert.Equal("OK", message.Status);
        Assert.Equal(ReceivedAt, message.ReceivedAtUtc);
    }

    [Fact]
    public void MissingTimestamp_IsValidJsonButViolatesHeartbeatContract()
    {
        const string missing = "{\"id\":\"AMR_001\",\"status\":\"OK\"}";
        using var parsed = JsonDocument.Parse(missing);
        Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
        AssertFailure(Validate(missing), ValidationErrorCode.MissingField, "$.ts");
        Assert.True(Validate(ValidJson).IsValid);
    }

    [Theory]
    [InlineData("{\"ts\":1,\"status\":\"OK\"}", "$.id")]
    [InlineData("{\"id\":\"AMR_001\",\"ts\":1}", "$.status")]
    public void MissingRequiredField_IsRejected(string json, string field) =>
        AssertFailure(Validate(json), ValidationErrorCode.MissingField, field);

    [Theory]
    [InlineData("id", "null", ValidationErrorCode.InvalidType)]
    [InlineData("id", "123", ValidationErrorCode.InvalidType)]
    [InlineData("id", "{}", ValidationErrorCode.InvalidType)]
    [InlineData("id", "\"\"", ValidationErrorCode.InvalidValue)]
    [InlineData("id", "\" \\t\"", ValidationErrorCode.InvalidValue)]
    [InlineData("id", "\"AMR_002\"", ValidationErrorCode.IdMismatch)]
    [InlineData("id", "\"amr_001\"", ValidationErrorCode.IdMismatch)]
    [InlineData("id", "\"AMR_001 \"", ValidationErrorCode.IdMismatch)]
    [InlineData("ts", "null", ValidationErrorCode.InvalidType)]
    [InlineData("ts", "\"1791195767000\"", ValidationErrorCode.InvalidType)]
    [InlineData("ts", "true", ValidationErrorCode.InvalidType)]
    [InlineData("ts", "[]", ValidationErrorCode.InvalidType)]
    [InlineData("ts", "1.0", ValidationErrorCode.InvalidType)]
    [InlineData("ts", "1e3", ValidationErrorCode.InvalidType)]
    [InlineData("ts", "1E+3", ValidationErrorCode.InvalidType)]
    [InlineData("ts", "9223372036854775808", ValidationErrorCode.OutOfRange)]
    [InlineData("ts", "-9223372036854775809", ValidationErrorCode.OutOfRange)]
    [InlineData("status", "null", ValidationErrorCode.InvalidType)]
    [InlineData("status", "false", ValidationErrorCode.InvalidType)]
    [InlineData("status", "{}", ValidationErrorCode.InvalidType)]
    [InlineData("status", "\"\"", ValidationErrorCode.InvalidValue)]
    [InlineData("status", "\"ok\"", ValidationErrorCode.InvalidValue)]
    [InlineData("status", "\"ONLINE\"", ValidationErrorCode.InvalidValue)]
    [InlineData("status", "\"OK \"", ValidationErrorCode.InvalidValue)]
    public void InvalidField_IsRejectedAndNextValidInputSucceeds(string field, string value, ValidationErrorCode code)
    {
        var fields = new Dictionary<string, string>
        {
            ["id"] = "\"AMR_001\"", ["ts"] = "1791195767000", ["status"] = "\"OK\""
        };
        fields[field] = value;
        string json = "{" + string.Join(",", fields.Select(pair => $"\"{pair.Key}\":{pair.Value}")) + "}";
        AssertFailure(Validate(json), code, "$." + field);
        Assert.True(Validate(ValidJson).IsValid);
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(1791195767L)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Int64_IsPreservedWithoutUnitGuessingOrDateConversion(long timestamp)
    {
        string json = ValidJson.Replace("1791195767000", timestamp.ToString(CultureInfo.InvariantCulture));
        var result = Validate(json);
        Assert.True(result.IsValid);
        Assert.Equal(timestamp, result.Message!.Timestamp);
        Assert.Equal(ReceivedAt, result.Message.ReceivedAtUtc);
    }

    [Theory]
    [InlineData("id", "\"AMR_001\"")]
    [InlineData("ts", "1")]
    [InlineData("status", "\"OK\"")]
    [InlineData("\\u0069d", "\"AMR_001\"")]
    public void DuplicateField_IncludingEscapedName_IsRejected(string name, string value)
    {
        string json = ValidJson[..^1] + $",\"{name}\":{value}}}";
        string field = name == "\\u0069d" ? "id" : name;
        AssertFailure(Validate(json), ValidationErrorCode.DuplicateField, "$." + field);
    }

    [Theory]
    [InlineData("ID")]
    [InlineData("Ts")]
    [InlineData("Status")]
    [InlineData("secret-field-name")]
    public void ExtraOrWrongCaseField_IsRejectedWithoutEchoingInput(string name)
    {
        string json = ValidJson[..^1] + $",\"{name}\":\"secret-value\"}}";
        var result = Validate(json);
        AssertFailure(result, ValidationErrorCode.UnexpectedField, "$");
        Assert.DoesNotContain(name, result.Error!.ToString());
        Assert.DoesNotContain("secret-value", result.Error.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("{")]
    [InlineData("{\"id\":\"AMR_001\",/*secret*/\"ts\":1,\"status\":\"OK\"}")]
    [InlineData("{\"id\":\"AMR_001\",\"ts\":1,\"status\":\"OK\",}")]
    [InlineData("{}{}")]
    [InlineData("{\"id\":\"AMR_001\",\"ts\":01,\"status\":\"OK\"}")]
    [InlineData("{\"id\":\"AMR_001\",\"ts\":+1,\"status\":\"OK\"}")]
    [InlineData("{\"id\":\"\\uD800\",\"ts\":1,\"status\":\"OK\"}")]
    [InlineData("{\"id\":\"AMR_001\",\"ts\":1,\"status\":\"\\uDC00\"}")]
    [InlineData("{\"\\uD800\":1}")]
    public void InvalidJson_IsRejectedWithoutLeakingParserDetailsAndNextInputSucceeds(string json)
    {
        var result = Validate(json);
        AssertFailure(result, ValidationErrorCode.InvalidJson, "$");
        Assert.DoesNotContain("secret", result.Error!.ToString());
        Assert.True(Validate(ValidJson).IsValid);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("\"text\"")]
    public void NonObjectJson_IsRejected(string json) =>
        AssertFailure(Validate(json), ValidationErrorCode.InvalidType, "$");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AMR/AMR_001/heartbeat")]
    [InlineData("amr/AMR_001/Heartbeat")]
    [InlineData("amr/AMR_001/telemetry")]
    [InlineData("amr/AMR_001/heartbeat/extra")]
    [InlineData("amr/area/AMR_001/heartbeat")]
    [InlineData("amr/AMR_001")]
    public void Topic_MustHaveExactCaseAndThreeSegments(string? topic) =>
        AssertFailure(Validate(ValidJson, topic), ValidationErrorCode.InvalidTopic, "$topic");

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("AMR 001")]
    [InlineData("AMR\t001")]
    [InlineData("AMR\u0000001")]
    [InlineData("AMR\u00a0001")]
    [InlineData("AMR+001")]
    [InlineData("AMR#001")]
    public void TopicId_RejectsForbiddenCharacters(string id) =>
        AssertFailure(Validate(ValidJson, $"amr/{id}/heartbeat"), ValidationErrorCode.InvalidTopic, "$topic.amrId");

    [Theory]
    [InlineData("장비_001")]
    [InlineData("장비_🚗")]
    [InlineData("e\u0301")]
    public void UnicodeId_IsPreserved(string id)
    {
        string json = JsonSerializer.Serialize(new { id, ts = 1, status = "OK" });
        var result = Validate(json, $"amr/{id}/heartbeat");
        Assert.True(result.IsValid);
        Assert.Equal(id, result.Message!.Id);
    }

    [Fact]
    public void UnicodeId_IsNotNormalized()
    {
        const string json = "{\"id\":\"e\\u0301\",\"ts\":1,\"status\":\"OK\"}";
        AssertFailure(Validate(json, "amr/é/heartbeat"), ValidationErrorCode.IdMismatch, "$.id");
    }

    [Theory]
    [InlineData("FF")]
    [InlineData("C080")]
    [InlineData("EAB0")]
    [InlineData("EDA080")]
    public void MalformedUtf8_IsNotReplacedByDiagnosticCharacters(string hex)
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes("{\"id\":\""), .. Convert.FromHexString(hex),
            .. Encoding.UTF8.GetBytes("\",\"ts\":1,\"status\":\"OK\"}")];
        AssertFailure(ValidateBytes(bytes), ValidationErrorCode.InvalidUtf8, "$");
        Assert.True(Validate(ValidJson).IsValid);
    }

    [Fact]
    public void SizeLimit_AcceptsExactBoundaryAndRejectsOneByteOverBeforeOtherChecks()
    {
        string atLimit = ValidJson.PadRight(HeartbeatValidator.MaxPayloadBytes, ' ');
        Assert.Equal(65_536, Encoding.UTF8.GetByteCount(atLimit));
        Assert.True(Validate(atLimit).IsValid);
        AssertFailure(Validate(atLimit + " "), ValidationErrorCode.PayloadTooLarge, "$");
        byte[] oversized = new byte[65_537];
        Array.Fill(oversized, (byte)0xff);
        AssertFailure(_validator.Validate(null, new ReadOnlySequence<byte>(oversized), ReceivedAt),
            ValidationErrorCode.PayloadTooLarge, "$");
        AssertFailure(Validate("{}".PadRight(65_536, ' ')), ValidationErrorCode.MissingField, "$.id");
    }

    [Fact]
    public void SizeLimit_CountsUtf8BytesRatherThanCharacters()
    {
        string id = new('가', 22_000);
        string json = $"{{\"id\":\"{id}\",\"ts\":1,\"status\":\"OK\"}}";
        Assert.True(json.Length < 65_536);
        AssertFailure(Validate(json, $"amr/{id}/heartbeat"), ValidationErrorCode.PayloadTooLarge, "$");
    }

    [Fact]
    public void DepthLimit_IsIndependentOfAllowedHeartbeatFields()
    {
        // 루트 object가 깊이 1. 깊이 16은 JSON 해석 후 추가 필드 규칙에서 거부한다.
        string AtDepth(int depth) => ValidJson[..^1] + ",\"extra\":" +
            new string('[', depth - 1) + "0" + new string(']', depth - 1) + "}";
        AssertFailure(Validate(AtDepth(16)), ValidationErrorCode.UnexpectedField, "$");
        AssertFailure(Validate(AtDepth(17)), ValidationErrorCode.InvalidJson, "$");
        Assert.True(Validate(ValidJson).IsValid);
    }

    [Fact]
    public void PayloadBeyondPreview_IsValidatedInFull()
    {
        string longJson = new(' ', 300);
        Assert.True(Validate(longJson + ValidJson).IsValid);
        AssertFailure(Validate(ValidJson + longJson + "garbage"), ValidationErrorCode.InvalidJson, "$");
    }

    [Fact]
    public void SegmentedPayload_CanSplitUtf8CharacterAndJsonToken()
    {
        const string json = "{\"id\":\"장비\",\"ts\":1791195767000,\"status\":\"OK\"}";
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        var first = new Segment(bytes.AsMemory(0, 8));
        var second = first.Append(bytes.AsMemory(8, 17));
        var last = second.Append(bytes.AsMemory(25));
        var payload = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        var result = _validator.Validate("amr/장비/heartbeat", payload, ReceivedAt);
        Assert.True(result.IsValid);
        Assert.Equal("장비", result.Message!.Id);
        Assert.Equal(1791195767000, result.Message.Timestamp);
    }

    private HeartbeatValidationResult Validate(string json) => Validate(json, Topic);
    private HeartbeatValidationResult Validate(string json, string? topic) =>
        _validator.Validate(topic, new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(json)), ReceivedAt);
    private HeartbeatValidationResult ValidateBytes(byte[] bytes) =>
        _validator.Validate(Topic, new ReadOnlySequence<byte>(bytes), ReceivedAt);

    private static void AssertFailure(HeartbeatValidationResult result, ValidationErrorCode code, string field)
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
