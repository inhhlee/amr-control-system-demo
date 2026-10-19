using System.Buffers;
using System.Text;
using Amr.Communication.Models;
using Amr.Communication.Validation;
using MQTTnet;
using MQTTnet.Protocol;

namespace Amr.Communication.Mqtt;

// 한 연결에서 Subscribe -> Stop -> Dispose 순서로 사용한다. Client의 소유자는 MqttConnection이다.
public sealed class HeartbeatSubscription : IDisposable
{
    public const string TopicFilter = "amr/+/heartbeat";
    public const int PayloadPreviewLimit = 256;
    private readonly IMqttClient _client;
    private readonly HeartbeatValidator _validator = new();
    private readonly Action<HeartbeatMessage> _received;
    private readonly Action<ValidationError> _rejected;
    private readonly Action<string> _receiverFailed;
    private readonly Action<HeartbeatDiagnostic>? _diagnostic;
    private readonly bool _showPayload;

    // _gate를 사용하여 임계구역 설정
    private readonly object _gate = new();

    //
    private bool _started;

    //
    private bool _subscribed;

    //
    private bool _disposed;

    public HeartbeatSubscription(
        IMqttClient client,
        Action<HeartbeatMessage> received,
        Action<ValidationError> rejected,
        Action<string> receiverFailed,
        Action<HeartbeatDiagnostic>? diagnostic = null,
        bool showPayload = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(received);
        ArgumentNullException.ThrowIfNull(rejected);
        ArgumentNullException.ThrowIfNull(receiverFailed);
        _client = client;
        _received = received;
        _rejected = rejected;
        _receiverFailed = receiverFailed;
        _diagnostic = diagnostic;
        _showPayload = showPayload;
        _client.ApplicationMessageReceivedAsync += OnMessageAsync;
    }

    // Mqtt client를 사용해 Heartbeat를 구독하여 구독하는 비동기 메서드
    public async Task SubscribeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started || !_client.IsConnected)
        {
            throw new InvalidOperationException("접속 후 한 번만 Heartbeat를 구독할 수 있습니다.");
        }
        _started = true;

        // QoS 0으로 구독한다. QoS 1 이상은 Heartbeat의 특성상 불필요하다.
        var options = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(filter => filter.WithTopic(TopicFilter).WithAtMostOnceQoS()
                .WithRetainHandling(MqttRetainHandling.DoNotSendOnSubscribe))
            .Build();

        // 구독 승인까지 3초를 기다린다. 승인되지 않으면 예외를 던진다.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        // 구독 요청을 보내고 승인 결과를 확인한다.
        var result = await _client.SubscribeAsync(options, timeout.Token);
        if (result.Items.Count != 1 || result.Items.Single().ResultCode != MqttClientSubscribeResultCode.GrantedQoS0)
        {
            throw new InvalidOperationException("Heartbeat 구독이 QoS 0으로 승인되지 않았습니다.");
        }

        // lock을 사용하여 _subscribed 상태를 안전하게 변경한다.
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _subscribed = true;
        }
    }

    // Heartbeat 구독을 중지하는 비동기 메서드
    public async Task StopAsync()
    {
        bool subscribed;
        lock (_gate)
        {
            subscribed = _subscribed;
            _subscribed = false;
        }
        try
        {
            if (subscribed && _client.IsConnected)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var options = new MqttClientUnsubscribeOptionsBuilder().WithTopicFilter(TopicFilter).Build();
                var result = await _client.UnsubscribeAsync(options, timeout.Token);

                if (result.Items.Count != 1 || result.Items.Single().ResultCode is not
                    (MqttClientUnsubscribeResultCode.Success or MqttClientUnsubscribeResultCode.NoSubscriptionExisted))
                {
                    throw new InvalidOperationException("Heartbeat 구독 해제가 승인되지 않았습니다.");
                }
            }
        }
        finally
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _subscribed = false;
            _client.ApplicationMessageReceivedAsync -= OnMessageAsync;
        }
    }

    private Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var receivedAt = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            var message = args.ApplicationMessage;
            if (!_subscribed || _disposed ||
                MqttTopicFilterComparer.Compare(message.Topic, TopicFilter) != MqttTopicFilterCompareResult.IsMatch)
            {
                return Task.CompletedTask;
            }

            // 미리보기와 독립적으로 전체 바이트를 검사한다. 검사기는 크기를 먼저 확인한다.
            var result = _validator.Validate(message.Topic, message.Payload, receivedAt);
            long length = message.Payload.Length;
            string? preview = result.IsValid && _showPayload && _diagnostic is not null
                ? Encoding.UTF8.GetString(message.Payload.Slice(0, Math.Min(length, PayloadPreviewLimit)).ToArray())
                : null;
            _diagnostic?.Invoke(new HeartbeatDiagnostic(receivedAt, message.Topic, length, preview,
                preview is not null && length > PayloadPreviewLimit, result.IsValid));

            if (!result.IsValid)
            {
                _rejected(result.Error!);
                return Task.CompletedTask;
            }

            try
            {
                _received(result.Message!);
            }
            catch (Exception exception)
            {
                // Core 대역의 처리 오류는 입력 거부로 기록하거나 재전달하지 않는다.
                // 예외 메시지는 Payload·비밀정보를 포함할 수 있으므로 형식 이름만 전달한다.
                _receiverFailed(exception.GetType().Name);
            }
        }
        return Task.CompletedTask;
    }
}
