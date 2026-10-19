using Amr.Communication.ConsoleApp.Logging;
using Serilog.Events;
using Amr.Communication.Mqtt;
using Amr.Communication.Models;
using Amr.Communication.Validation;
using MQTTnet;
using System.Net.Sockets;
using System.Text.Encodings.Web;
using System.Text.Json;

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Amr.Communication.Tests")]

namespace Amr.Communication.ConsoleApp;

internal static class ConsoleRunner
{
    private enum ShutdownReason { None, UserCanceled, ConnectionLost }

    internal static async Task<int> RunAsync(string[] args, Func<IMqttClient> clientFactory,
        CancellationToken userCancellation, TextWriter output, TextWriter error, StructuredLog? log = null)
    {
        if (args.Contains("--command", StringComparer.Ordinal))
            return await CommandConsoleRunner.RunAsync(args, clientFactory, userCancellation, output, error, log);
        if (args.Length == 1 && args[0] == "--help")
        {
            output.WriteLine("사용법: --host 127.0.0.1 --port 1884 --connect-timeout-seconds 3 [--show-payload]");
            output.WriteLine("Heartbeat·Telemetry·Alarm·ACK 검사 후 대상 AMR과 정상 모델을 전달합니다. --show-payload는 비밀정보 없는 정상 시험 입력의 앞 256바이트만 표시합니다. Ctrl+C로 종료합니다.");
            output.WriteLine("순서·독립성 시험: --test-delay-amr AMR_001 --test-delay-ms 5000 (해당 AMR의 처리만 지연)");
            output.WriteLine("명령 발행: --command MOVE --amr-id AMR_001 --x 30 --y 15 [--publish-timeout-seconds 3]");
            output.WriteLine("STOP/RESET은 --x/--y를 생략합니다. --host/--port/--connect-timeout-seconds 사용 가능. 한 명령 발행 후 종료하며 ACK·실제 동작 완료는 확인하지 않습니다.");
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
        int shutdownReason = (int)ShutdownReason.None;

        using var userRegistration = userCancellation.Register(() =>
        {
            // Only the first requester owns cancellation. A later signal must not release
            // the wait while the winner is still recording its reason and exit code.
            if (Interlocked.CompareExchange(ref shutdownReason, (int)ShutdownReason.UserCanceled,
                (int)ShutdownReason.None) != (int)ShutdownReason.None) return;
            _ = queue?.RequestStop();
            shutdown.Cancel();
        });
        bool connecting = false;
        int connectionApproved = 0;

        try
        {
            var (settings, showPayload, delayAmr, delayMs) = ReadSettings(args);
            log?.Event("Settings", properties: [("ClientId", settings.ClientId),
                ("Port", settings.Port), ("ConnectTimeoutSeconds", settings.ConnectTimeout.TotalSeconds)]);
            queueStopTimeout = settings.ConnectTimeout;
            Report($"Settings Host={settings.Host} Port={settings.Port} ClientId={settings.ClientId} " +
                $"MQTT=5 CleanStart=true SessionExpiry=0 KeepAliveSeconds={settings.KeepAlive.TotalSeconds} " +
                $"ConnectTimeoutSeconds={settings.ConnectTimeout.TotalSeconds} DisconnectTimeoutSeconds=3");

            client = clientFactory();
            connection = new MqttConnection(client);
            queue = new AmrReceiveQueue(async (message, token) =>
            {
                log?.Event("ProcessingStarted", properties: [("AmrId", message.AmrId), ("Kind", message.Kind)]);
                Report($"ProcessingStarted AmrId={JsonSerializer.Serialize(message.AmrId)} Kind={message.Kind}");
                if (message.AmrId == delayAmr && delayMs > 0) await Task.Delay(delayMs, token);
                token.ThrowIfCancellationRequested();
                ReceiveMessage(message);
            }, entry =>
            {
                Report($"QueueEvent {JsonSerializer.Serialize(entry)}");
                log?.Event("QueueEvent", LogEventLevel.Warning, ("Code", entry.Code), ("AmrId", entry.AmrId),
                    ("Kind", entry.Kind), ("Count", entry.Count), ("ExceptionType", entry.ExceptionType));
            });
            disconnectedHandler = eventArgs =>
            {
                if (Volatile.Read(ref connectionApproved) == 1 &&
                    Interlocked.CompareExchange(ref shutdownReason, (int)ShutdownReason.ConnectionLost,
                        (int)ShutdownReason.None) == (int)ShutdownReason.None)
                {
                    _ = queue.RequestStop();
                    Report("ConnectionLost", "ConnectionLost");
                    Interlocked.Exchange(ref exitCode, 1);
                    shutdown.Cancel();
                }
                return Task.CompletedTask;
            };
            client.DisconnectedAsync += disconnectedHandler;
            subscription = new MessageSubscription(client, message =>
            {
                var result = queue.TryEnqueue(message);
                log?.Event("QueueAdmission", result == QueueAdmission.Accepted ? LogEventLevel.Information : LogEventLevel.Warning,
                    ("Result", result.ToString()), ("AmrId", message.AmrId), ("Kind", message.Kind));
                Report($"QueueAdmission Result={result} AmrId={JsonSerializer.Serialize(message.AmrId)} Kind={message.Kind}");
            }, ReportRejection,
                error =>
                {
                    Report($"CallbackError Stage={error.Stage} ExceptionType={error.ExceptionType}");
                    log?.Event("CallbackError", LogEventLevel.Error, ("Stage", error.Stage), ("ExceptionType", error.ExceptionType));
                }, ReportDiagnostic, showPayload);
            connecting = true;
            Report("Connecting", "Connecting");
            await connection.ConnectAsync(settings, shutdown.Token);
            Volatile.Write(ref connectionApproved, 1);
            if (!connection.IsConnected) throw new MqttConnectionException(MqttConnectionFailure.TransportFailure);
            shutdown.Token.ThrowIfCancellationRequested();
            connecting = false;
            Report("Connected", "Connected");
            Report($"QueueReady CapacityPerAmr=256 AmrLimit=64 StopTimeoutSeconds={queueStopTimeout.TotalSeconds} TestDelayAmr={JsonSerializer.Serialize(delayAmr)} TestDelayMs={delayMs}");
            Report($"Subscribing TopicFilters={string.Join(',', MessageSubscription.TopicFilters)} QoS=0 PayloadPreview={showPayload} LimitBytes=256");
            await subscription.SubscribeAsync(shutdown.Token);
            log?.Event("Subscribed", properties: [("QoS", 0)]);
            Report($"Subscribed TopicFilters={string.Join(',', MessageSubscription.TopicFilters)} QoS=0");
            Report("WaitingForShutdown: Ctrl+C로 정상 종료합니다.");
            await Task.Delay(Timeout.Infinite, shutdown.Token);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            var reason = (ShutdownReason)Volatile.Read(ref shutdownReason);
            bool connectCanceled = connecting && reason == ShutdownReason.UserCanceled;
            Report(connectCanceled ? "ConnectCanceled Reason=UserCanceled" : "ShutdownRequested");
            log?.Event(connectCanceled ? "ConnectCanceled" : "ShutdownRequested", properties: [("Reason", reason.ToString())]);
        }
        catch (MqttConnectionException exception)
        {
            log?.Event("ConnectFailed", LogEventLevel.Error, ("Reason", exception.Reason.ToString()),
                ("ResultCode", exception.ResultCode?.ToString()), ("ExceptionType", exception.InnerException?.GetType().Name),
                ("SocketError", FindSocketError(exception)));
            error.WriteLine($"{DateTimeOffset.UtcNow:O} ConnectFailed Reason={exception.Reason} " +
                $"ResultCode={exception.ResultCode?.ToString() ?? "None"} ExceptionType={exception.InnerException?.GetType().Name ?? "None"} " +
                $"SocketError={FindSocketError(exception)}");
            exitCode = 1;
        }
        catch (Exception exception) when (client is null && exception is ArgumentException or FormatException or OverflowException)
        {
            log?.Event("InputRejected", LogEventLevel.Warning, ("Reason", "InvalidSettings"),
                ("Field", (exception as ArgumentException)?.ParamName ?? "arguments"), ("ExceptionType", exception.GetType().Name));
            error.WriteLine($"{DateTimeOffset.UtcNow:O} InputRejected Reason=InvalidSettings " +
                $"Field={(exception as ArgumentException)?.ParamName ?? "arguments"} ExceptionType={exception.GetType().Name}");
            exitCode = 1;
        }
        catch (Exception exception)
        {
            log?.Event("Error", LogEventLevel.Error, ("ExceptionType", exception.GetType().Name));
            error.WriteLine($"{DateTimeOffset.UtcNow:O} Error ExceptionType={exception.GetType().Name}");
            exitCode = 1;
        }
        finally
        {
            // 먼저 큐 접수를 차단한다. 구독 해제 응답을 기다리는 동안에도 새 처리를 시작하지 않는다.
            if (queue is not null)
            {
                try { await queue.StopAsync(queueStopTimeout); Report("QueueStopped", "QueueStopped"); }
                catch (Exception exception)
                {
                    log?.Event("QueueCleanupError", LogEventLevel.Error, ("ExceptionType", exception.GetType().Name));
                    error.WriteLine($"QueueCleanupError ExceptionType={exception.GetType().Name}");
                    exitCode = 1;
                }
            }
            try
            {
                if (subscription is not null)
                {
                    try
                    {
                        Report("Unsubscribing", "Unsubscribing");
                        await subscription.StopAsync();
                        Report("SubscriptionStopped", "SubscriptionStopped");
                    }
                    catch (Exception exception)
                    {
                        log?.Event("SubscriptionCleanupError", LogEventLevel.Error, ("ExceptionType", exception.GetType().Name));
                        error.WriteLine($"{DateTimeOffset.UtcNow:O} SubscriptionCleanupError ExceptionType={exception.GetType().Name}");
                        exitCode = 1;
                    }
                }
                if (connection is not null)
                {
                    try
                    {
                        if (connection.IsConnected)
                        {
                            Report("Disconnecting", "Disconnecting");
                            // 이미 취소된 shutdown.Token을 넘기면 해제까지 취소되므로 새 대기로 정리한다.
                            await connection.DisconnectAsync();
                            Report("Disconnected", "Disconnected");
                        }
                    }
                    finally
                    {
                        if (client is not null && disconnectedHandler is not null) client.DisconnectedAsync -= disconnectedHandler;
                        connection.Dispose();
                        Report("Disposed", "Disposed");
                    }
                }
            }
            catch (Exception exception)
            {
                log?.Event("CleanupError", LogEventLevel.Error, ("ExceptionType", exception.GetType().Name));
                error.WriteLine($"{DateTimeOffset.UtcNow:O} CleanupError ExceptionType={exception.GetType().Name}");
                exitCode = 1;
            }
        }

        Report($"ExitCode={exitCode}");
        log?.Event("ExitCode", properties: [("ExitCode", exitCode)]);
        return exitCode;

        void Report(string message, string? eventType = null)
        {
            if (eventType is not null) log?.Event(eventType);
            output.WriteLine($"{DateTimeOffset.UtcNow:O} {message}");
        }

        static string FindSocketError(Exception exception)
        {
            for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
                if (cause is SocketException socket) return socket.SocketErrorCode.ToString();
            return "None";
        }

        void ReportDiagnostic(MessageDiagnostic diagnostic)
        {
            log?.Event("MessageDiagnostic", properties: [("ReceivedAtUtc", diagnostic.ReceivedAtUtc),
                ("PayloadBytes", diagnostic.PayloadBytes), ("Validation", diagnostic.IsValid ? "Accepted" : "Rejected")]);
            // 제어문자·개행은 이스케이프해 한 메시지가 다른 로그 줄처럼 보이지 않게 한다.
            var jsonOptions = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            string topic = JsonSerializer.Serialize(diagnostic.Topic[..Math.Min(diagnostic.Topic.Length, 256)], jsonOptions);
            string payload = diagnostic.PayloadPreview is null ? "<hidden>" : JsonSerializer.Serialize(diagnostic.PayloadPreview, jsonOptions);
            output.WriteLine($"{diagnostic.ReceivedAtUtc:O} MessageDiagnostic Topic={topic} " +
                $"TopicTruncated={diagnostic.Topic.Length > 256} PayloadBytes={diagnostic.PayloadBytes} " +
                $"Payload={payload} PayloadTruncated={diagnostic.PayloadTruncated} Validation={(diagnostic.IsValid ? "Accepted" : "Rejected")}");
        }

        void ReceiveMessage(ReceivedMessage message)
        {
            // 실제 Core 업무 처리 대신 정상 모델 전달을 관찰하는 시험용 수신자다.
            // object Model의 실제 수신 유형별 필드를 출력하며 제어문자는 JSON 이스케이프한다.
            Report($"Received {JsonSerializer.Serialize(message)}");
            log?.Event("Received", properties: [("AmrId", message.AmrId), ("Kind", message.Kind), ("ReceivedAtUtc", message.ReceivedAtUtc)]);
        }

        void ReportRejection(ValidationError error)
        {
            Report($"Rejected Code={error.Code} Field={error.Field} Reason={error.Reason}");
            log?.Event("Rejected", LogEventLevel.Warning, ("Code", error.Code.ToString()), ("Field", error.Field), ("Reason", error.Reason));
        }

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
                    throw new ArgumentException("인자 값이 필요합니다.", "arguments");
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
                        throw new ArgumentException("알 수 없는 인자입니다.", "arguments");
                }
                index++;
            }

            if ((delayAmr is null) != (delayMs == 0)) throw new ArgumentException("시험 지연 AMR과 1~60000ms를 함께 지정하세요.");
            return (new MqttConnectionSettings(host, port, connectTimeoutSeconds), showPayload, delayAmr, delayMs);
        }
    }
}
