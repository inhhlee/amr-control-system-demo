using Amr.Communication.Mqtt;
using Amr.Communication.Models;
using Amr.Communication.Validation;
using MQTTnet;
using System.Text.Encodings.Web;
using System.Text.Json;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);

if (args.Length == 1 && args[0] == "--help")
{
    Console.WriteLine("사용법: --host 127.0.0.1 --port 1884 --connect-timeout-seconds 3 [--show-payload]");
    Console.WriteLine("Heartbeat·Telemetry·Alarm·ACK 검사 후 대상 AMR과 정상 모델을 전달합니다. --show-payload는 비밀정보 없는 정상 시험 입력의 앞 256바이트만 표시합니다. Ctrl+C로 종료합니다.");
    Console.WriteLine("순서·독립성 시험: --test-delay-amr AMR_001 --test-delay-ms 5000 (해당 AMR의 처리만 지연)");
    return 0;
}

int exitCode = 0;
MqttConnection? connection = null;
MessageSubscription? subscription = null;
AmrReceiveQueue? queue = null;
IMqttClient? client = null;
Func<MqttClientDisconnectedEventArgs, Task>? disconnectedHandler = null;
TimeSpan queueStopTimeout = TimeSpan.FromSeconds(3);
using var shutdown = new CancellationTokenSource();

ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    // 즉시 프로세스를 종료하는 기본 동작을 막고, 실행 흐름에 종료를 요청한다.
    eventArgs.Cancel = true;
    _ = queue?.RequestStop();
    shutdown.Cancel();
};
Console.CancelKeyPress += cancelHandler;

try
{
    var (settings, showPayload, delayAmr, delayMs) = ReadSettings(args);
    queueStopTimeout = settings.ConnectTimeout;
    Report($"Settings Host={settings.Host} Port={settings.Port} ClientId={settings.ClientId} " +
        $"MQTT=5 CleanStart=true SessionExpiry=0 KeepAliveSeconds={settings.KeepAlive.TotalSeconds} " +
        $"ConnectTimeoutSeconds={settings.ConnectTimeout.TotalSeconds} DisconnectTimeoutSeconds=3");

    client = new MqttClientFactory().CreateMqttClient();
    connection = new MqttConnection(client);
    queue = new AmrReceiveQueue(async (message, token) =>
    {
        Report($"ProcessingStarted AmrId={JsonSerializer.Serialize(message.AmrId)} Kind={message.Kind}");
        if (message.AmrId == delayAmr && delayMs > 0) await Task.Delay(delayMs, token);
        token.ThrowIfCancellationRequested();
        ReceiveMessage(message);
    }, entry => Report($"QueueEvent {JsonSerializer.Serialize(entry)}"));
    disconnectedHandler = eventArgs =>
    {
        if (!shutdown.IsCancellationRequested)
        {
            _ = queue.RequestStop();
            Report("ConnectionLost");
            Interlocked.Exchange(ref exitCode, 1);
            shutdown.Cancel();
        }
        return Task.CompletedTask;
    };
    client.DisconnectedAsync += disconnectedHandler;
    subscription = new MessageSubscription(client, message =>
    {
        var result = queue.TryEnqueue(message);
        Report($"QueueAdmission Result={result} AmrId={JsonSerializer.Serialize(message.AmrId)} Kind={message.Kind}");
    }, ReportRejection,
        error => Report($"CallbackError Stage={error.Stage} ExceptionType={error.ExceptionType}"), ReportDiagnostic, showPayload);
    Report("Connecting");
    await connection.ConnectAsync(settings, shutdown.Token);
    Report("Connected");
    Report($"QueueReady CapacityPerAmr=256 AmrLimit=64 StopTimeoutSeconds={queueStopTimeout.TotalSeconds} TestDelayAmr={JsonSerializer.Serialize(delayAmr)} TestDelayMs={delayMs}");
    Report($"Subscribing TopicFilters={string.Join(',', MessageSubscription.TopicFilters)} QoS=0 PayloadPreview={showPayload} LimitBytes=256");
    await subscription.SubscribeAsync(shutdown.Token);
    Report($"Subscribed TopicFilters={string.Join(',', MessageSubscription.TopicFilters)} QoS=0");
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
    // 먼저 큐 접수를 차단한다. 구독 해제 응답을 기다리는 동안에도 새 처리를 시작하지 않는다.
    if (queue is not null)
    {
        try { await queue.StopAsync(queueStopTimeout); Report("QueueStopped"); }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"QueueCleanupError ExceptionType={exception.GetType().Name}");
            exitCode = 1;
        }
    }
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
                if (client is not null && disconnectedHandler is not null) client.DisconnectedAsync -= disconnectedHandler;
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

static void ReportDiagnostic(MessageDiagnostic diagnostic)
{
    // 제어문자·개행은 이스케이프해 한 메시지가 다른 로그 줄처럼 보이지 않게 한다.
    var jsonOptions = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    string topic = JsonSerializer.Serialize(diagnostic.Topic[..Math.Min(diagnostic.Topic.Length, 256)], jsonOptions);
    string payload = diagnostic.PayloadPreview is null ? "<hidden>" : JsonSerializer.Serialize(diagnostic.PayloadPreview, jsonOptions);
    Console.WriteLine($"{diagnostic.ReceivedAtUtc:O} MessageDiagnostic Topic={topic} " +
        $"TopicTruncated={diagnostic.Topic.Length > 256} PayloadBytes={diagnostic.PayloadBytes} " +
        $"Payload={payload} PayloadTruncated={diagnostic.PayloadTruncated} Validation={(diagnostic.IsValid ? "Accepted" : "Rejected")}");
}

static void ReceiveMessage(ReceivedMessage message)
{
    // 실제 Core 업무 처리 대신 정상 모델 전달을 관찰하는 시험용 수신자다.
    // object Model의 실제 수신 유형별 필드를 출력하며 제어문자는 JSON 이스케이프한다.
    Report($"Received {JsonSerializer.Serialize(message)}");
}

static void ReportRejection(ValidationError error) =>
    Report($"Rejected Code={error.Code} Field={error.Field} Reason={error.Reason}");

static (MqttConnectionSettings Settings, bool ShowPayload, string? DelayAmr, int DelayMs) ReadSettings(string[] arguments)
{
    string host = "127.0.0.1";
    int port = 1884;
    int connectTimeoutSeconds = 3;
    bool showPayload = false;
    string? delayAmr = null;
    int delayMs = 0;

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
            case "--test-delay-amr":
                delayAmr = value;
                break;
            case "--test-delay-ms":
                delayMs = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                if (delayMs < 0 || delayMs > 60000) throw new ArgumentOutOfRangeException(nameof(delayMs));
                break;
            default:
                throw new ArgumentException($"알 수 없는 인자입니다: {arguments[index]}");
        }
        index++;
    }

    if ((delayAmr is null) != (delayMs == 0)) throw new ArgumentException("시험 지연 AMR과 1~60000ms를 함께 지정하세요.");
    return (new MqttConnectionSettings(host, port, connectTimeoutSeconds), showPayload, delayAmr, delayMs);
}
