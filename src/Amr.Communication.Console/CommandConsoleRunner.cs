using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Amr.Communication.Command;
using Amr.Communication.ConsoleApp.Logging;
using Amr.Communication.Mqtt;
using MQTTnet;
using Serilog.Events;

namespace Amr.Communication.ConsoleApp;

/// <summary>한 실행에서 하나의 요청을 만든다. ACK 대조는 COM05-B에서 수행한다.</summary>
internal static class CommandConsoleRunner
{
    internal static async Task<int> RunAsync(string[] args, Func<IMqttClient> clientFactory,
        CancellationToken userCancellation, TextWriter output, TextWriter error, StructuredLog? log)
    {
        MqttConnection? connection = null;
        string? amrId = null;
        CommandMessage? command = null;
        string stage = "Input";
        int exitCode = 0;
        try
        {
            var options = Parse(args);
            amrId = options.AmrId;
            command = new($"CMD-{Guid.NewGuid():N}", options.Type, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                new(options.X, options.Y));
            byte[] payload = command.ToPayload();
            var validation = new CommandValidator().Validate($"amr/{amrId}/command", new ReadOnlySequence<byte>(payload));
            if (!validation.IsValid) throw new CommandValidationException(validation.Error!);
            userCancellation.ThrowIfCancellationRequested();
            Report("CommandRequested");
            var client = clientFactory();
            connection = new MqttConnection(client);
            stage = "Connect";
            await connection.ConnectAsync(options.Connection, userCancellation);
            stage = "Publish";
            await new CommandPublisher(client, options.PublishTimeout).PublishAsync(amrId, payload, userCancellation);
            Report("CommandPublished", "QoS=0 Retain=false Delivery=Unconfirmed Execution=NotTracked");
        }
        catch (CommandValidationException exception)
        {
            exitCode = 1;
            Report("CommandRejected", $"Code={exception.Error.Code} Field={exception.Error.Field}", true);
        }
        catch (ArgumentException)
        {
            exitCode = 1;
            Report("InvalidCommandOptions", "Use --help for command options.", true);
        }
        catch (OperationCanceledException) when (userCancellation.IsCancellationRequested)
        {
            Report("CommandCanceled", $"Stage={stage} Reason=UserCanceled Delivery={(stage == "Publish" ? "Unknown" : "NotSent")}");
        }
        catch (MqttConnectionException exception)
        {
            exitCode = 1;
            Report("ConnectFailed", $"Reason={exception.Reason} Delivery=NotSent", true);
        }
        catch (CommandPublishException exception)
        {
            exitCode = 1;
            Report("CommandPublishFailed", $"Reason={exception.Reason} Delivery={(exception.Reason == CommandPublishFailure.NotConnected ? "NotSent" : "Unknown")}", true);
        }
        catch (Exception exception)
        {
            exitCode = 1;
            // 오류 원문·Payload 대신 고정 진단과 예외 유형만 남긴다.
            Report("CommandFailed", $"Stage={stage} ExceptionType={exception.GetType().Name}", true);
        }
        finally
        {
            if (connection is not null)
            {
                try { await connection.DisconnectAsync(); }
                catch (Exception exception)
                {
                    exitCode = 1;
                    Report("DisconnectFailed", $"ExceptionType={exception.GetType().Name}", true);
                }
                finally { connection.Dispose(); }
            }
        }
        return exitCode;

        void Report(string eventType, string detail = "", bool failed = false)
        {
            // JSON 이스케이프로 콘솔 줄 삽입을 방지한다. 거부 입력은 ID를 포함해 출력하지 않는다.
            bool accepted = stage != "Input" || eventType == "CommandRequested";
            string identity = accepted ? $" AmrId={JsonSerializer.Serialize(amrId)} CommandId={JsonSerializer.Serialize(command?.CommandId)} CommandType={command?.Type}" : "";
            (failed ? error : output).WriteLine($"{eventType}{identity} {detail}".TrimEnd());
            log?.Event(eventType, failed ? LogEventLevel.Error : LogEventLevel.Information,
                ("AmrId", accepted ? amrId : null), ("CommandId", accepted ? command?.CommandId : null),
                ("CommandType", accepted ? command?.Type : null), ("Stage", stage), ("Detail", detail));
        }
    }

    private sealed record Options(MqttConnectionSettings Connection, TimeSpan PublishTimeout,
        string AmrId, string Type, double? X, double? Y);

    private static Options Parse(string[] args)
    {
        string[] allowed = ["--command", "--amr-id", "--x", "--y", "--host", "--port", "--connect-timeout-seconds", "--publish-timeout-seconds"];
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !allowed.Contains(args[i]) || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Invalid command options.");
        }
        if (!values.TryGetValue("--command", out string? type) || !values.TryGetValue("--amr-id", out string? id) ||
            type is not ("MOVE" or "STOP" or "RESET")) throw new ArgumentException("Command and target are required.");
        double? x = null, y = null;
        if (type == "MOVE")
        {
            x = ReadCoordinate("--x");
            y = ReadCoordinate("--y");
        }
        else if (values.ContainsKey("--x") || values.ContainsKey("--y"))
            throw new ArgumentException("STOP and RESET require empty parameters.");
        int seconds = ReadInteger("--publish-timeout-seconds", 3);
        if (seconds < 1 || seconds > int.MaxValue / 1000) throw new ArgumentException("Invalid publish timeout.");
        return new(new(values.GetValueOrDefault("--host", "127.0.0.1"), ReadInteger("--port", 1884),
            ReadInteger("--connect-timeout-seconds", 3)), TimeSpan.FromSeconds(seconds), id, type, x, y);

        double ReadCoordinate(string key)
        {
            if (!values.TryGetValue(key, out string? value) ||
                !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
                throw new ArgumentException("Finite coordinates are required.");
            return number;
        }
        int ReadInteger(string key, int fallback)
        {
            if (!values.TryGetValue(key, out string? value)) return fallback;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number)) throw new ArgumentException("Invalid integer.");
            return number;
        }
    }
}
