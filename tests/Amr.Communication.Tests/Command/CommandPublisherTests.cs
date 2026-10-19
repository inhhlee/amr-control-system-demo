using System.Buffers;
using Amr.Communication.Command;
using Amr.Communication.Validation;
using MQTTnet;
using MQTTnet.Protocol;
using Xunit;
using static Amr.Communication.Tests.CommandValidationTests;

namespace Amr.Communication.Tests;

public sealed class CommandPublisherTests
{
    internal static MqttClientPublishResult Result(MqttClientPublishReasonCode code = MqttClientPublishReasonCode.Success) => new(null, code, null, []);

    [Theory]
    [MemberData(nameof(InvalidInputs), MemberType = typeof(CommandValidationTests))]
    public async Task InvalidInput_NeverCallsPublish(string? topic, byte[] payload, ValidationErrorCode code)
    {
        // topic-only invalid shapes are tested at validator boundary. IDs here include '/' and wildcard injection.
        string? id = topic is not null && topic.StartsWith("amr/") && topic.EndsWith("/command") ? topic[4..^8] : "bad/id";
        var client = new FakeMqttClient { IsConnected = true };
        var error = await Assert.ThrowsAsync<CommandValidationException>(() => new CommandPublisher(client).PublishAsync(id, payload));
        Assert.Equal(code, error.Error.Code);
        Assert.Equal(0, client.PublishCalls);
    }

    [Fact]
    public async Task ValidPublish_PreservesSnapshotIdentityTargetAndDeliveryOptions()
    {
        var client = new FakeMqttClient { IsConnected = true };
        byte[] bytes = Bytes(Stop);
        byte[] expected = (byte[])bytes.Clone();
        var pending = new CommandPublisher(client).PublishAsync("A", bytes);
        await client.PublishEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Array.Fill(bytes, (byte)'x');
        client.PublishResponse.SetResult(Result());
        var publication = await pending;
        Assert.Equal("amr/A/command", publication.Topic);
        Assert.Equal("CMD-preserved", publication.Command.CommandId);
        Assert.Equal(expected, client.PublishedMessage!.Payload.ToArray());
        Assert.Equal(MqttQualityOfServiceLevel.AtMostOnce, client.PublishedMessage.QualityOfServiceLevel);
        Assert.False(client.PublishedMessage.Retain);
        Assert.Equal(1, client.PublishCalls);
        Assert.Equal(0, client.DisposeCalls);
    }

    [Fact]
    public async Task Disconnected_FailsBeforePublish()
    {
        var client = new FakeMqttClient();
        var error = await Assert.ThrowsAsync<CommandPublishException>(() => new CommandPublisher(client).PublishAsync("A", Bytes(Stop)));
        Assert.Equal(CommandPublishFailure.NotConnected, error.Reason);
        Assert.Equal(0, client.PublishCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransportFailureAndRejectedResult_AreNotReportedAsPublication(bool rejected)
    {
        var client = new FakeMqttClient { IsConnected = true };
        if (rejected) client.PublishResponse.SetResult(Result(MqttClientPublishReasonCode.UnspecifiedError));
        else client.PublishResponse.SetException(new IOException("private transport detail"));
        var error = await Assert.ThrowsAsync<CommandPublishException>(() => new CommandPublisher(client).PublishAsync("A", Bytes(Stop)));
        Assert.Equal(rejected ? CommandPublishFailure.Rejected : CommandPublishFailure.TransportFailure, error.Reason);
        Assert.Equal(1, client.PublishCalls);
        Assert.DoesNotContain("private", error.Message);
    }

    [Fact]
    public async Task AlreadyCanceled_NeverPublishes()
    {
        var client = new FakeMqttClient { IsConnected = true };
        using var user = new CancellationTokenSource();
        user.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CommandPublisher(client).PublishAsync("A", Bytes(Stop), user.Token));
        Assert.Equal(0, client.PublishCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CancellationAndTimeout_BoundWaitEvenWhenTransportIgnoresToken(bool cancel, bool lateFault)
    {
        var client = new FakeMqttClient { IsConnected = true, IgnorePublishCancellation = true };
        using var user = new CancellationTokenSource();
        var pending = new CommandPublisher(client, TimeSpan.FromMilliseconds(cancel ? 3000 : 80)).PublishAsync("A", Bytes(Stop), user.Token);
        await client.PublishEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (cancel) user.Cancel();
        if (cancel)
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(user.Token, error.CancellationToken);
        }
        else
        {
            var error = await Assert.ThrowsAsync<CommandPublishException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(CommandPublishFailure.Timeout, error.Reason);
        }
        // Completing the transport later must not turn the caller's terminal result into a success or retry.
        if (lateFault) client.PublishResponse.SetException(new IOException("late"));
        else client.PublishResponse.SetResult(Result());
        Assert.Equal(1, client.PublishCalls);
        Assert.Equal(0, client.DisposeCalls);
    }
}
