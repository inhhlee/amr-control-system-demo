using System.Globalization;
using Amr.Core;

namespace Amr.ControlGui;

public partial class MainForm
{
    private TextBox? _fleetX, _fleetY;
    private Label? _fleetTarget, _fleetResponse;
    private Button? _fleetMove, _fleetStop, _fleetReset;
    private string? _fleetEditingId;
    private readonly Dictionary<string, (string X, string Y)> _fleetDrafts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _fleetRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _fleetErrors = new(StringComparer.Ordinal);

    private void ConfigureFleetControls()
    {
        Text = "AMR 관제 시스템 · Fleet 공통 맵";
        commandTitle.Text = "선택 AMR 제어 · 공통 맵";
        var body = (Panel)Controls.Find("commandBody", true).Single();
        foreach (Control control in body.Controls) control.Visible = false;
        _openSimulation!.Enabled = false;
        var panel = new TableLayoutPanel { Name = "fleetControlPanel", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        panel.RowStyles.Add(new(SizeType.Absolute, 25));
        panel.RowStyles.Add(new(SizeType.Absolute, 30));
        panel.RowStyles.Add(new(SizeType.Absolute, 32));
        panel.RowStyles.Add(new(SizeType.Percent, 100));
        _fleetTarget = TextLabel("fleetTarget", "AMR 선택 · 위치 수신 대기", 9, Color.SlateGray);
        var coords = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        coords.ColumnStyles.Add(new(SizeType.Absolute, 20)); coords.ColumnStyles.Add(new(SizeType.Percent, 50));
        coords.ColumnStyles.Add(new(SizeType.Absolute, 20)); coords.ColumnStyles.Add(new(SizeType.Percent, 50));
        _fleetX = new TextBox { Name = "fleetTargetX", AccessibleName = "목적지 X", Dock = DockStyle.Fill };
        _fleetY = new TextBox { Name = "fleetTargetY", AccessibleName = "목적지 Y", Dock = DockStyle.Fill };
        coords.Controls.Add(new Label { Text = "X", Dock = DockStyle.Fill }, 0, 0); coords.Controls.Add(_fleetX, 1, 0);
        coords.Controls.Add(new Label { Text = "Y", Dock = DockStyle.Fill }, 2, 0); coords.Controls.Add(_fleetY, 3, 0);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        for (int i = 0; i < 3; i++) buttons.ColumnStyles.Add(new(SizeType.Percent, 100f / 3));
        Button Command(string name, string text, CommandKind kind)
        {
            var button = new Button { Name = name, Text = text, Dock = DockStyle.Fill, Margin = new Padding(1) };
            button.Click += async (_, _) => await SendFleetAsync(kind);
            return button;
        }
        _fleetMove = Command("fleetMove", "시작", CommandKind.MOVE);
        _fleetStop = Command("fleetStop", "STOP", CommandKind.STOP);
        _fleetReset = Command("fleetReset", "RESET", CommandKind.RESET);
        buttons.Controls.Add(_fleetMove, 0, 0); buttons.Controls.Add(_fleetStop, 1, 0); buttons.Controls.Add(_fleetReset, 2, 0);
        _fleetResponse = TextLabel("fleetResponse", "요청 없음", 8, Color.SlateGray);
        _fleetResponse.TextAlign = ContentAlignment.TopLeft;
        panel.Controls.Add(_fleetTarget, 0, 0); panel.Controls.Add(coords, 0, 1);
        panel.Controls.Add(buttons, 0, 2); panel.Controls.Add(_fleetResponse, 0, 3);
        body.Controls.Add(panel); panel.BringToFront();
        // Fleet에서는 장비 목록을 기본 탭으로 보여 선택·공통 맵 관계를 바로 확인한다.
        ((TabControl)Controls.Find("feedbackTabs", true).Single()).SelectedIndex = 1;
        RenderFleetControls();
    }

    private async Task SendFleetAsync(CommandKind kind)
    {
        if (SelectedAmrId is not string id || _dispatcher is null) return;
        // 클릭 시점의 대상/좌표를 고정한다. await 도중 선택이 바뀌어도 새 선택에 결과를 쓰지 않는다.
        string px = _fleetX!.Text, py = _fleetY!.Text;
        await RunButtonActionAsync(async token =>
        {
            _fleetErrors.Remove(id);
            try
            {
                double? x = null, y = null;
                if (kind == CommandKind.MOVE)
                {
                    if (!double.TryParse(px, NumberStyles.Float, CultureInfo.InvariantCulture, out var dx) || !double.IsFinite(dx) ||
                        !double.TryParse(py, NumberStyles.Float, CultureInfo.InvariantCulture, out var dy) || !double.IsFinite(dy))
                        throw new ArgumentException("목적지 X/Y에 유한한 숫자를 입력하세요.");
                    x = dx; y = dy;
                }
                var submission = await _dispatcher.DispatchAsync(new(id, kind, x, y), token);
                if (submission.Accepted) _fleetRequests[id] = submission.CommandId!;
                else _fleetErrors[id] = submission.Reason ?? "명령 거부";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) { _fleetErrors[id] = error.Message; }
            if (!_closing) RenderFleetControls();
        });
    }

    private void RenderFleetControls()
    {
        if (_fleetResponse is null) return;
        string? id = SelectedAmrId;
        if (id != _fleetEditingId)
        {
            if (_fleetEditingId is not null) _fleetDrafts[_fleetEditingId] = (_fleetX!.Text, _fleetY!.Text);
            _fleetEditingId = id;
            var goal = id is not null ? _devices.GetValueOrDefault(id)?.Journey?.Destination : null;
            var draft = id is not null && _fleetDrafts.TryGetValue(id, out var saved) ? saved
                : (goal?.X.ToString(CultureInfo.InvariantCulture) ?? "", goal?.Y.ToString(CultureInfo.InvariantCulture) ?? "");
            _fleetX!.Text = draft.Item1; _fleetY!.Text = draft.Item2;
        }
        var device = id is null ? null : _devices.GetValueOrDefault(id);
        var connection = ConnectionFor(id);
        var commands = _commands.Where(x => x.AmrId == id).ToArray();
        var requestId = id is null ? null : _fleetRequests.GetValueOrDefault(id);
        var command = commands.Where(x => requestId is null || x.CommandId == requestId).OrderByDescending(x => x.RequestSequence).FirstOrDefault();
        bool pendingMove = commands.Any(x => x.Kind == CommandKind.MOVE && x.Result == CommandResult.Unconfirmed && !x.SupersededByConfirmedStop);
        bool pendingStop = commands.Any(x => x.Kind == CommandKind.STOP && x.Result == CommandResult.Unconfirmed && !x.ResultDeadlineExceeded);
        bool pendingReset = commands.Any(x => x.Kind == CommandKind.RESET && x.Result == CommandResult.Unconfirmed && !x.ResultDeadlineExceeded);
        bool ready = !_closing && !_actionBusy && _dispatcher is not null && connection is { State: DataConnectionState.Connected, TelemetryIsCurrent: true }
            && device?.Liveness == LivenessObservation.Alive;
        _fleetMove!.Enabled = ready && device?.State is AmrState.IDLE or AmrState.STOPPED && !pendingMove && !pendingReset
            && !_alarms.Any(x => x.AmrId == id && x.IsActive && x.Severity == AlarmSeverity.Error);
        _fleetStop!.Enabled = ready && !pendingStop && (device?.State == AmrState.BUSY || pendingMove);
        _fleetReset!.Enabled = ready && device?.State == AmrState.FAULT && !pendingReset;
        _fleetX!.Enabled = _fleetY!.Enabled = !_closing && !_actionBusy && device is not null && device.State != AmrState.BUSY && !pendingMove;
        _fleetTarget!.Text = device is null ? "AMR 선택 · 위치 수신 대기" : $"{id} · {device.State} · {LivenessText(device.Liveness)}";
        _fleetTarget.ForeColor = device?.State == AmrState.FAULT ? Color.Firebrick : Color.FromArgb(51, 65, 85);
        string? error = id is null ? null : _fleetErrors.GetValueOrDefault(id);
        _fleetResponse.Text = error is not null ? $"{id} · 요청 거부\n{error}"
            : command is null ? $"{id} · 요청 없음"
            : $"{id} · {command.Kind} · {(command.Result == CommandResult.Unconfirmed ? command.ResultDeadlineExceeded ? "대기 기한 경과 · 미확인" : "최종 응답 대기" : command.Result == CommandResult.Success ? "SUCCESS" : "FAILURE")}\n{command.Reason ?? "수행 결과 미확인"}\n{command.CommandId}";
        _fleetResponse.ForeColor = error is not null || command?.Result == CommandResult.Failure ? Color.Firebrick : Color.FromArgb(51, 65, 85);
        _fleetResponse.AccessibleDescription = _fleetResponse.Text;
    }
}
