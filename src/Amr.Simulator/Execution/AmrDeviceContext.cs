using System.Buffers;
using System.Text.Json;
using System.Threading.Channels;
using Amr.Communication.Command;
using Amr.Simulator.Configuration;
using Amr.Simulator.Simulation;
using Amr.Simulator.Telemetry;

namespace Amr.Simulator;

/// <summary>한 장비의 상태·명령 큐·시계·전송·취소·종료를 소유한다. 상태 변경은 실행 루프만 수행한다.</summary>
public sealed class AmrDeviceContext : IAsyncDisposable
{
    private readonly ConfiguredAmr _configured;
    private readonly SimulatorMqttOptions? _mqtt;
    private readonly IAmrDeviceTransport? _transport;
    private readonly ISimulationClock _clock;
    private readonly CommandDrivenAmr _amr;
    private readonly Channel<CommandMessage> _commands;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _lifetime = new();
    private readonly TextWriter _output, _error;
    private Task<int>? _run;
    private Task? _dispose;
    private bool _disposed;
    private int _accepting, _failed, _closing;
    private FaultScenarioRunner? _scenarios;
    public string AmrId => _configured.Settings.AmrId;
    private string CommandTopic => $"amr/{AmrId}/command";

    public AmrDeviceContext(ConfiguredAmr configured, SimulatorMqttOptions? mqtt,
        TextWriter output, TextWriter error, ISimulationClock? clock = null,
        IAmrDeviceTransport? transport = null, int commandQueueCapacity = 128)
    {
        ArgumentNullException.ThrowIfNull(configured);
        if (mqtt is null && configured.Mode == AmrOperationMode.Command)
            throw new ArgumentException("Command mode requires MQTT.");
        if (mqtt is null && transport is not null) throw new ArgumentException("Transport requires MQTT options.");
        if (commandQueueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(commandQueueCapacity));
        _configured = configured; _mqtt = mqtt;
        _clock = clock ?? new SystemSimulationClock();
        _output = TextWriter.Synchronized(output); _error = TextWriter.Synchronized(error);
        _amr = new(configured.Settings);
        _commands = Channel.CreateBounded<CommandMessage>(new BoundedChannelOptions(commandQueueCapacity)
        { SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false, FullMode = BoundedChannelFullMode.Wait });
        _transport = transport ?? (mqtt is null ? null : new MqttCommandSimulationSession(AmrId, mqtt));
    }

