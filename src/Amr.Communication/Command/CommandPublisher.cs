using System.Buffers;
using Amr.Communication.Validation;
using MQTTnet;
using MQTTnet.Protocol;

namespace Amr.Communication.Command;

public enum CommandPublishFailure { NotConnected, TransportFailure, Rejected, Timeout }

public sealed class CommandPublishException(CommandPublishFailure reason, Exception? innerException = null)
    : Exception($"Command publish failed: {reason}.", innerException)
{
    public CommandPublishFailure Reason { get; } = reason;
}

/// <summary>MQTTnet의 발행 반환 결과다. Broker 수신·AMR 실행 완료·ACK 수신을 보장하지 않는다.</summary>
public sealed record CommandPublication(string Topic, CommandMessage Command);

/// <summary>Client의 접속·정리는 호출자가 소유한다. 내부 재시도·대기열·ACK 대조는 없다.</summary>
public sealed class CommandPublisher
{
    private readonly IMqttClient _client;
    private readonly TimeSpan _timeout;
    private readonly CommandValidator _validator = new();

    public CommandPublisher(IMqttClient client, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
        if (_timeout.TotalMilliseconds < 1 || _timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        _client = client;
    }

    public async Task<CommandPublication> PublishAsync(string? amrId, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        if (payload.Length > CommandValidator.MaxPayloadBytes)
            throw new CommandValidationException(new(ValidationErrorCode.PayloadTooLarge, "$", "Payload exceeds 65536 bytes."));
        // 검사한 바이트와 실제 발행 바이트가 같도록 호출자가 가진 가변 버퍼를 분리한다.
        byte[] snapshot = payload.ToArray();
        string topic = $"amr/{amrId}/command";
        var validation = _validator.Validate(topic, new ReadOnlySequence<byte>(snapshot));
        if (!validation.IsValid) throw new CommandValidationException(validation.Error!);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_client.IsConnected) throw new CommandPublishException(CommandPublishFailure.NotConnected);
        var message = new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(snapshot)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce).WithRetainFlag(false).Build();

        using var abort = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource();
        int outcome = 0; // 0: pending, 1: user, 2: timeout, 3: returned, 4: failed
        void Cancel(int reason)
        {
            if (Interlocked.CompareExchange(ref outcome, reason, 0) == 0) abort.Cancel();
        }
        using var userRegistration = cancellationToken.Register(() => Cancel(1));
        using var timeoutRegistration = timeout.Token.Register(() => Cancel(2));
        timeout.CancelAfter(_timeout);
        Task<MqttClientPublishResult>? pending = null;
        try
        {
            abort.Token.ThrowIfCancellationRequested();
            pending = _client.PublishAsync(message, abort.Token);
            var result = await pending.WaitAsync(abort.Token).ConfigureAwait(false);
            if (!result.IsSuccess) throw new CommandPublishException(CommandPublishFailure.Rejected);
            if (Interlocked.CompareExchange(ref outcome, 3, 0) != 0)
                throw new OperationCanceledException(abort.Token);
            return new(topic, validation.Message!);
        }
        catch (Exception exception)
        {
            int terminalOutcome = Interlocked.CompareExchange(ref outcome, 4, 0);
            if (pending is not null) _ = ObserveCompletionAsync(pending);
            if (terminalOutcome == 1)
                throw new OperationCanceledException("Command publish canceled by caller; delivery may already have occurred.", exception, cancellationToken);
            if (terminalOutcome == 2) throw new CommandPublishException(CommandPublishFailure.Timeout, exception);
            if (exception is CommandPublishException) throw;
            throw new CommandPublishException(CommandPublishFailure.TransportFailure, exception);
        }
    }

    private static async Task ObserveCompletionAsync(Task<MqttClientPublishResult> pending)
    {
        // 전송 측의 취소 처리가 늦어도 늦은 결과를 정상 발행으로 변경하지 않는다.
        try { await pending.ConfigureAwait(false); }
        catch (Exception) { }
    }
}
