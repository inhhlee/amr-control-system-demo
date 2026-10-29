using System.Buffers;
using System.Text;
using Amr.Communication.Models;
using Amr.Communication.Validation;
using MQTTnet;
using MQTTnet.Protocol;
using Amr.Communication.Monitoring;

namespace Amr.Communication.Mqtt;

/// <summary>네 수신 유형을 구독하고 검사된 모델을 짧은 동기 수신자에 전달한다.</summary>
/// <remarks>
/// 한 Client에는 이 수신부 하나만 연결한다. HeartbeatSubscription을 함께 연결하지 않는다.
/// 접속 → SubscribeAsync 완료 → StopAsync 완료 → 연결 해제·Client 정리 순서로 사용한다.
/// 구독 실패·취소 뒤에도 StopAsync를 호출한다. 수명주기 메서드는 동시에 호출하지 않고 객체를 재사용하지 않는다.
/// Client는 MqttConnection 소유다. 이 클래스의 Dispose는 로컬 이벤트만 제거한다.
/// </remarks>
public sealed class MessageSubscription : IDisposable
{
    public static IReadOnlyList<string> TopicFilters { get; } = Array.AsReadOnly(new[]
    {
        "amr/+/heartbeat", "amr/+/telemetry", "amr/+/alarm", "amr/+/ack"
    });
    public const int PayloadPreviewLimit = 256;
    private readonly IMqttClient _client;
    private readonly HeartbeatValidator _heartbeat = new();
    private readonly TelemetryValidator _telemetry = new();
    private readonly AlarmValidator _alarm = new();
    private readonly AckValidator _ack = new();
    private readonly Action<ReceivedMessage> _received;
    private readonly Action<ValidationError> _rejected;
    private readonly Action<CallbackFailure> _callbackFailed;
    private readonly Action<MessageDiagnostic>? _diagnostic;
    private readonly bool _showPayload;
    private readonly Action<HeartbeatMessage, long>? _normalHeartbeat;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private bool _started;
    private bool _ready;
    private bool _disposed;

    /// <remarks>
    /// 콜백은 수신 처리의 같은 잠금 안에서 동기 실행된다. 짧게 작성하고 async 람다나 수명주기 메서드 재호출을 하지 않는다.
    /// Received·Rejected·Diagnostic 예외는 callbackFailed에 단계·예외 형식만 보고한다. 자동 재전달하지 않는다.
    /// Diagnostic 실패는 정상 전달을 막지 않는다. callbackFailed 자체의 오류는 호출 밖으로 전파되므로 자체 처리해야 한다.
    /// 느린 콜백은 다른 AMR 수신과 종료도 지연시킨다. AMR별 큐·독립성은 COM01-C 범위다.
    /// </remarks>
    public MessageSubscription(IMqttClient client, Action<ReceivedMessage> received,
        Action<ValidationError> rejected, Action<CallbackFailure> callbackFailed,
        Action<MessageDiagnostic>? diagnostic = null, bool showPayload = false,
        Action<HeartbeatMessage, long>? normalHeartbeat = null, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(received);
        ArgumentNullException.ThrowIfNull(rejected);
        ArgumentNullException.ThrowIfNull(callbackFailed);
        _client = client;
        _received = received;
        _rejected = rejected;
        _callbackFailed = callbackFailed;
        _diagnostic = diagnostic;
        _showPayload = showPayload;
        _normalHeartbeat = normalHeartbeat;
        _clock = clock ?? TimeProvider.System;
        _client.ApplicationMessageReceivedAsync += OnMessageAsync;
    }

