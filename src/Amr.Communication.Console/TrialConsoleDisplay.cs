using System.Globalization;
using System.Text;
using Amr.Communication.Models;
using Amr.Communication.Monitoring;
using Amr.Communication.Mqtt;
using Amr.Communication.Validation;

namespace Amr.Communication.ConsoleApp;

internal sealed class TrialConsoleDisplay(TextWriter output, bool color, TimeProvider? clock = null)
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public void Header(TrialConsoleOptions options)
    {
        Line("안내", "과제 1 · MQTT 통신 시험 프로그램", "36");
        Line("설정", $"Broker={options.Connection.Host}:{options.Connection.Port} AMR={options.AmrId}");
        Line("설정", $"재시도: {options.Reconnect.Delay.TotalSeconds}초 간격 / 최대 {options.Reconnect.MaxAttempts}회 (최초 시도 제외)");
        Line("안내", "입력: move 10 20 | stop | reset | status | help | /exit  /  Ctrl+C 종료");
        Line("안내", "명령 발행과 응답 수신을 관찰합니다. AMR 실제 수행·요청/응답 업무 매핑은 별도입니다.");
    }

    public void Event(CommunicationSessionEvent entry)
    {
        switch (entry.Name)
        {
            case "Connecting": Line("접속", "서버 연결을 시도합니다.", "36"); break;
            case "Subscribed": Line("구독", "Heartbeat·Telemetry·Alarm·ACK 구독 승인", "32"); break;
            case "Ready": Line("접속완료", "CONNECTED · 구독 완료, 송수신 준비 완료", "32"); break;
            case "Reconnected": Line("재접속완료", "RECONNECTED · 구독 복구, 송수신 준비 완료", "32"); break;
            case "ReconnectWaiting" when entry.Value is ReconnectNotice n:
                Line("재접속대기", $"{n.DelaySeconds}초 뒤 {n.Attempt}/{n.Maximum}회 시도 · 원인={n.Reason}", "33"); break;
            case "ReconnectAttempt" when entry.Value is ReconnectNotice n:
                Line("재접속시도", $"{n.Attempt}/{n.Maximum}회", "33"); break;
            case "Failed":
                Line(Equals(entry.Value, "BrokerDisconnected") ? "연결끊김" : "접속/세션실패", $"{entry.Value}", "31"); break;
            case "AttemptFailed": Line("시도실패", $"{entry.Value}", "33"); break;
            case "RetriesExhausted": Line("복구실패", "재접속 횟수를 모두 사용했습니다. 종료합니다.", "31"); break;
            case "FatalSessionFailure": Line("세션중단", $"원인={entry.Value}", "31"); break;
            case "CleanupFailed": Line("정리실패", "이전 연결 정리에 실패하여 새 연결을 시작하지 않습니다.", "31"); break;
            case "CallbackFailed" when entry.Value is CallbackFailure failure:
                Line("처리오류", $"단계={failure.Stage} 예외={failure.ExceptionType}", "31"); break;
            case "SupervisorStopped": Line("종료", "통신 연결과 소유 작업 정리를 완료했습니다."); break;
            case "Rejected" when entry.Value is ValidationError error:
                Line("검사거부", $"Code={error.Code} Field={error.Field} Reason={error.Reason}", "31"); break;
            case "MessageDiagnostic" when entry.Value is MessageDiagnostic { IsValid: true, PayloadPreview: not null } d:
                Line("내용", $"Topic={d.Topic} JSON={d.PayloadPreview}{(d.PayloadTruncated ? " (일부 표시)" : "")}"); break;
            case "Received" when entry.Value is ReceivedMessage message: Receive(message); break;
            case "HeartbeatMonitoringStarted": Line("생존확인", "정상 생존 신호 수신, 감시 시작", "32"); break;
            case "HeartbeatTimedOut" when entry.Value is HeartbeatObservation h:
                Line("AMR무응답", $"AMR={h.AmrId} 생존 신호 {h.TimeoutMilliseconds / 1000}초 이상 없음 · 서버 단절과 구분", "33"); break;
            case "HeartbeatRecovered": Line("생존복구", "정상 생존 신호를 다시 수신했습니다.", "32"); break;
        }
    }

    private void Receive(ReceivedMessage message)
    {
        string detail = message.Model switch
        {
            TelemetryMessage t => FormattableString.Invariant($"위치=({t.Position.X:G6},{t.Position.Y:G6}) 속도={t.Speed:G6} 상태={t.State} ts={t.Timestamp}"),
            HeartbeatMessage h => $"status={h.Status} ts={h.Timestamp}",
            AlarmMessage a => $"code={a.Code} level={a.Level} alarmState={a.AlarmState} ts={a.Timestamp}",
            AckMessage a => $"cmdId={a.CommandId} result={a.Result} ts={a.Timestamp} (응답 수신값)",
            _ => "지원하지 않는 모델"
        };
        Line("수신·검사통과", $"AMR={message.AmrId} 종류={message.Kind} {detail}", "36");
    }

    public void Line(string kind, string message, string? ansiColor = null)
    {
        lock (_gate)
        {
            string prefix = $"[{_clock.GetLocalNow():HH:mm:ss}] [{kind}] ";
            output.WriteLine(color && ansiColor is not null
                ? $"\u001b[{ansiColor}m{prefix}{Safe(message)}\u001b[0m" : prefix + Safe(message));
            output.Flush();
        }
    }

    private static string Safe(string text)
    {
        var result = new StringBuilder();
        foreach (char c in text.Take(1200))
            if (char.IsControl(c)) result.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else result.Append(c);
        if (text.Length > 1200) result.Append("… (일부 표시)");
        return result.ToString();
    }
}
