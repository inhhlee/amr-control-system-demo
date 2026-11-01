using System.Globalization;
using Amr.Communication.Command;
using Amr.Communication.Models;
using Amr.Communication.Mqtt;
using Amr.Communication.Validation;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Amr.Communication.ConsoleApp.Logging;

// 한 이벤트를 구조화한 뒤 출력 대상별 수준으로 나눈다. 통신/업무 상태는 변경하지 않는다.
internal sealed class TrialEventLogger : IDisposable
{
    private readonly object _gate = new();
    private readonly Logger _logger;
    private readonly LoggingLevelSwitch _consoleLevel;
    private readonly string _amrId;
    private readonly bool _showPayload;
    private readonly Dictionary<string, string> _states = new(StringComparer.Ordinal);
    private string? _sessionId;
    internal string RunId { get; }

    internal TrialEventLogger(TrialConsoleDisplay display, TrialConsoleOptions options, StructuredLog? seq)
    {
        _amrId = options.AmrId; _showPayload = options.ShowPayload;
        RunId = seq?.RunId ?? Guid.NewGuid().ToString("N");
        _consoleLevel = new(options.ConsoleDetail ? LogEventLevel.Debug : LogEventLevel.Information);
        var configuration = new LoggerConfiguration().MinimumLevel.Debug()
            .Enrich.WithProperty("Application", "Amr.Communication.Console").Enrich.WithProperty("RunId", RunId)
            .Enrich.WithProperty("Host", options.Connection.Host).Enrich.WithProperty("Port", options.Connection.Port)
            .WriteTo.Sink(new ConsoleSink(display), levelSwitch: _consoleLevel);
        if (seq is not null) configuration.WriteTo.Sink(new SeqForwardSink(seq), restrictedToMinimumLevel: LogEventLevel.Debug);
        _logger = configuration.CreateLogger();
        Write(LogEventLevel.Information, "TrialLogConfigured", "로그설정",
            "콘솔={ConsoleLevel:l} / Seq={SeqStatus:l} / RunId={RunId:l}",
            ("ConsoleLevel", _consoleLevel.MinimumLevel.ToString()),
            ("SeqStatus", seq is null ? "미설정 · 상세 로그 보관 안 됨" : "Debug 전송 설정 · 실제 수집은 Seq에서 확인"));
    }

    internal void SetConsoleDetail(bool enabled)
    {
        _consoleLevel.MinimumLevel = enabled ? LogEventLevel.Debug : LogEventLevel.Information;
        Write(LogEventLevel.Information, "ConsoleDetailChanged", "출력설정", "콘솔 상세 출력 {Detail:l}", ("Detail", enabled ? "ON" : "OFF"));
    }

    internal void Write(LogEventLevel level, string eventType, string kind, string template,
        params (string Name, object? Value)[] properties)
    {
        lock (_gate)
        {
            var context = _logger.ForContext("EventType", eventType).ForContext("ConsoleKind", kind).ForContext("AmrId", _amrId);
            if (_sessionId is not null) context = context.ForContext("SessionId", _sessionId);
            foreach (var (name, value) in properties)
            {
                context = context.ForContext(name, value is string text && text.Length > 512 ? text[..512] : value);
                if (value is string original && original.Length > 512) context = context.ForContext(name + "Truncated", true);
            }
            context.Write(level, template);
        }
    }

    internal void Command(string eventType, string kind, CommandMessage command, string detail,
        LogEventLevel level = LogEventLevel.Information) =>
        Write(level, eventType, kind, "AMR={AmrId:l} 명령={CommandType:l} cmdId={CommandId:l} {Detail:l}",
            ("CommandType", command.Type), ("CommandId", command.CommandId),
            ("X", command.Parameters.X), ("Y", command.Parameters.Y), ("Detail", detail));

