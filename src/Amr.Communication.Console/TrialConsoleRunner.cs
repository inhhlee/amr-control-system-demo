using System.Globalization;
using Amr.Communication.Command;
using Amr.Communication.ConsoleApp.Logging;
using Amr.Communication.Mqtt;
using Amr.Communication.Validation;
using MQTTnet;

namespace Amr.Communication.ConsoleApp;

internal static class TrialConsoleRunner
{
    internal static async Task<int> RunAsync(string[] args, Func<IMqttClient> clientFactory,
        CancellationToken cancellation, TextWriter output, TextWriter error, StructuredLog? log = null,
        Func<CancellationToken, ValueTask<string?>>? readLine = null)
    {
        TrialConsoleOptions options;
        try { options = TrialConsoleOptions.Parse(args); }
        catch (ArgumentException) { error.WriteLine("시험 옵션 오류: --trial [--host HOST --port 1884 --amr-id AMR_001 --reconnect-delay-seconds 5 --reconnect-attempts 5 --show-payload --no-color]"); return 1; }
        var display = new TrialConsoleDisplay(output, !options.NoColor && !Console.IsOutputRedirected);
        display.Header(options);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        await using var session = new ReconnectingCommunicationSession(options.Connection, options.AmrId, options.Reconnect,
            entry => { display.Event(entry); log?.Event("TrialSessionEvent", properties: [("Name", entry.Name), ("AmrId", options.AmrId)]); },
            clientFactory, showPayload: options.ShowPayload);
        TrialConsoleInput? ownedInput = null;
        try { if (readLine is null) ownedInput = new TrialConsoleInput(); }
        catch (Exception e) when (e is IOException or PlatformNotSupportedException)
        { display.Line("입력오류", $"표준 입력을 사용할 수 없습니다: {e.GetType().Name}", "31"); return 1; }
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
        catch (Exception exception) { inputFailed = true; display.Line("오류", $"시험 중단: {exception.GetType().Name}", "31"); }
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
        display.Line("종료결과", $"ExitCode={exit}");
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
                if (words.Length == 1 && verb == "STATUS") { display.Line("상태", $"Broker={session.State} AMR={options.AmrId} Ready={session.IsReady}"); continue; }
                if (words.Length == 1 && verb == "HELP") { display.Line("입력안내", "move X Y | stop | reset | status | /exit"); continue; }
                double? x = null, y = null;
                if (verb == "MOVE" && words.Length == 3 && double.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double px) &&
                    double.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double py) && double.IsFinite(px) && double.IsFinite(py))
                { x = px; y = py; }
                else if (!(words.Length == 1 && verb is "STOP" or "RESET"))
                { display.Line("입력거부", "move X Y / stop / reset / status / help / /exit 형식을 확인하세요.", "33"); continue; }
                if (!session.IsReady) { display.Line("발행거부", "서버 연결·구독 준비 전입니다. 명령을 보관하거나 자동 재전송하지 않습니다.", "33"); continue; }
                var command = new CommandMessage($"CMD-{Guid.NewGuid():N}", verb, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), new(x, y));
                display.Line("발행요청", $"AMR={options.AmrId} 명령={verb} cmdId={command.CommandId}", "33");
                try
                {
                    await session.PublishCommandAsync(command.ToPayload(), lifetime.Token);
                    display.Line("명령발행", $"AMR={options.AmrId} 명령={verb} cmdId={command.CommandId} · MQTT 발행 반환, AMR 수행 미확인", "33");
                }
                catch (CommandPublishException e) { display.Line("발행실패", $"Reason={e.Reason} cmdId={command.CommandId} · 자동 재전송 없음, 전달 여부 불명일 수 있음", "31"); }
                catch (OperationCanceledException) { display.Line("발행중단", $"cmdId={command.CommandId} · 전달 여부 불명, 자동 재전송 없음", "33"); }
                catch (CommandValidationException e) { display.Line("발행검사거부", $"Code={e.Error.Code} Field={e.Error.Field}", "31"); }
            }
        }
    }
}
