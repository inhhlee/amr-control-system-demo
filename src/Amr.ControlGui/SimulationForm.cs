using Amr.Core;

namespace Amr.ControlGui;

// 이 창의 대상 ID는 생성 시 고정된다. 관제 화면에서 다른 AMR을 선택해도 바뀌지 않는다.
public sealed class SimulationForm : Form
{
    private readonly MockAmrDataProvider _provider;
    private readonly ICommandDispatcher _dispatcher;
    private readonly string _amrId;
    private readonly Label _state, _sourceName, _sourceCoordinates, _destinationName, _destinationCoordinates, _alarm, _response, _reason;
    private readonly Button _sourceSettings, _start, _stop;
    private AmrSnapshot _device;
    private string? _commandId, _rejection;
    private bool _busy;

    public SimulationForm(AmrSnapshot device, MockAmrDataProvider provider, ICommandDispatcher dispatcher)
    {
        _device = device;
        _amrId = device.AmrId;
        _provider = provider;
        _dispatcher = dispatcher;
        Name = "simulationWindow";
        Text = $"AMR 독립 가상 시뮬레이션 · {_amrId}";
        Font = new Font("맑은 고딕", 10);
        BackColor = Color.FromArgb(248, 250, 252);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1000, 680);
        MinimumSize = new Size(760, 620);
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(32, 20, 32, 20), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(TextBlock("simulationAmrId", _amrId, 20, true), 0, 0);
        _state = TextBlock("simulationState", "현재 상태\n대기", 13, true);
        _state.BackColor = Color.White;
        _state.Padding = new Padding(12, 6, 0, 0);
        header.Controls.Add(_state, 1, 0);
        root.Controls.Add(header, 0, 0);

        var coordinates = TwoColumns();
        var source = Card("출발지", out var sourceBody);
        var sourceLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        sourceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        sourceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        sourceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        sourceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _sourceName = TextBlock("sourceName", "", 18, true);
        _sourceCoordinates = TextBlock("sourceCoordinates", "", 13);
        _sourceSettings = new Button { Name = "sourceSettings", AccessibleName = "출발지 설정", Text = "설정",
            Dock = DockStyle.Top, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = Color.White };
        _sourceSettings.Click += (_, _) => OpenSourceSettings();
        sourceLayout.Controls.Add(_sourceName, 0, 0);
        sourceLayout.Controls.Add(_sourceSettings, 1, 0);
        sourceLayout.Controls.Add(_sourceCoordinates, 0, 1);
        sourceLayout.SetColumnSpan(_sourceCoordinates, 2);
        sourceBody.Controls.Add(sourceLayout);
        var destination = Card("도착지", out var destinationBody);
        _destinationName = TextBlock("destinationName", "", 18, true);
        _destinationName.Dock = DockStyle.Top;
        _destinationName.Height = 48;
        _destinationCoordinates = TextBlock("destinationCoordinates", "", 13);
        destinationBody.Controls.Add(_destinationCoordinates);
        destinationBody.Controls.Add(_destinationName);
        coordinates.Controls.Add(source, 0, 0);
        coordinates.Controls.Add(destination, 1, 0);
        root.Controls.Add(coordinates, 0, 1);

        var status = TwoColumns();
        var alarm = Card("알람", out var alarmBody);
        _alarm = TextBlock("simulationAlarm", "발생 알람 없음\n0건", 13);
        alarmBody.Controls.Add(_alarm);
        var response = Card("응답", out var responseBody);
        _response = TextBlock("simulationResponse", "요청 없음", 15, true);
        _response.Dock = DockStyle.Top;
        _response.Height = 60;
        _reason = TextBlock("simulationReason", "사유  —", 11);
        responseBody.Controls.Add(_reason);
        responseBody.Controls.Add(_response);
        status.Controls.Add(alarm, 0, 0);
        status.Controls.Add(response, 1, 0);
        root.Controls.Add(status, 0, 2);

