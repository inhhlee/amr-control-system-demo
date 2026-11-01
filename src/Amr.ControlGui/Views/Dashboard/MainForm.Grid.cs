using Amr.Core;

namespace Amr.ControlGui;

public partial class MainForm
{
    private GridDashboardFrame? _gridFrame;
    private GridMapState? CurrentGridState => _gridFrame?.State?.Fleet?.FirstOrDefault(x => x.AmrId == SelectedAmrId) ?? _gridFrame?.State;
    private AmrDomainStatus? CurrentDomainStatus => _gridFrame?.State?.Domain?.Statuses.FirstOrDefault(x => x.AmrId == SelectedAmrId);

    private void ConfigureGridMode()
    {
        Text = $"AMR 관제 · 격자 MQTT · {(_provider as GridMapDataProvider)!.Endpoint}";
        modeLabel.Text = "MQTT · 격자 시연";
        mockInputs.Visible = false;
        gridActions.Visible = gridDetails.Visible = true;
        commandGrid.Visible = false;
        mapCaption.Text = "격자 · MQTT 수신 위치 · 장애물 클릭 편집";
        mapSource.Text = "청록: 수신 궤적 · 파랑 점선: 예정 · 주황: 추가 장애물";
        commandTitle.Text = "명령 · 격자 시연 제어";
        submissionLabel.Text = "출발/중단/초기화는 시연 제어입니다. MQTT Command/ACK 업무와 구분합니다.";
        gridDetails.Text = "지도 서버의 상태를 기다립니다.";
        historyNote.Text = "지도 세션 로그를 기다립니다. DB 업무 이력이 아닙니다.";
    }

