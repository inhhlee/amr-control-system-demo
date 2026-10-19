namespace Amr.Communication.Mqtt;

public sealed class MqttConnectionSettings
{
    public MqttConnectionSettings(string host = "127.0.0.1", int port = 1884, int connectTimeoutSeconds = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port < 1 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "포트는 1~65535여야 합니다.");
        }

        // CancellationTokenSource에 밀리초로 전달할 수 있는 양수 범위다.
        if (connectTimeoutSeconds < 1 || connectTimeoutSeconds > int.MaxValue / 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(connectTimeoutSeconds), "접속 대기 시간 범위를 확인하세요.");
        }

        Host = host;
        Port = port;
        ConnectTimeout = TimeSpan.FromSeconds(connectTimeoutSeconds);
        ClientId = $"amr-demo-{Guid.NewGuid():N}";
    }

    public string Host { get; }
    public int Port { get; }
    public TimeSpan ConnectTimeout { get; }
    public string ClientId { get; }
    public TimeSpan KeepAlive { get; } = TimeSpan.FromSeconds(5);
}
