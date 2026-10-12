using Amr.Communication.Mqtt;
using MQTTnet;
using System.Text.Encodings.Web;
using System.Text.Json;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);

if (args.Length == 1 && args[0] == "--help")
{
    Console.WriteLine("사용법: --host 127.0.0.1 --port 1884 --connect-timeout-seconds 3 [--show-payload]");
    Console.WriteLine("Heartbeat 구독 후 Ctrl+C까지 유지합니다. --show-payload는 비밀정보 없는 시험 입력 전용이며 최대 256바이트를 표시합니다.");
    return 0;
}

int exitCode = 0;
MqttConnection? connection = null;
HeartbeatSubscription? subscription = null;
using var shutdown = new CancellationTokenSource();

ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    // 즉시 프로세스를 종료하는 기본 동작을 막고, 실행 흐름에 종료를 요청한다.
    eventArgs.Cancel = true;
    shutdown.Cancel();
};
Console.CancelKeyPress += cancelHandler;

try
{
    var (settings, showPayload) = ReadSettings(args);
    Report($"Settings Host={settings.Host} Port={settings.Port} ClientId={settings.ClientId} " +
        $"MQTT=5 CleanStart=true SessionExpiry=0 KeepAliveSeconds={settings.KeepAlive.TotalSeconds} " +
        $"ConnectTimeoutSeconds={settings.ConnectTimeout.TotalSeconds} DisconnectTimeoutSeconds=3");

    var client = new MqttClientFactory().CreateMqttClient();
    connection = new MqttConnection(client);
    subscription = new HeartbeatSubscription(client, ReportHeartbeat, showPayload);
    Report("Connecting");
    await connection.ConnectAsync(settings, shutdown.Token);
    Report("Connected");
    Report($"Subscribing TopicFilter={HeartbeatSubscription.TopicFilter} QoS=0 PayloadPreview={showPayload} LimitBytes=256");
    await subscription.SubscribeAsync(shutdown.Token);
    Report($"Subscribed TopicFilter={HeartbeatSubscription.TopicFilter} QoS=0");
    Report("WaitingForShutdown: Ctrl+C로 정상 종료합니다.");
    await Task.Delay(Timeout.Infinite, shutdown.Token);
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    Report("ShutdownRequested");
}
catch (Exception exception)
{
    Console.Error.WriteLine($"{DateTimeOffset.UtcNow:O} Error {exception.GetType().Name}: {exception.Message}");
    exitCode = 1;
}
finally
{
    try
    {
        if (subscription is not null)
        {
            try
            {
                Report("Unsubscribing");
                await subscription.StopAsync();
                Report("SubscriptionStopped");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"{DateTimeOffset.UtcNow:O} SubscriptionCleanupError {exception.GetType().Name}: {exception.Message}");
                exitCode = 1;
            }
        }
        if (connection is not null)
        {
            try
            {
                if (connection.IsConnected)
                {
                    Report("Disconnecting");
                    // 이미 취소된 shutdown.Token을 넘기면 해제까지 취소되므로 새 대기로 정리한다.
                    await connection.DisconnectAsync();
                    Report("Disconnected");
                }
            }
            finally
            {
                connection.Dispose();
                Report("Disposed");
            }
        }
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"{DateTimeOffset.UtcNow:O} CleanupError {exception.GetType().Name}: {exception.Message}");
        exitCode = 1;
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }
}

Report($"ExitCode={exitCode}");
return exitCode;

static void Report(string message)
{
    Console.WriteLine($"{DateTimeOffset.UtcNow:O} {message}");
}

static void ReportHeartbeat(HeartbeatDiagnostic diagnostic)
{
    // 제어문자·개행은 이스케이프해 한 메시지가 다른 로그 줄처럼 보이지 않게 한다.
    var jsonOptions = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    string topic = JsonSerializer.Serialize(diagnostic.Topic[..Math.Min(diagnostic.Topic.Length, 256)], jsonOptions);
    string payload = diagnostic.PayloadPreview is null ? "<hidden>" : JsonSerializer.Serialize(diagnostic.PayloadPreview, jsonOptions);
    Console.WriteLine($"{diagnostic.ReceivedAtUtc:O} HeartbeatDiagnostic Topic={topic} " +
        $"TopicTruncated={diagnostic.Topic.Length > 256} PayloadBytes={diagnostic.PayloadBytes} " +
        $"Payload={payload} PayloadTruncated={diagnostic.PayloadTruncated} Validation=NotPerformed");
}

static (MqttConnectionSettings Settings, bool ShowPayload) ReadSettings(string[] arguments)
{
    string host = "127.0.0.1";
    int port = 1884;
    int connectTimeoutSeconds = 3;
    bool showPayload = false;

    for (int index = 0; index < arguments.Length; index++)
    {
        if (arguments[index] == "--show-payload")
        {
            showPayload = true;
            continue;
        }
        if (index + 1 >= arguments.Length)
        {
            throw new ArgumentException($"인자 값이 필요합니다: {arguments[index]}");
        }

        string value = arguments[index + 1];
        switch (arguments[index])
        {
            case "--host":
                host = value;
                break;
            case "--port":
                port = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "--connect-timeout-seconds":
                connectTimeoutSeconds = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            default:
                throw new ArgumentException($"알 수 없는 인자입니다: {arguments[index]}");
        }
        index++;
    }

    return (new MqttConnectionSettings(host, port, connectTimeoutSeconds), showPayload);
}