    public async Task SubscribeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started || !_client.IsConnected)
            throw new InvalidOperationException("접속 후 한 번만 네 유형을 구독할 수 있습니다.");
        _started = true;

        var builder = new MqttClientSubscribeOptionsBuilder();
        foreach (string topic in TopicFilters)
        {
            // 개발 시험 설정: QoS 0, 구독 시 보관 메시지 재전송 안 함. 무손실·exactly-once 보장이 아니다.
            builder.WithTopicFilter(filter => filter.WithTopic(topic).WithAtMostOnceQoS()
                .WithRetainHandling(MqttRetainHandling.DoNotSendOnSubscribe));
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var result = await _client.SubscribeAsync(builder.Build(), timeout.Token);
        if (result.Items.Count != TopicFilters.Count || result.Items.Any(item =>
            item.ResultCode != MqttClientSubscribeResultCode.GrantedQoS0))
            throw new InvalidOperationException("네 Topic 모두 QoS 0으로 승인되어야 합니다.");

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _ready = true;
        }
    }

    /// <summary>수신 차단 후 네 필터를 해제하고 이벤트를 제거한다. 해제 실패는 소유자에게 알린다.</summary>
    /// <remarks>해제의 3초 제한에는 실행 중인 동기 콜백의 잠금 대기 시간이 포함되지 않는다.</remarks>
    public async Task StopAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _ready = false;
        }
        try
        {
            // 취소·일부 거부 시 Broker가 이미 받아들인 필터를 모를 수 있으므로 시도한 네 필터 모두 정리한다.
            if (_started && _client.IsConnected)
            {
                var builder = new MqttClientUnsubscribeOptionsBuilder();
                foreach (string topic in TopicFilters) builder.WithTopicFilter(topic);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var result = await _client.UnsubscribeAsync(builder.Build(), timeout.Token);
                if (result.Items.Count != TopicFilters.Count || result.Items.Any(item => item.ResultCode is not
                    (MqttClientUnsubscribeResultCode.Success or MqttClientUnsubscribeResultCode.NoSubscriptionExisted)))
                    throw new InvalidOperationException("네 Topic 구독 해제가 승인되지 않았습니다.");
            }
        }
        finally
        {
            // 실패해도 이벤트는 반드시 제거. 연결 소유자는 이후 Disconnect/Dispose로 세션을 정리해야 한다.
            Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _ready = false;
            _disposed = true;
            _client.ApplicationMessageReceivedAsync -= OnMessageAsync;
        }
    }

    private Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var receivedAt = _clock.GetUtcNow();
        long receivedTimestamp = _clock.GetTimestamp();
        lock (_gate)
        {
            if (!_ready || _disposed) return Task.CompletedTask;
            var message = args.ApplicationMessage;
            // 구독 밖 Topic은 검사 결과로 오인하지 않고 무시한다. ID의 유효성은 기존 검사기가 판단한다.
            if (!TopicFilters.Any(filter => MqttTopicFilterComparer.Compare(message.Topic, filter)
                == MqttTopicFilterCompareResult.IsMatch)) return Task.CompletedTask;

            string[] parts = message.Topic.Split('/');
            string kind = parts[2];
            var (model, error) = Validate(kind, message.Topic, message.Payload, receivedAt);
            string? preview = model is not null && _showPayload && _diagnostic is not null
                ? Encoding.UTF8.GetString(message.Payload.Slice(0, Math.Min(message.Payload.Length, PayloadPreviewLimit)).ToArray())
                : null;
            if (_diagnostic is not null)
                Invoke(() => _diagnostic(new(receivedAt, message.Topic, message.Payload.Length, preview,
                    preview is not null && message.Payload.Length > PayloadPreviewLimit, model is not null)), "Diagnostic");
            if (error is not null)
                Invoke(() => _rejected(error), "Rejected");
            else
            {
                if (model is HeartbeatMessage heartbeat && _normalHeartbeat is not null)
                    Invoke(() => _normalHeartbeat(heartbeat, receivedTimestamp), "HeartbeatObservation");
                Invoke(() => _received(new(parts[1], kind, model!, receivedAt)), "Received");
            }
        }
        return Task.CompletedTask;
    }

    private (object? Model, ValidationError? Error) Validate(string kind, string topic,
        ReadOnlySequence<byte> payload, DateTimeOffset receivedAt)
    {
        switch (kind)
        {
            case "heartbeat":
                var heartbeat = _heartbeat.Validate(topic, payload, receivedAt);
                return (heartbeat.Message, heartbeat.Error);
            case "telemetry":
                var telemetry = _telemetry.Validate(topic, payload, receivedAt);
                return (telemetry.Message, telemetry.Error);
            case "alarm":
                var alarm = _alarm.Validate(topic, payload, receivedAt);
                return (alarm.Message, alarm.Error);
            case "ack":
                var ack = _ack.Validate(topic, payload, receivedAt);
                return (ack.Message, ack.Error);
            default:
                throw new InvalidOperationException("Unsupported kind after Topic filtering.");
        }
    }

    private void Invoke(Action callback, string stage)
    {
        try { callback(); }
        catch (Exception exception) { _callbackFailed(new(stage, exception.GetType().Name)); }
    }
}
