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

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.ConnectTimeout);

        // 요청 전송만으로 성공을 표시하지 않고 Broker의 MQTT 승인 결과를 기다린다.
        var result = await _client.ConnectAsync(options, timeout.Token);
        if (result.ResultCode != MqttClientConnectResultCode.Success || !_client.IsConnected)
        {
            throw new InvalidOperationException($"MQTT 접속이 승인되지 않았습니다: {result.ResultCode}");
        }

        _approved = true;
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