    // Task.Run은 장비별 시작/동기 구간도 다른 장비의 시작을 막지 않게 한다. 전용 OS 스레드는 만들지 않는다.
    public Task<int> RunAsync(CancellationToken token = default)
    {
        lock (_lifetime)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_run is not null) throw new InvalidOperationException("A device context is single-use.");
            return _run = Task.Run(() => RunCoreAsync(token), CancellationToken.None);
        }
    }

    public void RequestStop()
    {
        lock (_lifetime) { if (!_disposed) _stop.Cancel(); }
    }

    private void Fail(string reason)
    {
        Interlocked.Exchange(ref _failed, 1);
        _error.WriteLine($"{reason} AmrId={AmrId}");
        RequestStop(); // 다른 장비의 CTS에는 접근하지 않는다.
    }

    private void Receive(string topic, ReadOnlySequence<byte> payload, bool retained)
    {
        if (Volatile.Read(ref _accepting) == 0 || topic != CommandTopic) return;
        var result = new CommandValidator().Validate(topic, payload);
        if (!result.IsValid || retained)
            _output.WriteLine($"CommandRejected AmrId={AmrId} Reason={result.Error?.Code.ToString() ?? "RETAINED_COMMAND"}");
        else if (!_commands.Writer.TryWrite(result.Message!) && Volatile.Read(ref _accepting) != 0 && !_stop.IsCancellationRequested)
            Fail("CommandQueueFull");
    }

    private void Disconnected()
    {
        if (Volatile.Read(ref _closing) == 0) Fail("CommandSessionDisconnected");
    }

    private async Task<int> RunCoreAsync(CancellationToken parentToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(parentToken, _stop.Token);
        var token = stop.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            if (_transport is not null)
            {
                _transport.CommandReceived += Receive; _transport.ConnectionLost += Disconnected;
                await _transport.ConnectAsync(token);
                token.ThrowIfCancellationRequested();
                Volatile.Write(ref _accepting, 1);
                await _transport.SubscribeAsync(token);
                token.ThrowIfCancellationRequested();
                await _output.WriteLineAsync($"CommandSessionReady AmrId={AmrId} Topic={CommandTopic} Mode={_configured.Mode}");
            }
            long started = _clock.GetTimestamp();
            _scenarios = new(_configured.Faults,
                () => TimeSpan.FromSeconds(_clock.GetElapsedSeconds(started, _clock.GetTimestamp())));
            foreach (var fault in _configured.Faults)
                await _output.WriteLineAsync($"FaultScenarioScheduled AmrId={AmrId} Code={JsonSerializer.Serialize(fault.Code)} ActivateAfterMs={fault.ActivateAfterMs} ResolveAfterMs={fault.ResolveAfterMs}");
            // 초기 상태를 알린 뒤, route는 같은 상태 머신에서 설정 경로를 한 번만 시작한다.
            await Emit(_scenarios.Poll(_amr), true, token);
            await Emit(_scenarios.Poll(_amr), false, token);
            if (_configured.Mode == AmrOperationMode.Route && _amr.State == "IDLE") _amr.StartConfiguredRoute();
            await WriteSnapshot("SimulationStarted");
            if (_mqtt is not null) await Heartbeat(token);
            long lastTelemetry = _clock.GetTimestamp(), lastHeartbeat = lastTelemetry;
            long tick = _mqtt is null ? _configured.Settings.UpdateIntervalMs :
                Math.Min(_configured.Settings.UpdateIntervalMs, Math.Min(_mqtt.TelemetryMs, _mqtt.HeartbeatMs));
            while (true)
            {
                token.ThrowIfCancellationRequested();
                // 로컬 경로는 도착하고 남은 시나리오가 없으면 종료한다. MQTT는 도착 후에도 수신 대기한다.
                if (_mqtt is null && _amr.State == "IDLE" && !_scenarios.HasPendingEvents)
                { await WriteSnapshot("SimulationCompleted"); break; }
                await _clock.DelayAsync(tick, token);
                token.ThrowIfCancellationRequested();
                long now = _clock.GetTimestamp();
                bool telemetry = _mqtt is null || _clock.GetElapsedSeconds(lastTelemetry, now) * 1000 >= _mqtt.TelemetryMs;
                await Emit(_scenarios.Poll(_amr), telemetry, token);
                if (telemetry) lastTelemetry = now;
                // 한 tick의 처리 개수를 제한해 시간·장애·생존 신호 확인으로 돌아온다.
                for (int count = 0; count < 128 && _commands.Reader.TryRead(out var command); count++)
                {
                    token.ThrowIfCancellationRequested();
                    await Emit(_scenarios.Poll(_amr), false, token);
                    var result = _amr.Execute(command);
                    await _output.WriteLineAsync($"CommandProcessed AmrId={AmrId} Type={command.Type} CmdId={command.CommandId} State={_amr.State} Diagnostic={result.Diagnostic ?? "NONE"}");
                    await Emit(result, true, token);
                }
                now = _clock.GetTimestamp();
                if (_mqtt is not null && _clock.GetElapsedSeconds(lastHeartbeat, now) * 1000 >= _mqtt.HeartbeatMs)
                { await Heartbeat(token); lastHeartbeat = _clock.GetTimestamp(); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) { Fail($"CommandSessionFailed Reason={e.GetType().Name}"); }
        finally
        {
            Volatile.Write(ref _closing, 1);
            Volatile.Write(ref _accepting, 0);
            _commands.Writer.TryComplete();
            if (_transport is not null)
            {
                _transport.CommandReceived -= Receive; _transport.ConnectionLost -= Disconnected;
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _transport.UnsubscribeAsync(timeout.Token);
                }
                catch (Exception e) { Fail($"CommandUnsubscribeFailed Reason={e.GetType().Name}"); }
                try { await _transport.DisconnectAsync(); }
                catch (Exception e) { Fail($"CommandDisconnectFailed Reason={e.GetType().Name}"); }
                try { _transport.Dispose(); }
                catch (Exception e) { Fail($"CommandDisposeFailed Reason={e.GetType().Name}"); }
            }
            // 세션 종료 후 큐를 비운다. 미처리 명령을 성공으로 처리하거나 재송신하지 않는다.
            while (_commands.Reader.TryRead(out _)) { }
        }
        await _output.WriteLineAsync($"CommandSessionEnded ExitCode={_failed} AmrId={AmrId}");
        return _failed;
    }

    private async Task Emit(CommandExecutionResult result, bool telemetry, CancellationToken token)
    {
        if (result.Diagnostic == "FAULT_CAUSE_RESOLVED")
            await _output.WriteLineAsync($"FaultCauseResolved AmrId={AmrId} AllCausesResolved={_amr.CausesResolved} State={_amr.State} Alarm=LatchedUntilReset");
        foreach (var alarm in result.Alarms)
        {
            var payload = new Dictionary<string, object> { ["id"] = AmrId, ["ts"] = alarm.Timestamp,
                ["code"] = alarm.Code, ["level"] = alarm.Level, ["alarmState"] = alarm.AlarmState };
            if (alarm.Message is not null) payload["message"] = alarm.Message;
            await Publish("alarm", payload, token);
            await _output.WriteLineAsync($"AlarmPublished AmrId={AmrId} Code={JsonSerializer.Serialize(alarm.Code)} AlarmState={alarm.AlarmState} Delivery={(_transport is null ? "LocalOnly" : "Unconfirmed")}");
        }
        if (telemetry || result.Alarms.Count != 0 || result.Replies.Count != 0)
        {
            if (_transport is null) await WriteSnapshot("AmrMoved");
            else await Publish("telemetry", new { id = AmrId, ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                pos = new { x = _amr.Position.X, y = _amr.Position.Y }, sp = _amr.SpeedMps, state = _amr.State }, token);
        }
        foreach (var reply in result.Replies)
        {
            await Publish("ack", new { id = AmrId, cmdId = reply.CommandId, result = reply.Result, ts = reply.Timestamp, reason = reply.Reason }, token);
            await _output.WriteLineAsync($"FinalAckPublished AmrId={AmrId} CmdId={reply.CommandId} Result={reply.Result} Reason={reply.Reason} Delivery={(_transport is null ? "LocalOnly" : "Unconfirmed")}");
        }
    }

    private Task WriteSnapshot(string name) => _output.WriteLineAsync(FormattableString.Invariant(
        $"{name} AmrId={AmrId} X={_amr.Position.X:R} Y={_amr.Position.Y:R} State={_amr.State} CurrentSpeedMps={_amr.SpeedMps:R}"));
    private Task Heartbeat(CancellationToken token) => Publish("heartbeat", new { id = AmrId, ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), status = "OK" }, token);
    private Task Publish(string kind, object value, CancellationToken token) => _transport is null ? Task.CompletedTask :
        _transport.PublishAsync(new(kind, $"amr/{AmrId}/{kind}", JsonSerializer.SerializeToUtf8Bytes(value)), token);

    public ValueTask DisposeAsync()
    {
        lock (_lifetime)
        {
            if (_dispose is not null) return new(_dispose);
            _disposed = true; _stop.Cancel();
            return new(_dispose = DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            if (_run is not null) await _run.ConfigureAwait(false);
            else _transport?.Dispose();
        }
        finally { _stop.Dispose(); }
    }
}
