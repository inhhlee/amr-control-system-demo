using Amr.Core;

namespace Amr.ControlGui;

public partial class MainForm
{
    private void ConfigureMqttPanel()
    {
        commandTitle.Text = "선택 AMR 시뮬레이션";
        mapSource.Text = "실제 MQTT 수신 대기 · 아직 위치를 수신하지 않았습니다.";
        if (_provider is Amr.Infrastructure.FleetMqttAmrDataProvider) ConfigureFleetControls();
    }

    // 위치가 없는 초기 상태에서도 서버/재접속 상태를 표시한다. 운행 상태는 수신 원본을 유지한다.
    private void RenderDataConnection()
    {
        if (_dataConnection is not { } connection) return;
        string state = connection.State switch
        {
            DataConnectionState.Connecting => "서버 접속 중",
            DataConnectionState.Connected => "서버 연결됨",
            DataConnectionState.ReconnectWaiting => "연결 끊김 · 재접속 대기",
            DataConnectionState.Reconnecting => "재접속 시도 중",
            DataConnectionState.Failed => "통신 복구 실패",
            DataConnectionState.Stopped => "통신 종료",
            _ => "서버 연결 끊김"
        };
        communicationValue.Text = state;
        if (!_deviceConnections.IsEmpty)
        {
            int connected = _deviceConnections.Values.Count(x => x.State == DataConnectionState.Connected);
            state = $"서버 연결 {connected}/{_deviceConnections.Count}";
            communicationValue.Text = state;
        }
        communicationValue.ForeColor = connection.State == DataConnectionState.Connected ? Color.DarkGreen : Color.Firebrick;
        communicationDetail.Text = connection.State == DataConnectionState.ReconnectWaiting
            ? $"{connection.Endpoint}\n재시도 {connection.RetryAttempt}/{connection.MaxAttempts} · {connection.RetryDelaySeconds:0.#}초 간격"
            : $"{connection.Endpoint}\nAMR 응답 상태는 별도로 확인합니다.";
        devicesValue.Text = $"전체(등록) {connection.RegisteredAmrIds.Length}대";
        devicesDetail.Text = $"생존 확인 {_devices.Values.Count(x => x.Liveness == LivenessObservation.Alive)} · 응답 없음 {_devices.Values.Count(x => x.Liveness == LivenessObservation.Unresponsive)}\n위치 수신 {_devices.Count}대 · 미확인 {Math.Max(0, connection.RegisteredAmrIds.Length - _devices.Values.Count(x => x.Liveness != LivenessObservation.Unknown))}대";
        string freshness = !_deviceConnections.IsEmpty
            ? $"현재 위치 수신 {_deviceConnections.Values.Count(x => x.TelemetryIsCurrent)}/{_deviceConnections.Count}대 · 장비별 연결 확인"
            : _devices.Count == 0 ? "위치 수신 대기"
            : connection.TelemetryIsCurrent ? "현재 연결에서 수신한 위치·상태"
            : "마지막 수신값 유지 · 현재 위치·운행 미확인";
        mapSource.Text = $"실제 MQTT · {freshness}";
        if (!connection.TelemetryIsCurrent && _deviceConnections.IsEmpty)
        {
            operationValue.Text = _devices.Count == 0 ? "운행 정보 수신 대기" : "현재 운행 미확인";
            operationDetail.Text = "새 운행 정보 수신 대기";
        }
        else
        {
            var alive = _devices.Values.Where(x => x.Liveness == LivenessObservation.Alive && ConnectionFor(x.AmrId)?.TelemetryIsCurrent == true).ToArray();
            operationValue.Text = $"대기 {alive.Count(x => x.State == AmrState.IDLE)} · 이동 {alive.Count(x => x.State == AmrState.BUSY)} · 오류 {alive.Count(x => x.State == AmrState.FAULT)}";
            operationDetail.Text = $"생존 확인 {alive.Length}대 기준 · 정지 {alive.Count(x => x.State == AmrState.STOPPED)}대";
        }
        foreach (DataGridViewRow row in deviceGrid.Rows)
            if (row.Cells[0].Value is string id && _devices.TryGetValue(id, out var device))
                row.Cells[4].Value = StateText(device.State) + (ConnectionFor(id)?.TelemetryIsCurrent == true ? "" : " · 마지막 수신");
        var active = _alarms.Where(x => x.IsActive).ToArray();
        alarmsValue.Text = $"수신된 활성 알람 {active.Length}건";
        alarmsDetail.Text = $"오류 {active.Count(x => x.Severity == AlarmSeverity.Error)}건 · 경고 {active.Count(x => x.Severity == AlarmSeverity.Warning)}건";
        statusLabel.Text = $"{state} · {freshness} · 형식 오류 거부 {connection.RejectedMessages}건";
        if (connection.HistoryAtCapacity) statusLabel.Text += " · 이력 수집 한도 도달(기존 기록 보존)";
        if (connection.DataWarning is not null) statusLabel.Text += " · " + connection.DataWarning;
        if (_historyCount is not null && !_historyCount.Text.Contains("현재 GUI 세션 수신 이력"))
            _historyCount.Text += " · 현재 GUI 세션 수신 이력(DB 저장 아님)";
    }
}