    internal void Observe(CommunicationSessionEvent entry, Guid? sessionId)
    {
        lock (_gate)
        {
            if (entry.Name == "Connecting") { _states.Clear(); _sessionId = null; }
            if (sessionId is not null) _sessionId = sessionId.Value.ToString("N");
            switch (entry.Name)
            {
                case "Received" when entry.Value is ReceivedMessage message: Receive(message); return;
                case "MessageDiagnostic" when entry.Value is MessageDiagnostic diagnostic:
                    Write(LogEventLevel.Debug, "MessageDiagnostic", "내용", "메시지 검사 Topic={Topic:l} bytes={PayloadBytes} 정상={IsValid}",
                        ("Topic", diagnostic.Topic), ("PayloadBytes", diagnostic.PayloadBytes), ("IsValid", diagnostic.IsValid),
                        ("ReceivedAtUtc", diagnostic.ReceivedAtUtc), ("PayloadTruncated", diagnostic.PayloadTruncated),
                        ("ConsolePreview", _showPayload && diagnostic.IsValid ? diagnostic.PayloadPreview : null)); return;
                case "Rejected" when entry.Value is ValidationError validation:
                    Write(LogEventLevel.Warning, "MessageRejected", "검사거부", "Code={Code:l} Field={Field:l} Reason={Reason:l}",
                        ("Code", validation.Code.ToString()), ("Field", validation.Field), ("Reason", validation.Reason)); return;
                case "CallbackFailed" when entry.Value is CallbackFailure failure:
                    Write(LogEventLevel.Error, entry.Name, "처리오류", "단계={Stage:l} 예외={ExceptionType:l}",
                        ("Stage", failure.Stage), ("ExceptionType", failure.ExceptionType)); return;
                case "QueueEvent" when entry.Value is QueueProcessingEvent queue:
                    Write(queue.Code is "HandlerFailed" or "StopTimeout" ? LogEventLevel.Error : queue.Code == "ShutdownDiscarded" ? LogEventLevel.Warning : LogEventLevel.Debug,
                        "Queue" + queue.Code, "처리상태", "Code={Code:l} AMR={AmrId:l} Kind={Kind:l} Count={Count} 예외={ExceptionType:l}",
                        ("Code", queue.Code), ("AmrId", queue.AmrId ?? _amrId), ("Kind", queue.Kind), ("Count", queue.Count), ("ExceptionType", queue.ExceptionType)); return;
                case "ReconnectWaiting" or "ReconnectAttempt" when entry.Value is ReconnectNotice notice:
                    Write(LogEventLevel.Warning, entry.Name, entry.Name == "ReconnectWaiting" ? "재접속대기" : "재접속시도",
                        "{DelaySeconds}초 간격 {Attempt}/{Maximum}회 · 원인={Reason:l}",
                        ("Attempt", notice.Attempt), ("Maximum", notice.Maximum), ("DelaySeconds", notice.DelaySeconds), ("Reason", notice.Reason)); return;
            }
            var (level, kind, text) = entry.Name switch
            {
                "Connecting" => (LogEventLevel.Information, "접속", "서버 연결을 시도합니다."),
                "Subscribed" => (LogEventLevel.Information, "구독", "Heartbeat·Telemetry·Alarm·ACK 구독 승인"),
                "Ready" => (LogEventLevel.Information, "접속완료", "CONNECTED · 구독 완료, 송수신 준비 완료"),
                "Reconnected" => (LogEventLevel.Information, "재접속완료", "RECONNECTED · 구독 복구, 송수신 준비 완료"),
                "Failed" => (LogEventLevel.Warning, Equals(entry.Value, "BrokerDisconnected") ? "연결끊김" : "접속/세션실패", entry.Value as string ?? "세션 실패"),
                "AttemptFailed" => (LogEventLevel.Warning, "시도실패", entry.Value as string ?? "접속 실패"),
                "RetriesExhausted" => (LogEventLevel.Error, "복구실패", "재접속 횟수를 모두 사용했습니다. 종료합니다."),
                "FatalSessionFailure" => (LogEventLevel.Error, "세션중단", entry.Value as string ?? "세션 실패"),
                "CleanupFailed" => (LogEventLevel.Error, "정리실패", "이전 연결 정리에 실패하여 새 연결을 시작하지 않습니다."),
                "SupervisorStopped" => (LogEventLevel.Information, "종료", "통신 연결과 소유 작업 정리를 완료했습니다."),
                "HeartbeatMonitoringStarted" => (LogEventLevel.Information, "생존확인", "정상 생존 신호 수신, 감시 시작"),
                "HeartbeatTimedOut" => (LogEventLevel.Warning, "AMR무응답", "생존 신호 5초 이상 없음 · 서버 단절과 구분"),
                "HeartbeatRecovered" => (LogEventLevel.Information, "생존복구", "정상 생존 신호를 다시 수신했습니다."),
                _ => (LogEventLevel.Debug, "통신상세", entry.Name)
            };
            Write(level, entry.Name, kind, "{Detail:l}", ("Detail", text));
            if (entry.Name == "Failed") _states.Clear();
        }
    }

