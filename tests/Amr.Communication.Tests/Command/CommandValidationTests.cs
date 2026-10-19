using System.Buffers;
using System.Text;
using Amr.Communication.Command;
using Amr.Communication.Validation;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class CommandValidationTests
{
    internal const string Stop = "{\"cmdId\":\"CMD-preserved\",\"type\":\"STOP\",\"ts\":2208988800123,\"param\":{}}";
    internal static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    [Theory]
    [InlineData("MOVE", -30.25, 15.5)]
    [InlineData("STOP", null, null)]
    [InlineData("RESET", null, null)]
    public void ValidCommands_RoundTripWithoutChangingFields(string type, double? x, double? y)
    {
        var message = new CommandMessage("CMD-요청", type, long.MinValue, new(x, y));
        var result = new CommandValidator().Validate("amr/AMR_001/command", new(message.ToPayload()));
        Assert.True(result.IsValid);
        Assert.Null(result.Error);
        Assert.Equal(message, result.Message);
    }

    public static IEnumerable<object[]> InvalidInputs()
    {
        foreach (var topic in new string?[] { null, "", "amr//command", "amr/A B/command", "amr/A\nB/command",
            "amr/A/B/command", "amr/+/command", "amr/#/command", "amr/A/cmd", "amr/A/command/", "amr/A\0B/command", "amr/" + new string('a', 65524) + "/command" })
            yield return [topic!, Bytes(Stop), ValidationErrorCode.InvalidTopic];
        foreach (var (before, after, code) in new[]
        {
            ("\"cmdId\":\"CMD-preserved\",", "", ValidationErrorCode.MissingField),
            ("\"CMD-preserved\"", "\" \"", ValidationErrorCode.InvalidValue),
            ("\"CMD-preserved\"", "null", ValidationErrorCode.InvalidType),
            ("\"CMD-preserved\"", "7", ValidationErrorCode.InvalidType),
            ("\"CMD-preserved\"", "\"\\uD800\"", ValidationErrorCode.InvalidJson),
            ("\"type\":\"STOP\",", "", ValidationErrorCode.MissingField),
            ("\"STOP\"", "\"stop\"", ValidationErrorCode.InvalidValue),
            ("\"STOP\"", "\"PAUSE\"", ValidationErrorCode.InvalidValue),
            ("\"STOP\"", "false", ValidationErrorCode.InvalidType),
            ("\"ts\":2208988800123,", "", ValidationErrorCode.MissingField),
            ("2208988800123", "1.0", ValidationErrorCode.InvalidType),
            ("2208988800123", "1e3", ValidationErrorCode.InvalidType),
            ("2208988800123", "\"1\"", ValidationErrorCode.InvalidType),
            ("2208988800123", "9223372036854775808", ValidationErrorCode.OutOfRange),
            ("2208988800123", "-9223372036854775809", ValidationErrorCode.OutOfRange),
            (",\"param\":{}", "", ValidationErrorCode.MissingField),
            ("\"param\":{}", "\"param\":[]", ValidationErrorCode.InvalidType),
            ("\"param\":{}", "\"param\":null", ValidationErrorCode.InvalidType),
            ("\"param\":{}", "\"param\":{\"x\":1}", ValidationErrorCode.UnexpectedField),
            ("\"param\":{}", "\"param\":{},\"other\":1", ValidationErrorCode.UnexpectedField),
            ("\"param\":{}", "\"param\":{},\"cmdId\":\"second\"", ValidationErrorCode.DuplicateField),
            ("\"param\":{}", "\"param\":{},\"type\":\"RESET\"", ValidationErrorCode.DuplicateField),
            ("\"param\":{}", "\"param\":{},\"ts\":1", ValidationErrorCode.DuplicateField),
            ("\"param\":{}", "\"param\":{},\"param\":{}", ValidationErrorCode.DuplicateField),
        }) yield return ["amr/A/command", Bytes(Stop.Replace(before, after)), code];
        foreach (var (param, code) in new[]
        {
            ("{}", ValidationErrorCode.MissingField),
            ("{\"x\":1}", ValidationErrorCode.MissingField),
            ("{\"y\":1}", ValidationErrorCode.MissingField),
            ("{\"x\":\"1\",\"y\":1}", ValidationErrorCode.InvalidType),
            ("{\"x\":1,\"y\":false}", ValidationErrorCode.InvalidType),
            ("{\"x\":1e309,\"y\":1}", ValidationErrorCode.OutOfRange),
            ("{\"x\":1,\"y\":-1e309}", ValidationErrorCode.OutOfRange),
            ("{\"x\":1,\"y\":2,\"x\":3}", ValidationErrorCode.DuplicateField),
            ("{\"x\":1,\"y\":2,\"z\":3}", ValidationErrorCode.UnexpectedField),
        }) yield return ["amr/A/command", Bytes(Stop.Replace("STOP", "MOVE").Replace("{}", param)), code];
        foreach (var json in new[] { "", "{", Stop + "{}", Stop.Insert(1, "/* comment */"), Stop.Insert(Stop.Length - 1, ","),
            "{\"nested\":" + new string('[', 16) + "0" + new string(']', 16) + "}" })
            yield return ["amr/A/command", Bytes(json), ValidationErrorCode.InvalidJson];
        foreach (var json in new[] { "[]", "null", "123", "\"string\"" })
            yield return ["amr/A/command", Bytes(json), ValidationErrorCode.InvalidType];
        yield return ["amr/A/command", new byte[] { 0xff }, ValidationErrorCode.InvalidUtf8];
        yield return ["amr/A/command", new byte[65537], ValidationErrorCode.PayloadTooLarge];
        yield return ["amr/A/command", Bytes(Stop.Replace("STOP", "RESET").Replace("{}", "{\"x\":1,\"y\":2}")), ValidationErrorCode.UnexpectedField];
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public void InvalidWireInput_IsRejected(string? topic, byte[] payload, ValidationErrorCode code)
    {
        var result = new CommandValidator().Validate(topic, new(payload));
        Assert.False(result.IsValid);
        Assert.Null(result.Message);
        Assert.Equal(code, result.Error!.Code);
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(0)]
    public void SignedTimestamp_IsPreserved(long timestamp)
    {
        var message = new CommandMessage("id", "RESET", timestamp, new());
        Assert.Equal(timestamp, new CommandValidator().Validate("amr/A/command", new(message.ToPayload())).Message!.Timestamp);
    }

    [Fact]
    public void ExactByteLimit_IsAccepted()
    {
        string json = Stop.Replace("CMD-preserved", new string('a', 65536 - Bytes(Stop).Length + "CMD-preserved".Length));
        Assert.Equal(65536, Bytes(json).Length);
        Assert.True(new CommandValidator().Validate("amr/A/command", new(Bytes(json))).IsValid);
    }

    [Fact]
    public async Task UnpairedSurrogateTopic_IsRejectedBeforePublish()
    {
        // xUnit Theory serialization normalizes an unpaired surrogate; construct it inside this test.
        string id = new((char)0xd800, 1);
        var result = new CommandValidator().Validate($"amr/{id}/command", new(Bytes(Stop)));
        Assert.Equal(ValidationErrorCode.InvalidTopic, result.Error!.Code);
        var client = new FakeMqttClient { IsConnected = true };
        var error = await Assert.ThrowsAsync<CommandValidationException>(() => new CommandPublisher(client).PublishAsync(id, Bytes(Stop)));
        Assert.Equal(ValidationErrorCode.InvalidTopic, error.Error.Code);
        Assert.Equal(0, client.PublishCalls);
    }
}