        var commands = TwoColumns();
        commands.Padding = new Padding(0, 24, 0, 12);
        _start = CommandButton("simulationStart", "시작");
        _stop = CommandButton("simulationStop", "Stop");
        // 화면의 '시작'은 기존 명령 계약의 MOVE에 대응한다. 버튼이 직접 좌표를 이동시키지 않는다.
        _start.Click += async (_, _) => await SendAsync(CommandKind.MOVE);
        _stop.Click += async (_, _) => await SendAsync(CommandKind.STOP);
        commands.Controls.Add(_start, 0, 0);
        commands.Controls.Add(_stop, 1, 0);
        root.Controls.Add(commands, 0, 3);
        root.Controls.Add(TextBlock("simulationMode", "Mock AMR · 가상 데이터 · 창을 닫으면 이 AMR의 가상 이동이 정지합니다.", 9), 0, 4);
        Controls.Add(root);
        FormClosing += (_, _) => _provider.StopSimulationOnWindowClose(_amrId);
    }

    // 설정창은 후보 선택만 담당한다. '선택·적용'으로 닫힌 경우에만 공유 공급자를 갱신한다.
    // 닫기/Escape는 출발지·좌표·이력을 변경하지 않으며, 실제 적용 가능 상태는 공급자가 재검사한다.
    private void OpenSourceSettings()
    {
        if (_busy || _device.Journey!.Phase != SimulationPhase.Ready) return;
        using var dialog = new SourceSelectionForm(_amrId, _device.Journey.Source);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedSource is not { } station) return;
        try
        {
            _provider.SetSimulationSource(_amrId, station.Name);
            _rejection = null;
        }
        catch (Exception error) { _rejection = error.Message; }
        RenderCurrent();
    }

    // 대상 ID와 목적지를 Dispatcher에 전달하고 접수 결과/거부 사유를 받는다.
    // 접수된 요청 ID를 기억해, 다른 요청의 후속 결과와 섞이지 않도록 조회·표시한다.
    private async Task SendAsync(CommandKind kind)
    {
        if (_busy) return;
        _busy = true;
        _rejection = null;
        UpdateButtons();
        try
        {
            var goal = _device.Journey!.Destination;
            var result = await _dispatcher.DispatchAsync(new(_amrId, kind, kind == CommandKind.MOVE ? goal.X : null, kind == CommandKind.MOVE ? goal.Y : null));
            if (result.Accepted) _commandId = result.CommandId;
            else _rejection = result.Reason;
        }
        catch (Exception error) { _rejection = error.Message; }
        finally { _busy = false; if (!IsDisposed) RenderCurrent(); }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var area = Screen.FromControl(Owner ?? this).WorkingArea;
        Size = new Size(Math.Min(Width, area.Width - 32), Math.Min(Height, area.Height - 32));
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
    }

    private void RenderCurrent()
    {
        // Mock 조회/명령은 잠금 내에서 즉시 완료한다. 실제 I/O 공급자를 여기에 주입하지 않는다.
        var device = _provider.GetDeviceAsync(_amrId).GetAwaiter().GetResult()!;
        var alarms = _provider.GetAlarmsAsync(new(_amrId)).GetAwaiter().GetResult();
        var command = _commandId is null ? null : _provider.GetCommandAsync(_amrId, _commandId).GetAwaiter().GetResult();
        Render(device, alarms, command is null ? [] : [command]);
    }

    // 요청 수락(Unconfirmed)과 목적지 도착 완료를 구분한다. 표시의 근거는 공급자 상태/요청 결과다.
    internal void Render(AmrSnapshot device, IEnumerable<AlarmSnapshot> alarms, IEnumerable<CommandSnapshot> commands)
    {
        if (IsDisposed) return;
        _device = device;
        var journey = device.Journey!;
        _state.Text = "현재 상태\n" + (device.State == AmrState.FAULT ? "오류" : PhaseText(journey.Phase));
        _sourceName.Text = journey.Source.Name;
        _sourceCoordinates.Text = $"X {journey.Source.X:0.0} m / Y {journey.Source.Y:0.0} m";
        _destinationName.Text = journey.Destination.Name;
        _destinationCoordinates.Text = $"X {journey.Destination.X:0.0} m / Y {journey.Destination.Y:0.0} m";
        var currentAlarms = alarms.Where(x => x.AmrId == _amrId && x.IsActive).ToArray();
        _alarm.Text = currentAlarms.Length == 0 ? "발생 알람 없음\n\n0건" : string.Join("\n\n", currentAlarms.Select(x =>
            $"{MockAmrDataProvider.SeverityText(x.Severity)} · {x.Message} · 발생 중\n비고: {x.Note}"));
        var command = commands.Where(x => x.AmrId == _amrId && (_commandId is null || x.CommandId == _commandId)).OrderByDescending(x => x.RequestedAt).FirstOrDefault();
        _response.Text = _rejection is not null ? "요청 거부" : command is null ? "요청 없음"
            : command.Result == CommandResult.Unconfirmed ? "요청 수락 · 실행 중"
            : journey.Phase == SimulationPhase.Arrived ? "목적지 도착 · 실행 완료" : "Stop · 정지 완료";
        _response.ForeColor = _rejection is not null ? Color.Firebrick : journey.Phase == SimulationPhase.Arrived ? Color.ForestGreen : Color.FromArgb(51, 65, 85);
        _reason.Text = "사유  " + (_rejection ?? command?.Reason ?? "—");
        UpdateButtons();
    }

    // 대기: 출발지 설정·시작 허용 / 이동: Stop 허용 / 정지·도착: 둘 다 비활성.
    // 여기서는 UI 조작만 제한하며, 잘못된 직접 요청도 공급자에서 다시 검사한다.
    private void UpdateButtons()
    {
        var phase = _device.Journey!.Phase;
        _start.Enabled = !_busy && phase == SimulationPhase.Ready;
        _stop.Enabled = !_busy && phase == SimulationPhase.Moving;
        _sourceSettings.Enabled = !_busy && phase == SimulationPhase.Ready;
        foreach (var button in new[] { _start, _stop })
        {
            button.BackColor = button.Enabled ? Color.FromArgb(31, 41, 55) : Color.FromArgb(226, 232, 240);
            button.ForeColor = button.Enabled ? Color.White : Color.SlateGray;
        }
    }

    internal static string PhaseText(SimulationPhase phase) => phase switch
    {
        SimulationPhase.Ready => "대기", SimulationPhase.Moving => "이동 중", SimulationPhase.Stopped => "정지", _ => "도착"
    };

    internal static Label TextBlock(string name, string text, float size, bool bold = false) => new()
    {
        Name = name, Text = text, Dock = DockStyle.Fill, Font = new Font("맑은 고딕", size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = Color.FromArgb(51, 65, 85), TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false
    };
    private static TableLayoutPanel TwoColumns()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return panel;
    }
    private static Panel Card(string title, out Panel body)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(18, 10, 18, 14), Margin = new Padding(0, 10, 16, 10) };
        var heading = TextBlock("", title, 11, true);
        heading.Dock = DockStyle.Top;
        heading.Height = 32;
        body = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(body);
        panel.Controls.Add(heading);
        return panel;
    }
    private static Button CommandButton(string name, string text) => new()
    {
        Name = name, Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
        Font = new Font("맑은 고딕", 15, FontStyle.Bold), Margin = new Padding(0, 0, 16, 0)
    };
}
