using System.Buffers;
using System.Text;
using Amr.Communication.Models;
using Amr.Communication.Validation;
using MQTTnet;
using MQTTnet.Protocol;

namespace Amr.Communication.Mqtt;

/// <summary>한 연결에서 Heartbeat를 한 번 구독하고, 검사 결과를 콜백에 전달한다.</summary>
/// <remarks>
/// Client 접속 후 <see cref="SubscribeAsync"/>를 기다리고, 종료할 때 <see cref="StopAsync"/>를 기다린다.
/// 구독 시도가 실패하거나 취소된 경우에도 StopAsync로 정리한다. StopAsync는 Dispose까지 수행한다.
/// 구독 요청을 시작한 객체는 재사용하지 않으며, 수명주기 메서드는 동시에 호출하지 않는다.
/// Client의 소유자는 MqttConnection이다. 구독 정리가 끝난 뒤 소유자가 연결을 해제하고 Client를 정리한다.
/// </remarks>
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

    // 수신 처리와 종료가 겹치지 않도록 보호한다. 모든 공개 메서드의 동시 호출을 보장하는 잠금은 아니다.
    private readonly object _gate = new();

    private bool _started; // 구독 시도 여부. 실패·취소 후에도 유지한다.

    private bool _subscribed; // 구독 승인 후 수신을 허용하는 동안만 true다.

    private bool _disposed;

    /// <summary>수신 이벤트에 연결한다. Broker 접속과 구독 요청은 별도로 수행한다.</summary>
    /// <remarks>
    /// 콜백은 MQTT 수신 처리 중 같은 잠금 안에서 동기 실행된다. 짧은 동기 처리로 작성하고,
    /// async 람다를 전달하거나 콜백에서 SubscribeAsync·StopAsync·Dispose를 호출하지 않는다.
    /// 긴 처리는 다음 수신과 종료를 지연시킨다.
    /// diagnostic은 received 또는 rejected보다 먼저 실행되므로 예외가 나면 해당 전달도 수행되지 않는다.
    /// received의 예외만 receiverFailed로 보고한다. diagnostic·rejected·receiverFailed의 예외는
    /// MQTT 수신 콜백 밖으로 전파되므로 이 세 콜백은 자체 오류를 처리해야 한다.
    /// </remarks>
    /// <param name="client">MqttConnection이 소유하는 Client. 이 클래스는 Client를 폐기하지 않는다.</param>
    /// <param name="received">검사를 통과한 모델을 한 번 전달받는 동기 콜백. 실패 시 재전달하지 않는다.</param>
    /// <param name="rejected">입력 검사 실패를 전달받는 동기 콜백. 정상 수신자는 호출되지 않는다.</param>
    /// <param name="receiverFailed">received가 던진 예외의 형식 이름을 전달받는 동기 콜백.</param>
    /// <param name="diagnostic">검사 결과와 수신 정보를 전달받는 선택적 동기 진단 콜백.</param>
    /// <param name="showPayload">정상 입력만 최대 256바이트 미리보기를 제공한다. 비밀정보 없는 시험 입력에 사용한다.</param>
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

    /// <summary>접속된 Client로 Heartbeat를 구독하고 QoS 0 승인을 기다린다.</summary>
    /// <remarks>
    /// 연결 확인을 통과해 구독 시도를 시작하면 실패·취소 후에도 같은 객체로 재시도할 수 없다.
    /// 구독 승인을 확인하기 전에는 수신 콜백을 전달하지 않는다. 실패·취소 시에도 StopAsync로 정리한다.
    /// </remarks>
    public async Task SubscribeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started || !_client.IsConnected)
        {
            throw new InvalidOperationException("접속 후 한 번만 Heartbeat를 구독할 수 있습니다.");
        }
        _started = true;

        // 이번 실습의 Heartbeat 계약에 따라 QoS 0을 사용한다.
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

        // 구독 승인을 확인한 뒤부터 수신을 허용한다.
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _subscribed = true;
        }
    }

    /// <summary>수신을 차단하고 필요한 구독 해제를 기다린 뒤 객체를 정리한다.</summary>
    /// <remarks>
    /// SubscribeAsync가 끝난 뒤 호출한다. 구독 해제가 실패해도 Dispose로 이벤트 연결을 제거한다.
    /// 해제 요청의 3초 제한에는 실행 중인 콜백이 잠금을 반환할 때까지 기다리는 시간이 포함되지 않는다.
    /// 완료 후 재호출은 가능하지만 구독을 다시 시작할 수는 없다. Client 연결 해제는 소유자가 수행한다.
    /// </remarks>
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

    /// <summary>로컬 수신 이벤트 연결을 제거한다. 반복 호출해도 된다.</summary>
    /// <remarks>
    /// Broker 구독 해제나 Client 연결 해제·폐기는 수행하지 않는다.
    /// 정상 종료에서는 먼저 StopAsync를 기다린다. using만으로는 구독 해제 요청을 보내지 않는다.
    /// </remarks>
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