    private void Receive(ReceivedMessage message)
    {
        string topic = $"amr/{message.AmrId}/{message.Kind}";
        switch (message.Model)
        {
            case TelemetryMessage telemetry:
                var properties = new (string, object?)[] { ("AmrId", message.AmrId), ("Kind", message.Kind), ("Topic", topic),
                    ("X", telemetry.Position.X), ("Y", telemetry.Position.Y), ("Speed", telemetry.Speed), ("State", telemetry.State),
                    ("DeviceTimestamp", telemetry.Timestamp), ("ReceivedAtUtc", message.ReceivedAtUtc) };
                const string template = "AMR={AmrId:l} 종류=telemetry 위치=({X},{Y}) 속도={Speed} 상태={State:l} ts={DeviceTimestamp}";
                Write(LogEventLevel.Debug, "TelemetryReceived", "수신상세", template, properties);
                bool seen = _states.TryGetValue(message.AmrId, out string? previous);
                if (!seen || previous != telemetry.State)
                    Write(LogEventLevel.Information, !seen ? "TelemetryResumed" : "AmrStateChanged", "수신·검사통과", template, properties);
                _states[message.AmrId] = telemetry.State; break;
            case HeartbeatMessage heartbeat:
                Write(LogEventLevel.Debug, "HeartbeatReceived", "수신상세", "AMR={AmrId:l} 종류=heartbeat status={HeartbeatStatus:l} ts={DeviceTimestamp}",
                    ("AmrId", message.AmrId), ("Kind", message.Kind), ("Topic", topic), ("HeartbeatStatus", heartbeat.Status),
                    ("DeviceTimestamp", heartbeat.Timestamp), ("ReceivedAtUtc", message.ReceivedAtUtc)); break;
            case AckMessage ack:
                Write(ack.Result == "FAILURE" ? LogEventLevel.Warning : LogEventLevel.Information, "AckReceived", "수신·검사통과",
                    "AMR={AmrId:l} 종류=ack cmdId={CommandId:l} result={Result:l} reason={Reason:l} ts={DeviceTimestamp} (응답 수신값)",
                    ("AmrId", message.AmrId), ("Kind", message.Kind), ("Topic", topic), ("CommandId", ack.CommandId), ("Result", ack.Result),
                    ("AckAmrId", ack.Id), ("Reason", ack.Reason), ("DeviceTimestamp", ack.Timestamp), ("ReceivedAtUtc", message.ReceivedAtUtc)); break;
            case AlarmMessage alarm:
                Write(alarm.AlarmState == "CLEARED" ? LogEventLevel.Information : alarm.Level is "ERR" or "FATAL" ? LogEventLevel.Error : alarm.Level == "WARN" ? LogEventLevel.Warning : LogEventLevel.Information,
                    "AlarmReceived", "수신·검사통과", "AMR={AmrId:l} 종류=alarm code={Code:l} level={AlarmLevel:l} alarmState={AlarmState:l}",
                    ("AmrId", message.AmrId), ("Kind", message.Kind), ("Topic", topic), ("Code", alarm.Code),
                    ("AlarmLevel", alarm.Level), ("AlarmState", alarm.AlarmState), ("DeviceTimestamp", alarm.Timestamp), ("ReceivedAtUtc", message.ReceivedAtUtc)); break;
        }
    }

    public void Dispose() => _logger.Dispose();

    private sealed class ConsoleSink(TrialConsoleDisplay display) : ILogEventSink
    {
        public void Emit(LogEvent entry)
        {
            string Scalar(string name) => entry.Properties.TryGetValue(name, out var value) && value is ScalarValue scalar ? scalar.Value?.ToString() ?? "" : "";
            string message = Scalar("ConsolePreview") is { Length: > 0 } preview
                ? $"Topic={Scalar("Topic")} JSON={preview}{(Scalar("PayloadTruncated") == "True" ? " (일부 표시)" : "")}"
                : entry.RenderMessage(CultureInfo.InvariantCulture);
            string? color = entry.Level >= LogEventLevel.Error ? "31" : entry.Level == LogEventLevel.Warning ? "33" : "36";
            display.Line(Scalar("ConsoleKind"), message, color);
        }
    }

    private sealed class SeqForwardSink(StructuredLog seq) : ILogEventSink
    {
        public void Emit(LogEvent entry)
        {
            // 콘솔 전용의 제한 미리보기/표시 속성은 Seq로 보내지 않는다.
            var fields = entry.Properties.Where(p => p.Key is not ("ConsoleKind" or "ConsolePreview" or "ConsolePreviewTruncated"))
                .Select(p => new LogEventProperty(p.Key, p.Value));
            seq.Write(new LogEvent(entry.Timestamp, entry.Level, null, entry.MessageTemplate, fields));
        }
    }
}