    private async Task ObserveGridAsync(GridMapDataProvider provider, CancellationToken token)
    {
        try
        {
            await foreach (var frame in provider.WatchGridAsync(token).ConfigureAwait(false))
                await this.InvokeAsync(() => ApplyGridFrame(frame), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
                await this.InvokeAsync(() => statusLabel.Text = $"지도 수신 오류: {error.Message}", token).ConfigureAwait(false);
        }
    }

    internal void ApplyGridFrame(GridDashboardFrame frame)
    {
        if (_closing) return;
        _gridFrame = frame;
        ApplyChange(frame.Change);
        if (frame.State?.Domain is { } domain)
        {
            alarmsValue.Text = $"업무 경보 {domain.Alarms.Length}건";
            alarmsDetail.Text = $"관제 미션 보류 {domain.Statuses.Count(x => x.Mission.State == AmrMissionState.OnHold)}대 · 업무 Command/ACK 미연결";
            alarmSummary.Text = domain.Alarms.IsEmpty ? "현재 통신 업무 경보 없음" : string.Join("\n", domain.Alarms.Select(x => $"{x.AmrId} · {x.Message}"));
            submissionLabel.Text = "선택 AMR 제어 · 통신 복구 후 미션 재개는 명시적으로 요청합니다.\n관제 미션 보류는 차량의 물리적 정지 명령이 아닙니다.";
        }
        else
        {
            alarmsValue.Text = "미확인";
            alarmsDetail.Text = "알람 · 업무 명령 데이터 미연결";
            alarmSummary.Text = "격자 시연에서 알람 데이터를 제공하지 않습니다.";
        }
        mapCanvas.SetGridState(CurrentGridState);
        if (CurrentGridState is { } state)
        {
            string target = state.Navigation.CurrentTarget is { } cell ? $"({cell.Row},{cell.Col})" : "모두 방문";
            gridDetails.Text = $"{PhaseText(state.Navigation.Phase)} · {state.Navigation.Moves}회 이동\n" +
                $"다음 목표 {target} · {DistanceText(state.Navigation.RemainingDistance)}\n" +
                $"경유 {state.Navigation.NextWaypointIndex}/{state.Navigation.Waypoints.Length} · 위치 수신 {state.TelemetryCount} · Heartbeat {state.HeartbeatCount}\n" +
                $"{(CurrentDomainStatus is { } observed ? "AMR 생존 " + LivenessText(observed.Liveness) : HeartbeatText(state.HeartbeatMonitor))}\n수신 세션 {state.BrokerState} · {state.MqttEndpoint}\n{state.Failure}";
            if (CurrentDomainStatus is { } status)
                gridDetails.Text += $"\n{DomainStatusText(status)}";
            historyNote.Text = "격자 세션 로그 (DB 업무 이력 아님)\n" + string.Join("\n", state.Logs.TakeLast(3));
            statusLabel.Text = $"{state.AmrId} · {PhaseText(state.Navigation.Phase)} · 수신 전 위치 없음 · X=열 / Y=-행";
            if (state.Position is not null)
                statusLabel.Text = $"{state.AmrId} · {PhaseText(state.Navigation.Phase)} · {state.Navigation.Moves}회 · MQTT 수신 ({state.Position.Row},{state.Position.Col})";
            ShowSelectedDevice();
        }
        if (frame.Error is not null)
        {
            modeLabel.Text = "수신 오류 · 마지막 수신값";
            statusLabel.Text = frame.Error;
            mapSource.Text = "데이터 갱신 오류 · 마지막 수신 위치/경로";
        }
        else
        {
            modeLabel.Text = frame.State?.Domain is null ? "MQTT · 격자 시연" : "MQTT · Core 관제 · AMR 2대";
            mapSource.Text = "청록: 수신 궤적 · 파랑 점선: 예정 · 주황: 추가 장애물";
        }
        UpdateButtons();
    }

    private async void GridStartClicked(object? sender, EventArgs e) => await GridActionAsync((p, t) =>
        _gridFrame?.State?.Domain is not null ? p.FleetActionAsync("start", SelectedAmrId!, t) : p.StartAsync(t));
    private async void GridCancelClicked(object? sender, EventArgs e) => await GridActionAsync((p, t) =>
        _gridFrame?.State?.Domain is not null ? p.FleetActionAsync("cancel", SelectedAmrId!, t) : p.CancelAsync(t));
    private async void GridResetClicked(object? sender, EventArgs e) => await GridActionAsync((p, t) =>
        _gridFrame?.State?.Domain is not null ? p.FleetActionAsync("grid/reset", SelectedAmrId!, t) : p.ResetAsync(t));
    private async void GridObstacleRequested(object? sender, GridCellEdit edit) =>
        await GridActionAsync((p, t) => _gridFrame?.State?.Domain is not null
            ? p.FleetObstacleAsync(SelectedAmrId!, edit, t) : p.SetObstacleAsync(edit, t));
    private Task GridActionAsync(Func<GridMapDataProvider, CancellationToken, Task> action) =>
        _provider is GridMapDataProvider grid && _gridFrame?.Error is null
            ? RunButtonActionAsync(token => action(grid, token)) : Task.CompletedTask;

    private static string PhaseText(string phase) => phase switch {
        "Ready" => "출발 준비", "Connecting" => "MQTT 연결 중", "Running" => "이동 중",
        "Waiting" => "경로 차단 · 대기", "WaitingForTelemetry" => "최종 수신 대기",
        "Completed" => "도착 · 수신 확인", "Canceling" => "중단 정리 중", "Resetting" => "초기화 정리 중",
        "Canceled" => "중단됨", "AmrUnavailable" => "AMR 송신 중단 · 감시 유지",
        "Failed" => "송수신 실패 · 정지", _ => phase
    };
    private static string HeartbeatText(GridHeartbeatObservation? observation) => observation?.State switch {
        "Alive" => "AMR 생존 신호 정상", "Unresponsive" => "AMR 응답 없음 · Broker 상태와 별도",
        "AwaitingFirst" => "첫 정상 Heartbeat 대기", "Stopped" => "Heartbeat 감시 중지", _ => "Heartbeat 감시 미확인"
    };
    private static string DistanceText(int? distance) => distance is int value ? $"남은 {value}칸 ({value}m)" : "경로 없음";
    private static string DomainStatusText(AmrDomainStatus status) => $"관제 통신 {status.Communication}\n관제 미션 " +
        (status.Mission.State switch { AmrMissionState.OnHold => "보류 · 명시적 재개 필요", AmrMissionState.Running => "진행",
            AmrMissionState.Completed => "완료", AmrMissionState.Canceled => "중단", _ => "미할당" });
}
