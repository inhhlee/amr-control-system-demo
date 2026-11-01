using System.Text.Json;
using Amr.Communication.ConsoleApp.Logging;
using Amr.Communication.Mqtt;
using MQTTnet;

namespace Amr.Communication.ConsoleApp;

// 지도/GUI를 참조하지 않는 정식 관제 세션 실행 경로. 기존 콘솔 모드는 그대로 유지한다.
internal static class HeartbeatConsoleRunner
{
    internal static async Task<int> RunAsync(string[] args, Func<IMqttClient> clientFactory,
        CancellationToken cancellation, TextWriter output, TextWriter error, StructuredLog? log = null)
    {
        output = TextWriter.Synchronized(output);
        error = TextWriter.Synchronized(error);
        ControlCommunicationSession? session = null;
        int exit = 0;
        try
        {
            int index = Array.IndexOf(args, "--monitor-amr");
            if (args.Count(value => value == "--monitor-amr") != 1 || index + 1 >= args.Length)
                throw new ArgumentException("Specify --monitor-amr once with an AMR ID.", "arguments");
            string id = args[index + 1];
            if (string.IsNullOrWhiteSpace(id) || id.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '/' or '+' or '#'))
                throw new ArgumentException("Invalid AMR topic ID.", "amrId");
            var remaining = args.Take(index).Concat(args.Skip(index + 2)).ToArray();
            var (settings, showPayload, delayAmr, delayMs) = ConsoleRunner.ReadSettings(remaining);
            output.WriteLine($"{DateTimeOffset.UtcNow:O} ControlSession Host={settings.Host} Port={settings.Port} AmrId={JsonSerializer.Serialize(id)} TimeoutMs=5000 CheckIntervalMs=500");
            session = new(settings, id, async (message, token) => {
                if (message.AmrId == delayAmr && delayMs > 0) await Task.Delay(delayMs, token);
                token.ThrowIfCancellationRequested();
            }, entry => {
                output.WriteLine($"{DateTimeOffset.UtcNow:O} {entry.Name} {JsonSerializer.Serialize(entry.Value)}");
                log?.Event("ControlSessionEvent", properties: [("Name", entry.Name), ("Value", entry.Value)]);
            }, client: clientFactory(), showPayload: showPayload);
            await session.StartAsync(cancellation);
            output.WriteLine("WaitingForShutdown: Ctrl+C로 정상 종료합니다.");
            await session.Completion;
            exit = session.FailureReason is null ? 0 : 1;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (ArgumentException exception) when (session is null)
        { error.WriteLine($"InputRejected Field={exception.ParamName ?? "arguments"} ExceptionType={exception.GetType().Name}"); exit = 1; }
        catch (MqttConnectionException exception)
        { error.WriteLine($"ConnectFailed Reason={exception.Reason} ExceptionType={exception.GetType().Name}"); exit = 1; }
        catch (Exception exception)
        { error.WriteLine($"SessionError ExceptionType={exception.GetType().Name}"); exit = 1; }
        finally
        {
            if (session is not null)
                try { await session.DisposeAsync(); }
                catch (Exception exception) { error.WriteLine($"CleanupError ExceptionType={exception.GetType().Name}"); exit = 1; }
        }
        output.WriteLine($"ExitCode={exit}");
        return exit;
    }
}
