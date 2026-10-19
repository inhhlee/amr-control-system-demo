using MQTTnet;
using MQTTnet.Formatter;

namespace Amr.Communication.Mqtt;

// 호출자는 접속 -> 해제 -> 정리를 순서대로 기다린다. 동시 호출·자동 재접속은 후속 범위다.
public sealed class MqttConnection : IDisposable
{
    private readonly IMqttClient _client;
    private bool _approved;
    private bool _disposed;

    public MqttConnection() : this(new MqttClientFactory().CreateMqttClient())
    {
    }

    // 실제 MQTT 호출을 시험 대역으로 바꿀 경계. 전달받은 client의 정리도 이 객체가 맡는다.
    public MqttConnection(IMqttClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public bool IsConnected => !_disposed && _approved && _client.IsConnected;

    public async Task ConnectAsync(MqttConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        if (_client.IsConnected)
        {
            throw new InvalidOperationException("이미 접속한 Client입니다.");
        }

        _approved = false;
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(settings.Host, settings.Port)
            .WithClientId(settings.ClientId)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithCleanStart()
            .WithSessionExpiryInterval(0)
            .WithKeepAlivePeriod(settings.KeepAlive)
            .Build();

        using var abort = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource();
        // 두 신호가 근접해도 먼저 관찰한 종료 원인을 유지한다.
        int outcome = 0; // 0: 대기, 1: 사용자, 2: 대기 제한, 3: 승인, 4: 실패
        void Cancel(int reason)
        {
            if (Interlocked.CompareExchange(ref outcome, reason, 0) == 0) abort.Cancel();
        }
        using var userRegistration = cancellationToken.Register(() => Cancel(1));
        using var timeoutRegistration = timeout.Token.Register(() => Cancel(2));
        timeout.CancelAfter(settings.ConnectTimeout);

        Task<MqttClientConnectResult>? pending = null;
        try
        {
            abort.Token.ThrowIfCancellationRequested();
            pending = _client.ConnectAsync(options, abort.Token);
            // 대역/라이브러리가 취소를 늦게 처리해도 호출자의 대기는 끝낸다.
            var result = await pending.WaitAsync(abort.Token);
            abort.Token.ThrowIfCancellationRequested();
            if (result.ResultCode != MqttClientConnectResultCode.Success)
                throw new MqttConnectionException(MqttConnectionFailure.MqttRejected, result.ResultCode);
            if (!_client.IsConnected)
                throw new MqttConnectionException(MqttConnectionFailure.TransportFailure);
            // 최종 검사와 승인 사이에 취소가 들어와도 이미 결정한 결과를 덮어쓰지 않는다.
            if (Interlocked.CompareExchange(ref outcome, 3, 0) != 0)
                throw new OperationCanceledException(abort.Token);
            _approved = true;
        }
        catch (Exception exception)
        {
            int terminalOutcome = Interlocked.CompareExchange(ref outcome, 4, 0);
            // 실패한 인스턴스는 재사용하지 않는다. Dispose가 소켓을 닫고 늦은 승인을 격리한다.
            Dispose();
            if (pending is not null) _ = ObserveCompletionAsync(pending);
            if (terminalOutcome == 1)
                throw new OperationCanceledException("MQTT 접속을 사용자가 취소했습니다.", exception, cancellationToken);
            if (terminalOutcome == 2)
                throw new MqttConnectionException(MqttConnectionFailure.Timeout, innerException: exception);
            if (exception is MqttConnectionException) throw;
            throw new MqttConnectionException(MqttConnectionFailure.TransportFailure, innerException: exception);
        }
    }

    private static async Task ObserveCompletionAsync(Task<MqttClientConnectResult> pending)
    {
        // 이미 확정한 실패/취소를 변경하지 않으며, 늦은 fault도 관찰한다.
        try { await pending; }
        catch (Exception) { }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        if (_client.IsConnected)
        {
            // Dispose로 소켓만 닫기 전에 정상 종료 DISCONNECT를 보낸다.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var options = new MqttClientDisconnectOptionsBuilder()
                .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                .Build();
            await _client.DisconnectAsync(options, timeout.Token);
        }

        _approved = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _approved = false;
        _client.Dispose();
    }
}
