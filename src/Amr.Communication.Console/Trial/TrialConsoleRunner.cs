using System.Globalization;
using Amr.Communication.Command;
using Amr.Communication.ConsoleApp.Logging;
using Amr.Communication.Mqtt;
using Amr.Communication.Validation;
using MQTTnet;
using Serilog.Events;

namespace Amr.Communication.ConsoleApp;

internal static class TrialConsoleRunner
{
    internal static async Task<int> RunAsync(string[] args, Func<IMqttClient> clientFactory,
        CancellationToken cancellation, TextWriter output, TextWriter error, StructuredLog? log = null,
        Func<CancellationToken, ValueTask<string?>>? readLine = null)
    {
        TrialConsoleOptions options;
        try { options = TrialConsoleOptions.Parse(args); }
        catch (ArgumentException) { error.WriteLine("시험 옵션 오류: --trial [--host HOST --port 1884 --amr-id AMR_001 --reconnect-delay-seconds 5 --reconnect-attempts 5 --console-detail --show-payload --no-color]"); return 1; }
        bool interactive = readLine is null && ReferenceEquals(output, Console.Out) &&
            !Console.IsInputRedirected && !Console.IsOutputRedirected;
        var display = new TrialConsoleDisplay(output, !options.NoColor && !Console.IsOutputRedirected, interactive: interactive);
        display.Header(options);
        using var events = new TrialEventLogger(display, options, log);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        ReconnectingCommunicationSession? source = null;
        await using var session = new ReconnectingCommunicationSession(options.Connection, options.AmrId, options.Reconnect,
            entry => events.Observe(entry, source?.SessionId),
            clientFactory, showPayload: options.ShowPayload);
        source = session;
        TrialConsoleInput? ownedInput = null;
        try { if (readLine is null) ownedInput = new TrialConsoleInput(display); }
        catch (Exception e) when (e is IOException or PlatformNotSupportedException)
        { events.Write(LogEventLevel.Error, "InputUnavailable", "입력오류", "표준 입력을 사용할 수 없습니다: {ExceptionType:l}", ("ExceptionType", e.GetType().Name)); return 1; }
        using var input = ownedInput;
        readLine ??= input!.ReadLineAsync;
        Task<int> communication = session.RunAsync(lifetime.Token);
        Task commands = ReadCommandsAsync();
        int exit = 1;
        bool inputFailed = false;
        try
        {
            await Task.WhenAny(communication, commands);
            if (commands.IsCompleted) await commands;
            else exit = await communication;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { exit = 0; }
        catch (Exception exception) { inputFailed = true; events.Write(LogEventLevel.Error, "TrialFailed", "오류", "시험 중단: {ExceptionType:l}", ("ExceptionType", exception.GetType().Name)); }
        finally
        {
            lifetime.Cancel();
            try { await commands; } catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception) { inputFailed = true; }
            await session.StopAsync();
            int communicationExit = await communication;
            if (communicationExit != 0) exit = communicationExit;
            else if (!inputFailed && (cancellation.IsCancellationRequested || commands.IsCompletedSuccessfully)) exit = 0;
        }
        events.Write(exit == 0 ? LogEventLevel.Information : LogEventLevel.Error, "TrialExit", "종료결과", "ExitCode={ExitCode}", ("ExitCode", exit));
        return exit;

        async Task ReadCommandsAsync()
        {
            while (!lifetime.IsCancellationRequested)
            {
                string? line = await readLine(lifetime.Token);
                if (line is null || line.Trim().Equals("/exit", StringComparison.OrdinalIgnoreCase) || line.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
                { lifetime.Cancel(); return; }
                string[] words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) continue;
                string verb = words[0].ToUpperInvariant();
                if (words.Length == 1 && verb == "STATUS") { events.Write(LogEventLevel.Information, "TrialStatus", "상태", "Broker={Broker:l} AMR={AmrId:l} Ready={Ready} RunId={RunId:l}", ("Broker", session.State), ("Ready", session.IsReady)); continue; }
                if (words.Length == 1 && verb == "HELP") { events.Write(LogEventLevel.Information, "TrialHelp", "입력안내", "move X Y | stop | reset | status | detail on/off | /exit"); continue; }
                if (words.Length == 2 && verb == "DETAIL" && words[1].ToUpperInvariant() is "ON" or "OFF")
                { events.SetConsoleDetail(words[1].Equals("on", StringComparison.OrdinalIgnoreCase)); continue; }
                double? x = null, y = null;
                if (verb == "MOVE" && words.Length == 3 && double.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double px) &&
                    double.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double py) && double.IsFinite(px) && double.IsFinite(py))
                { x = px; y = py; }
                else if (!(words.Length == 1 && verb is "STOP" or "RESET"))
                { events.Write(LogEventLevel.Warning, "InputRejected", "입력거부", "move X Y / stop / reset / status / detail on/off / help / /exit 형식을 확인하세요."); continue; }
                if (!session.IsReady) { events.Write(LogEventLevel.Warning, "CommandRejectedOffline", "발행거부", "서버 연결·구독 준비 전입니다. 명령을 보관하거나 자동 재전송하지 않습니다."); continue; }
                var command = new CommandMessage($"CMD-{Guid.NewGuid():N}", verb, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), new(x, y));
                events.Command("CommandRequested", "발행요청", command, "요청 작성");
                try
                {
                    await session.PublishCommandAsync(command.ToPayload(), lifetime.Token);
                    events.Command("CommandPublished", "명령발행", command, "· MQTT 발행 반환, AMR 수행 미확인");
                }
                catch (CommandPublishException e) { events.Command("CommandPublishFailed", "발행실패", command, $"Reason={e.Reason} · 자동 재전송 없음, 전달 여부 불명일 수 있음", LogEventLevel.Error); }
                catch (OperationCanceledException) { events.Command("CommandPublishCancelled", "발행중단", command, "전달 여부 불명, 자동 재전송 없음", LogEventLevel.Warning); }
                catch (CommandValidationException e) { events.Command("CommandValidationFailed", "발행검사거부", command, $"Code={e.Error.Code} Field={e.Error.Field}", LogEventLevel.Error); }
            }
        }
    }
}
