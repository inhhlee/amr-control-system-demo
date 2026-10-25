using System.Collections.Immutable;
using System.Globalization;
using Amr.Core;

namespace Amr.ControlGui;

public partial class MainForm : Form
{
    private readonly IAmrDataProvider? _provider;
    private readonly ICommandDispatcher? _dispatcher;
    private readonly Func<string, CancellationToken, Task>? _advanceScenario;
    private readonly Func<string, string, bool, CancellationToken, Task>? _completeScenario;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _actions = [];
    private readonly Dictionary<string, AmrSnapshot> _devices = new(StringComparer.Ordinal);
    private ImmutableArray<CommandSnapshot> _commands = [];
    private ImmutableArray<AlarmSnapshot> _alarms = [];
    private Task _watchTask = Task.CompletedTask;
    private long _lastRevision = -1;
    private long _selectionRequest;
    private bool _rendering;
    private bool _actionBusy;
    private bool _closing;
    private bool _closeReady;
    private bool _resourcesDisposed;

    // Visual Studio 디자이너용. 실행 시에는 아래 생성자로 계약과 Mock 동작을 주입한다.
    public MainForm() => InitializeComponent();

    public MainForm(IAmrDataProvider provider, ICommandDispatcher dispatcher,
        Func<string, CancellationToken, Task>? advanceScenario = null,
        Func<string, string, bool, CancellationToken, Task>? completeScenario = null) : this()
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _advanceScenario = advanceScenario;
        _completeScenario = completeScenario;
        modeLabel.Text = provider.Mode == ProviderMode.Mock ? "MOCK · 시험 데이터" : "ACTUAL · 실제 공급자";
        advanceButton.Visible = completeSuccessButton.Visible = completeFailureButton.Visible = provider.Mode == ProviderMode.Mock;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // 실제 창이 놓인 모니터의 작업 영역에 맞춘다. 주 모니터와 다를 수 있다.
        var area = Screen.FromControl(this).WorkingArea;
        Size = new Size(Math.Min(1200, area.Width - 40), Math.Min(900, area.Height - 40));
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        if (_provider is not null) _watchTask = ObserveAsync(_lifetime.Token);
    }

    private async Task ObserveAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await foreach (var change in _provider!.WatchAsync(token).ConfigureAwait(false))
                        await this.InvokeAsync(() => ApplyChange(change), token).ConfigureAwait(false);
                    return;
                }
                catch (AmrResyncRequiredException)
                {
                    await this.InvokeAsync(() => statusLabel.Text = "변경이 많아 최신 상태로 다시 동기화합니다.", token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
                await this.InvokeAsync(() => statusLabel.Text = $"데이터 수신 오류: {error.Message}", token).ConfigureAwait(false);
        }
    }

    internal void ApplyChange(AmrChange change)
    {
        if (_closing || change.Revision < _lastRevision) return;
        _lastRevision = change.Revision;
        string? selected = SelectedAmrId;
        _devices.Clear();
        foreach (var device in change.Devices) _devices.Add(device.AmrId, device);
        _commands = change.Commands;
        _alarms = change.Alarms;
        _rendering = true;
        try
        {
            deviceGrid.Rows.Clear();
            foreach (var device in change.Devices)
                deviceGrid.Rows.Add(device.AmrId, Number(device.X), Number(device.Y), Number(device.SpeedMps), StateText(device.State));
            deviceGrid.ClearSelection();
            var row = deviceGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(x => x.Cells[0].Value as string == selected)
                ?? deviceGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault();
            if (row is not null) { row.Selected = true; deviceGrid.CurrentCell = row.Cells[0]; }
            commandGrid.Rows.Clear();
            foreach (var command in _commands.Reverse())
                commandGrid.Rows.Add(command.AmrId, command.CommandId, "접수됨", ResultText(command.Result), command.Reason ?? "결과 미확인");
        }
        finally { _rendering = false; }
        ShowSelectedDevice();
        RefreshDashboard();
        statusLabel.Text = $"장비 {change.Devices.Length}대 · 알람 {change.Alarms.Length}건 · 요청 {_commands.Length}건 · 상태 버전 {change.Revision}";
        UpdateButtons();
    }

    private string? SelectedAmrId => deviceGrid.SelectedRows.Count == 0 ? null : deviceGrid.SelectedRows[0].Cells[0].Value as string;
    internal string SelectedDetails => detailsLabel.Text;
    internal string? SelectedDeviceId => SelectedAmrId;

    private async void DeviceSelectionChanged(object? sender, EventArgs e)
    {
        if (_rendering || _closing || _provider is null || SelectedAmrId is not string id) return;
        ShowSelectedDevice();
        UpdateButtons();
        long request = ++_selectionRequest;
        await TrackAsync(async token =>
        {
            var result = await _provider.GetDeviceAsync(id, token);
            // 선택이 바뀌거나 더 최신 통지를 받았다면 늦은 조회 결과를 적용하지 않는다.
            if (request == _selectionRequest && SelectedAmrId == id && result is not null) ApplyDeviceQuery(result);
        });
    }

    internal void ApplyDeviceQuery(AmrSnapshot result)
    {
        if (_closing || !_devices.TryGetValue(result.AmrId, out var current) || result.Revision < current.Revision) return;
        _lastRevision = Math.Max(_lastRevision, result.Revision);
        _devices[result.AmrId] = result;
        foreach (DataGridViewRow row in deviceGrid.Rows)
        {
            if (row.Cells[0].Value as string != result.AmrId) continue;
            row.Cells[1].Value = Number(result.X);
            row.Cells[2].Value = Number(result.Y);
            row.Cells[3].Value = Number(result.SpeedMps);
            row.Cells[4].Value = StateText(result.State);
        }
        ShowSelectedDevice();
        RefreshDashboard();
    }

    private void RefreshDashboard()
    {
        var devices = _devices.Values.ToArray();
        int CountConnection(ConnectionObservation value) => devices.Count(x => x.BrokerConnection == value);
        int CountLiveness(LivenessObservation value) => devices.Count(x => x.Liveness == value);
        int CountState(AmrState value) => devices.Count(x => x.State == value);
        communicationValue.Text = devices.Length == 0 ? "데이터 없음"
            : CountConnection(ConnectionObservation.Disconnected) > 0 ? "연결 끊김 관측"
            : CountConnection(ConnectionObservation.Unknown) > 0 ? "연결 미확인" : "연결 관측됨";
        communicationValue.ForeColor = CountConnection(ConnectionObservation.Disconnected) > 0 ? Color.Firebrick : Color.FromArgb(51, 65, 85);
        communicationDetail.Text = $"연결 {CountConnection(ConnectionObservation.Connected)} · 끊김 {CountConnection(ConnectionObservation.Disconnected)} · 미확인 {CountConnection(ConnectionObservation.Unknown)}";
        devicesValue.Text = $"전체 {devices.Length}대";
        devicesDetail.Text = $"생존 확인 {CountLiveness(LivenessObservation.Alive)} · 응답 없음 {CountLiveness(LivenessObservation.Unresponsive)}\n미확인 {CountLiveness(LivenessObservation.Unknown)}";
        operationValue.Text = $"대기 {CountState(AmrState.IDLE)} · 이동 {CountState(AmrState.BUSY)}";
        operationDetail.Text = $"정지 {CountState(AmrState.STOPPED)} · 오류 {CountState(AmrState.FAULT)}";
        alarmsValue.Text = $"현재 {_alarms.Length}건";
        alarmsValue.ForeColor = _alarms.IsEmpty ? Color.FromArgb(51, 65, 85) : Color.Firebrick;
        alarmsDetail.Text = $"명령 대기 {_commands.Count(x => x.Result == CommandResult.Unconfirmed)} · 성공 {_commands.Count(x => x.Result == CommandResult.Success)} · 실패 {_commands.Count(x => x.Result == CommandResult.Failure)}";
        alarmSummary.Text = _alarms.IsEmpty ? "현재 알람 없음\n수신된 알람 0건" : string.Join("\n", _alarms.Take(3).Select(x => $"{x.AmrId} · {x.Code}\n{x.Message}"));
    }

    private void MapDeviceSelected(object? sender, EventArgs e)
    {
        if (_closing || mapCanvas.SelectedAmrId is not string id) return;
        var row = deviceGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(x => x.Cells[0].Value as string == id);
        if (row is null) return;
        deviceGrid.ClearSelection();
        row.Selected = true;
        deviceGrid.CurrentCell = row.Cells[0];
    }

    private void ShowSelectedDevice()
    {
        mapCanvas.SetScene(_devices.Values, SelectedAmrId);
        if (SelectedAmrId is not string id || !_devices.TryGetValue(id, out var device))
        {
            detailsLabel.Text = "장비를 선택하세요.";
            return;
        }
        detailsLabel.Text = $"{device.AmrId}\n\nX {Number(device.X)} m / Y {Number(device.Y)} m\n속도  {Number(device.SpeedMps)} m/s\n운행  {StateText(device.State)}\n\nBroker  {ConnectionText(device.BrokerConnection)}\n생존  {LivenessText(device.Liveness)}\nHeartbeat  {TimeText(device.LastHeartbeatReceivedAt)}\nTelemetry  {TimeText(device.LastTelemetryReceivedAt)}";
    }

    private async void AdvanceClicked(object? sender, EventArgs e)
    {
        if (SelectedAmrId is not string id || _advanceScenario is null) return;
        await RunButtonActionAsync(token => Task.Run(() => _advanceScenario(id, token), token));
    }

    private async void SubmitClicked(object? sender, EventArgs e)
    {
        if (SelectedAmrId is not string id || _dispatcher is null) return;
        var request = new CommandRequest(id, CommandKind.MOVE, (double)targetX.Value, (double)targetY.Value);
        await RunButtonActionAsync(async token =>
        {
            var submission = await _dispatcher.DispatchAsync(request, token);
            submissionLabel.Text = submission.Accepted
                ? $"{id} 접수됨 · {submission.CommandId}\n접수는 실행 완료가 아닙니다. 아래에서 후속 결과를 확인하세요."
                : $"접수 거부: {submission.Reason}";
        });
    }

    private async void CompleteSuccessClicked(object? sender, EventArgs e) => await CompleteSelectedAsync(true);
    private async void CompleteFailureClicked(object? sender, EventArgs e) => await CompleteSelectedAsync(false);

    private Task CompleteSelectedAsync(bool success)
    {
        var command = _commands.FirstOrDefault(x => x.AmrId == SelectedAmrId && x.Result == CommandResult.Unconfirmed);
        if (command is null || _completeScenario is null) return Task.CompletedTask;
        return RunButtonActionAsync(token => Task.Run(() => _completeScenario(command.AmrId, command.CommandId, success, token), token));
    }

    private async Task RunButtonActionAsync(Func<CancellationToken, Task> action)
    {
        if (_closing || _actionBusy) return;
        _actionBusy = true;
        UpdateButtons();
        try { await TrackAsync(action); }
        finally { _actionBusy = false; if (!_closing) UpdateButtons(); }
    }

    private async Task TrackAsync(Func<CancellationToken, Task> action)
    {
        if (_closing) return;
        var task = RunObservedAsync(action);
        _actions.Add(task);
        try { await task; }
        finally { _actions.Remove(task); }
    }

    private async Task RunObservedAsync(Func<CancellationToken, Task> action)
    {
        try { await action(_lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_closing) statusLabel.Text = $"작업 오류: {error.Message}"; }
    }

    private void UpdateButtons()
    {
        bool ready = !_closing && !_actionBusy && SelectedAmrId is not null;
        advanceButton.Enabled = ready && _advanceScenario is not null;
        submitButton.Enabled = ready && _dispatcher is not null;
        bool pending = ready && _completeScenario is not null && _commands.Any(x => x.AmrId == SelectedAmrId && x.Result == CommandResult.Unconfirmed);
        completeSuccessButton.Enabled = completeFailureButton.Enabled = pending;
    }

    private async void MainFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closeReady) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        Enabled = false;
        _lifetime.Cancel();
        try
        {
            // 구독이 취소/실패해도 이미 시작한 조회·버튼 작업의 정리가 끝날 때까지 기다린다.
            await Task.WhenAll(_actions.Append(_watchTask).ToArray());
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { _closeReady = true; Close(); }
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string TimeText(DateTimeOffset? value) => value?.ToLocalTime().ToString("HH:mm:ss") ?? "미관측";
    private static string ConnectionText(ConnectionObservation value) => value switch { ConnectionObservation.Connected => "연결됨", ConnectionObservation.Disconnected => "연결 끊김", _ => "미확인" };
    private static string LivenessText(LivenessObservation value) => value switch { LivenessObservation.Alive => "확인됨", LivenessObservation.Unresponsive => "응답 없음", _ => "미확인" };
    internal static string StateText(AmrState value) => value switch { AmrState.IDLE => "대기", AmrState.BUSY => "이동", AmrState.STOPPED => "정지", AmrState.FAULT => "오류", _ => "미확인" };
    private static string ResultText(CommandResult value) => value switch { CommandResult.Success => "성공 (Mock)", CommandResult.Failure => "실패 (Mock)", _ => "결과 미확인" };
}
