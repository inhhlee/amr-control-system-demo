using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Amr.Communication.Models;
using Amr.Communication.Validation;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class TelemetryValidatorTests
{
    private const string Topic = "amr/AMR_001/telemetry";
    private const string ValidJson = """{"id":"AMR_001","ts":1791195767000,"pos":{"x":1.2,"y":-3.4},"sp":0.5,"state":"IDLE"}""";
    private static readonly DateTimeOffset ReceivedAt = new(2026, 10, 19, 5, 0, 0, TimeSpan.Zero);
    private readonly TelemetryValidator _validator = new();

    [Theory]
    [InlineData("IDLE")]
    [InlineData("BUSY")]
    [InlineData("STOPPED")]
    [InlineData("FAULT")]
    public void ValidTelemetry_PreservesEveryFieldAndReceptionTime(string state)
    {
        var result = Validate(ValidJson.Replace("IDLE", state));
        Assert.True(result.IsValid);
        Assert.Null(result.Error);
        var message = Assert.IsType<TelemetryMessage>(result.Message);
        Assert.Equal("AMR_001", message.Id);
        Assert.Equal(1791195767000, message.Timestamp);
        Assert.Equal(new TelemetryPosition(1.2, -3.4), message.Position);
        Assert.Equal(0.5, message.Speed);
        Assert.Equal(state, message.State);
        Assert.Equal(ReceivedAt, message.ReceivedAtUtc);
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(1791195767L)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Timestamp_PreservesInt64WithoutUnitOrDateConversion(long timestamp)
    {
        var result = Validate(ValidJson.Replace("1791195767000", timestamp.ToString(CultureInfo.InvariantCulture)));
        Assert.True(result.IsValid);
        Assert.Equal(timestamp, result.Message!.Timestamp);
    }

    [Theory]
    [InlineData("{\"x\":-1,\"y\":2e2}", "0", -1, 200, 0)]
    [InlineData("{\"x\":1.7976931348623157e308,\"y\":-1.7976931348623157e308}", "1e2", double.MaxValue, double.MinValue, 100)]
    [InlineData("{\"x\":5e-324,\"y\":0}", "-0", double.Epsilon, 0, 0)]
    public void FiniteNumbers_AcceptNegativePositionsZeroSpeedAndExponentNotation(
        string position, string speed, double x, double y, double expectedSpeed)
    {
        var result = Validate(BuildJson("pos", position, "sp", speed));
        Assert.True(result.IsValid);
        Assert.Equal(x, result.Message!.Position.X);
        Assert.Equal(y, result.Message.Position.Y);
        Assert.Equal(expectedSpeed, result.Message.Speed);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("ts")]
    [InlineData("pos")]
    [InlineData("sp")]
    [InlineData("state")]
    public void RequiredField_MustExistExactlyOnce(string name)
    {
        var fields = ValidFields();
        fields.Remove(name);
        AssertFailure(Validate(SerializeFields(fields)), ValidationErrorCode.MissingField, "$." + name);
        string duplicate = ValidJson[..^1] + $",\"{name}\":{ValidFields()[name]}}}";
        AssertFailure(Validate(duplicate), ValidationErrorCode.DuplicateField, "$." + name);
    }

    [Theory]
    [InlineData("id", "null", ValidationErrorCode.InvalidType, "$.id")]
    [InlineData("id", "1", ValidationErrorCode.InvalidType, "$.id")]
    [InlineData("id", "\"\"", ValidationErrorCode.InvalidValue, "$.id")]
    [InlineData("id", "\" \\t\"", ValidationErrorCode.InvalidValue, "$.id")]
    [InlineData("id", "\"AMR_002\"", ValidationErrorCode.IdMismatch, "$.id")]
    [InlineData("id", "\"amr_001\"", ValidationErrorCode.IdMismatch, "$.id")]
    [InlineData("ts", "null", ValidationErrorCode.InvalidType, "$.ts")]
    [InlineData("ts", "\"1\"", ValidationErrorCode.InvalidType, "$.ts")]
    [InlineData("ts", "true", ValidationErrorCode.InvalidType, "$.ts")]
    [InlineData("ts", "1.0", ValidationErrorCode.InvalidType, "$.ts")]
    [InlineData("ts", "1e3", ValidationErrorCode.InvalidType, "$.ts")]
    [InlineData("ts", "1E+3", ValidationErrorCode.InvalidType, "$.ts")]
    [InlineData("ts", "9223372036854775808", ValidationErrorCode.OutOfRange, "$.ts")]
    [InlineData("ts", "-9223372036854775809", ValidationErrorCode.OutOfRange, "$.ts")]
    [InlineData("pos", "null", ValidationErrorCode.InvalidType, "$.pos")]
    [InlineData("pos", "[]", ValidationErrorCode.InvalidType, "$.pos")]
    [InlineData("pos", "1", ValidationErrorCode.InvalidType, "$.pos")]
    [InlineData("pos", "{}", ValidationErrorCode.MissingField, "$.pos.x")]
    [InlineData("pos", "{\"x\":1}", ValidationErrorCode.MissingField, "$.pos.y")]
    [InlineData("pos", "{\"x\":1,\"y\":2,\"z\":3}", ValidationErrorCode.UnexpectedField, "$.pos")]
    [InlineData("pos", "{\"x\":1,\"y\":2,\"x\":3}", ValidationErrorCode.DuplicateField, "$.pos.x")]
    [InlineData("pos", "{\"x\":1,\"y\":2,\"\\u0079\":3}", ValidationErrorCode.DuplicateField, "$.pos.y")]
    [InlineData("pos", "{\"X\":1,\"y\":2}", ValidationErrorCode.UnexpectedField, "$.pos")]
    [InlineData("pos", "{\"x\":null,\"y\":2}", ValidationErrorCode.InvalidType, "$.pos.x")]
    [InlineData("pos", "{\"x\":1,\"y\":\"2\"}", ValidationErrorCode.InvalidType, "$.pos.y")]
    [InlineData("pos", "{\"x\":true,\"y\":2}", ValidationErrorCode.InvalidType, "$.pos.x")]
    [InlineData("pos", "{\"x\":[],\"y\":2}", ValidationErrorCode.InvalidType, "$.pos.x")]
    [InlineData("pos", "{\"x\":1e309,\"y\":2}", ValidationErrorCode.OutOfRange, "$.pos.x")]
    [InlineData("pos", "{\"x\":1,\"y\":-1e309}", ValidationErrorCode.OutOfRange, "$.pos.y")]
    [InlineData("sp", "null", ValidationErrorCode.InvalidType, "$.sp")]
    [InlineData("sp", "\"0.5\"", ValidationErrorCode.InvalidType, "$.sp")]
    [InlineData("sp", "false", ValidationErrorCode.InvalidType, "$.sp")]
    [InlineData("sp", "{}", ValidationErrorCode.InvalidType, "$.sp")]
    [InlineData("sp", "1e309", ValidationErrorCode.OutOfRange, "$.sp")]
    [InlineData("sp", "-1e309", ValidationErrorCode.OutOfRange, "$.sp")]
    [InlineData("sp", "-0.5", ValidationErrorCode.InvalidValue, "$.sp")]
    [InlineData("state", "null", ValidationErrorCode.InvalidType, "$.state")]
    [InlineData("state", "1", ValidationErrorCode.InvalidType, "$.state")]
    [InlineData("state", "\"\"", ValidationErrorCode.InvalidValue, "$.state")]
    [InlineData("state", "\"idle\"", ValidationErrorCode.InvalidValue, "$.state")]
    [InlineData("state", "\"IDLE \"", ValidationErrorCode.InvalidValue, "$.state")]
    [InlineData("state", "\"MOVING\"", ValidationErrorCode.InvalidValue, "$.state")]
    public void InvalidField_IsRejectedAndNextValidInputSucceeds(
        string name, string value, ValidationErrorCode code, string field)
    {
        AssertFailure(Validate(BuildJson(name, value)), code, field);
        Assert.True(Validate(ValidJson).IsValid);
    }

    [Theory]
    [InlineData("ID")]
    [InlineData("secret-name")]
    public void ExtraField_DoesNotLeakInput(string name)
    {
        var result = Validate(ValidJson[..^1] + $",\"{name}\":\"secret-value\"}}");
        AssertFailure(result, ValidationErrorCode.UnexpectedField, "$");
        Assert.DoesNotContain(name, result.Error!.ToString());
        Assert.DoesNotContain("secret-value", result.Error.ToString());
    }

    [Fact]
    public void EscapedNames_AreDecodedBeforeDuplicateDetection()
    {
        Assert.True(Validate(ValidJson.Replace("\"id\"", "\"\\u0069d\"")).IsValid);
        AssertFailure(Validate(ValidJson[..^1] + ",\"\\u0069d\":\"AMR_001\"}"),
            ValidationErrorCode.DuplicateField, "$.id");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AMR/AMR_001/telemetry")]
    [InlineData("amr/AMR_001/Telemetry")]
    [InlineData("amr/AMR_001/heartbeat")]
    [InlineData("amr/AMR_001/telemetry/extra")]
    [InlineData("amr/area/AMR_001/telemetry")]
    public void Topic_RequiresExactKindCaseAndThreeSegments(string? topic) =>
        AssertFailure(_validator.Validate(topic, Bytes(ValidJson), ReceivedAt), ValidationErrorCode.InvalidTopic, "$topic");

    [Theory]
    [InlineData("")]
    [InlineData("AMR 001")]
    [InlineData("AMR\t001")]
    [InlineData("AMR\u0000001")]
    [InlineData("AMR\u00a0001")]
    [InlineData("AMR+001")]
    [InlineData("AMR#001")]
    public void TopicId_RejectsForbiddenCharacters(string id) =>
        AssertFailure(_validator.Validate($"amr/{id}/telemetry", Bytes(ValidJson), ReceivedAt),
            ValidationErrorCode.InvalidTopic, "$topic.amrId");

    [Theory]
    [InlineData("장비_🚗")]
    [InlineData("e\u0301")]
    public void UnicodeId_IsPreservedWithoutNormalization(string id)
    {
        string json = BuildJson("id", JsonSerializer.Serialize(id));
        var result = _validator.Validate($"amr/{id}/telemetry", Bytes(json), ReceivedAt);
        Assert.True(result.IsValid);
        Assert.Equal(id, result.Message!.Id);
        AssertFailure(_validator.Validate("amr/é/telemetry", Bytes(BuildJson("id", "\"e\\u0301\"")), ReceivedAt),
            ValidationErrorCode.IdMismatch, "$.id");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{}{}")]
    [InlineData("{\"secret\": /*secret*/ 1}")]
    [InlineData("{\"secret\":1,}")]
    [InlineData("{\"secret\":NaN}")]
    [InlineData("{\"secret\":Infinity}")]
    [InlineData("{\"\\uD800\":1}")]
    public void InvalidJson_IsRejectedWithoutLeakingDetails(string json)
    {
        var result = Validate(json);
        AssertFailure(result, ValidationErrorCode.InvalidJson, "$");
        Assert.DoesNotContain("secret", result.Error!.ToString());
        Assert.True(Validate(ValidJson).IsValid);
    }

    [Theory]
    [InlineData("id", "\"\\uD800\"")]
    [InlineData("state", "\"\\uDC00\"")]
    [InlineData("pos", "{\"x\":1,\"y\":2,\"\\uD800\":3}")]
    public void InvalidUnicodeEscape_IsContainedAtCommonBoundary(string name, string value) =>
        AssertFailure(Validate(BuildJson(name, value)), ValidationErrorCode.InvalidJson, "$");

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("\"text\"")]
    public void Root_MustBeObject(string json) =>
        AssertFailure(Validate(json), ValidationErrorCode.InvalidType, "$");

    [Theory]
    [InlineData("FF")]
    [InlineData("C080")]
    [InlineData("EAB0")]
    [InlineData("EDA080")]
    public void MalformedUtf8_IsRejected(string hex)
    {
        byte[] payload = [.. Encoding.UTF8.GetBytes(ValidJson), .. Convert.FromHexString(hex)];
        AssertFailure(_validator.Validate(Topic, new(payload), ReceivedAt), ValidationErrorCode.InvalidUtf8, "$");
        Assert.True(Validate(ValidJson).IsValid);
    }

    [Fact]
    public void SizeAndDepthLimits_AreAppliedBeforeFieldValidation()
    {
        Assert.True(Validate(ValidJson.PadRight(65_536)).IsValid);
        AssertFailure(_validator.Validate(null, new(new byte[65_537]), ReceivedAt), ValidationErrorCode.PayloadTooLarge, "$");
        string id = new('가', 22_000);
        string large = ValidJson.Replace("AMR_001", id);
        Assert.True(large.Length < 65_536);
        AssertFailure(_validator.Validate($"amr/{id}/telemetry", Bytes(large), ReceivedAt), ValidationErrorCode.PayloadTooLarge, "$");
        string AtDepth(int depth) => ValidJson[..^1] + ",\"extra\":" + new string('[', depth - 1) + "0" + new string(']', depth - 1) + "}";
        AssertFailure(Validate(AtDepth(16)), ValidationErrorCode.UnexpectedField, "$");
        AssertFailure(Validate(AtDepth(17)), ValidationErrorCode.InvalidJson, "$");
        Assert.True(Validate(ValidJson).IsValid);
    }

    [Fact]
    public void FullPayload_IsUsedBeyondDiagnosticPreview()
    {
        Assert.True(Validate(new string(' ', 300) + ValidJson).IsValid);
        AssertFailure(Validate(ValidJson.PadRight(300) + "garbage"), ValidationErrorCode.InvalidJson, "$");
    }

    [Fact]
    public void SegmentedPayload_CanSplitUnicodeAndNumberTokens()
    {
        string json = ValidJson.Replace("AMR_001", "장비");
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        var first = new Segment(bytes.AsMemory(0, 8));
        var second = first.Append(bytes.AsMemory(8, 17));
        var last = second.Append(bytes.AsMemory(25));
        var result = _validator.Validate("amr/장비/telemetry", new(first, 0, last, last.Memory.Length), ReceivedAt);
        Assert.True(result.IsValid);
        Assert.Equal("장비", result.Message!.Id);
        Assert.Equal(1791195767000, result.Message.Timestamp);
        Assert.Equal(new TelemetryPosition(1.2, -3.4), result.Message.Position);
    }

    private static Dictionary<string, string> ValidFields() => new()
    {
        ["id"] = "\"AMR_001\"", ["ts"] = "1791195767000", ["pos"] = "{\"x\":1.2,\"y\":-3.4}",
        ["sp"] = "0.5", ["state"] = "\"IDLE\""
    };

    private static string BuildJson(string name, string value, string? secondName = null, string? secondValue = null)
    {
        var fields = ValidFields();
        fields[name] = value;
        if (secondName is not null) fields[secondName] = secondValue!;
        return SerializeFields(fields);
    }

    private static string SerializeFields(Dictionary<string, string> fields) =>
        "{" + string.Join(",", fields.Select(pair => $"\"{pair.Key}\":{pair.Value}")) + "}";
    private static ReadOnlySequence<byte> Bytes(string json) => new(Encoding.UTF8.GetBytes(json));
    private TelemetryValidationResult Validate(string json) => _validator.Validate(Topic, Bytes(json), ReceivedAt);

    private static void AssertFailure(TelemetryValidationResult result, ValidationErrorCode code, string field)
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
