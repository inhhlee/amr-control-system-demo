using System.Diagnostics;
using System.Text;
using Amr.Simulator.Simulation;

namespace Amr.Simulator.Telemetry;

public static class MqttSimulationSession
{
    public static async Task<int> RunAsync(SimulatedAmr amr, SimulatorMqttOptions options,
        ISimulationTransport transport, TextWriter output, TextWriter error, CancellationToken userToken,
        ISimulationClock? clock = null)
    {
        output = TextWriter.Synchronized(output);
        SimulationSnapshot latest = SimulationSnapshot.Capture(amr);
        using var stop = new CancellationTokenSource();
        using var publishing = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        int reason = 0; // 1=user, 2=connection loss, 3=failure, 4=final publication completed
        int code = 0;
        bool connected = false;
        Task? runner = null, sender = null;
        void RequestStop(int value)
        {
            if (Interlocked.CompareExchange(ref reason, value, 0) == 0) stop.Cancel();
        }
        void Lost() { if (Volatile.Read(ref connected)) RequestStop(2); }
        using var registration = userToken.Register(() => RequestStop(1));
        transport.ConnectionLost += Lost;
        try
        {
            // 입력 오류는 접속·이동 전에 검사한다. 송신 시각은 실제 발행 준비 시 생성한다.
            _ = SimulationMessages.Create(latest, false, 0);
            _ = SimulationMessages.Create(latest, true, 0);
            await transport.ConnectAsync(stop.Token);
            Volatile.Write(ref connected, true);
            await output.WriteLineAsync($"MqttConnected ClientId={options.Connection.ClientId} TelemetryMs={options.TelemetryMs} HeartbeatMs={options.HeartbeatMs}");
            await PublishAsync(true, stop.Token);
            await PublishAsync(false, stop.Token);
            sender = SendPeriodicallyAsync(publishing.Token);
            runner = new SimulationRunner(clock).RunAsync(amr, output, stop.Token,
                snapshot => Volatile.Write(ref latest, snapshot));
            await Task.WhenAny(runner, sender);
            if (sender.IsFaulted) await sender;
            await runner;
            publishing.Cancel();
            try { await sender; } catch (OperationCanceledException) when (publishing.IsCancellationRequested) { }
            stop.Token.ThrowIfCancellationRequested();
            // 주기 송신과 겹치지 않게 기다린 뒤 최종 IDLE/속도0을 반드시 발행한다.
            await PublishAsync(false, stop.Token);
            if (Interlocked.CompareExchange(ref reason, 4, 0) != 0)
                stop.Token.ThrowIfCancellationRequested();
            await output.WriteLineAsync("MqttSimulationCompleted Delivery=Unconfirmed");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception exception)
        {
            RequestStop(3);
            await error.WriteLineAsync($"MqttSimulationFailed Reason={exception.GetType().Name} Delivery=Unknown");
        }
        finally
        {
            stop.Cancel();
            publishing.Cancel();
            foreach (var task in new[] { runner, sender })
            {
                if (task is null) continue;
                try { await task; }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                catch (Exception) { Interlocked.CompareExchange(ref reason, 3, 0); }
            }
            Volatile.Write(ref connected, false);
            transport.ConnectionLost -= Lost;
            try { await transport.DisconnectAsync(); }
            catch (Exception exception)
            {
                code = 1;
                await error.WriteLineAsync($"MqttDisconnectFailed Reason={exception.GetType().Name}");
            }
            transport.Dispose();
        }
        if (reason == 1) await output.WriteLineAsync("MqttSimulationCanceled Reason=UserCanceled");
        if (reason == 2) await error.WriteLineAsync("MqttSimulationFailed Reason=ConnectionLost Delivery=Unknown");
        if (reason is 2 or 3) code = 1;
        await output.WriteLineAsync($"MqttSessionEnded ExitCode={code}");
        return code;

        async Task PublishAsync(bool heartbeat, CancellationToken token)
        {
            var message = SimulationMessages.Create(Volatile.Read(ref latest), heartbeat, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await transport.PublishAsync(message, token);
            await output.WriteLineAsync($"{message.Kind}Published Topic={message.Topic} Payload={Encoding.UTF8.GetString(message.Payload)} Delivery=Unconfirmed");
        }

        async Task SendPeriodicallyAsync(CancellationToken token)
        {
            var elapsed = Stopwatch.StartNew();
            long nextTelemetry = options.TelemetryMs, nextHeartbeat = options.HeartbeatMs;
            while (true)
            {
                long remaining = Math.Min(nextTelemetry, nextHeartbeat) - elapsed.ElapsedMilliseconds;
                if (remaining > 0) await Task.Delay((int)Math.Min(remaining, int.MaxValue), token);
                token.ThrowIfCancellationRequested();
                if (elapsed.ElapsedMilliseconds >= nextHeartbeat)
                {
                    await PublishAsync(true, token);
                    nextHeartbeat = elapsed.ElapsedMilliseconds + options.HeartbeatMs;
                }
                if (elapsed.ElapsedMilliseconds >= nextTelemetry)
                {
                    await PublishAsync(false, token);
                    nextTelemetry = elapsed.ElapsedMilliseconds + options.TelemetryMs;
                }
            }
        }
    }
}
