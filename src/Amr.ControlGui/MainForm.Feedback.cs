using System.Collections.Immutable;
using System.Diagnostics;
using Amr.Core;

namespace Amr.ControlGui;

// MainForm.Designer.cs의 기본 배치에 Figma 피드백 화면을 구성하는 부분 클래스다.
// 이 파일은 표시·선택·창 수명을 담당하고, 가상 이동과 명령 허용 판단은 Mock 공급자가 담당한다.
public partial class MainForm
{
    private Button? _openSimulation;
    private DataGridView? _alarmGrid, _historyGrid;
    private Label? _alarmDetail, _simulationTarget, _historyDetail, _historyCount, _zoomLabel;
    private TextBox? _historyAmr;
    private ComboBox? _historyType;
    private DateTimePicker? _historyFrom, _historyTo;
    private ImmutableArray<HistoryEntry> _historyEntries = [];
    private readonly Dictionary<string, SimulationForm> _simulationWindows = new(StringComparer.Ordinal);
    private System.Windows.Forms.Timer? _simulationTimer;
    private long _lastTick;

    // 대시보드·지도 도구·알람·별도 창 열기·이력 영역을 연결한다. Mock 피드백 모드에서만 호출한다.
    private void ConfigureFeedbackView()
    {
        Text = "AMR 관제 시스템 · Mock";
        MinimumSize = new Size(1000, 800);
        BackColor = Color.White;
        modeLabel.Text = "MOCK · 가상 데이터";
        operationValue.Font = new Font("맑은 고딕", 11, FontStyle.Bold);
        alarmsValue.Font = new Font("맑은 고딕", 12, FontStyle.Bold);
        var root = (TableLayoutPanel)Controls.Find("rootLayout", true).Single();
        root.RowStyles[0].Height = 156;
        root.RowStyles[2].Height = 210;
        var side = (TableLayoutPanel)Controls.Find("sideLayout", true).Single();
        side.RowStyles[0] = new RowStyle(SizeType.Percent, 57);
        side.RowStyles[1] = new RowStyle(SizeType.Percent, 43);
        var summaryTitle = alarmsValue.Parent!.Controls.OfType<Label>().First(x => x.Text == "알람 · 명령 요약");
        summaryTitle.Text = "알람 요약";

        var mapLayout = (TableLayoutPanel)Controls.Find("mapLayout", true).Single();
        mapLayout.RowStyles[0].Height = 36;
        mapCaption.Visible = false;
        var zoom = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
        zoom.Controls.Add(new Label { Text = "출발지와 할당 목적지 · m", AutoSize = true, Margin = new Padding(0, 8, 12, 0) });
        var minus = ActionButton("zoomOut", "−", (_, _) => SetMapZoom(mapCanvas.Zoom / 1.25), 32);
        var plus = ActionButton("zoomIn", "+", (_, _) => SetMapZoom(mapCanvas.Zoom * 1.25), 32);
        var fit = ActionButton("zoomFit", "전체 보기", (_, _) => SetMapZoom(1), 72);
        minus.Enabled = plus.Enabled = fit.Enabled = true;
        _zoomLabel = new Label { Text = "100%", Width = 45, Height = 28, TextAlign = ContentAlignment.MiddleCenter };
        zoom.Controls.AddRange([minus, _zoomLabel, plus, fit]);
        mapLayout.Controls.Add(zoom, 0, 0);
        mapSource.Text = "출발지 · 목적지 · 담당 AMR의 위치와 거리를 확인합니다.";

        var alarmBody = (Panel)Controls.Find("alarmBody", true).Single();
        alarmSummary.Visible = false;
        var alarmLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        alarmLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        alarmLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        _alarmGrid = ReadOnlyGrid("alarmGrid", "발생 중인 알람");
        _alarmGrid.MultiSelect = false;
        foreach (var (name, title) in new[] { ("Time", "발생 시각"), ("Amr", "대상 AMR"), ("Severity", "등급"), ("Message", "내용") })
            _alarmGrid.Columns.Add(name, title);
        _alarmGrid.Font = new Font("맑은 고딕", 8);
        _alarmGrid.Columns[3].FillWeight = 150;
        _alarmDetail = TextLabel("alarmDetail", "알람을 선택하세요.", 8, Color.FromArgb(51, 65, 85));
        _alarmDetail.TextAlign = ContentAlignment.TopLeft;
        _alarmDetail.Padding = new Padding(4);
        _alarmGrid.SelectionChanged += (_, _) => ShowAlarmDetail();
        void ShowAlarmDetail()
        {
            _alarmDetail.Text = _alarmGrid.SelectedRows.Count > 0 && _alarmGrid.SelectedRows[0].Tag is AlarmSnapshot alarm
                ? $"선택 알람 상세 · {alarm.AmrId}\n{MockAmrDataProvider.SeverityText(alarm.Severity)} · {alarm.Message}\n현재 상태: {(alarm.IsActive ? "발생 중" : "해제")}\n비고: {alarm.Note}"
                : "알람을 선택하세요.";
        }
        _refreshAlarmDetail = ShowAlarmDetail;
        alarmLayout.Controls.Add(_alarmGrid, 0, 0);
        alarmLayout.Controls.Add(_alarmDetail, 0, 1);
        alarmBody.Controls.Add(alarmLayout);
        alarmLayout.BringToFront();

        commandTitle.Text = "선택 AMR 시뮬레이션";
        var commandBody = (Panel)Controls.Find("commandBody", true).Single();
        foreach (Control control in commandBody.Controls) control.Visible = false;
        var launch = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        launch.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        launch.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        launch.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _simulationTarget = TextLabel("simulationTarget", "지도 또는 장비 목록에서 AMR 선택", 8, Color.SlateGray);
        _openSimulation = ActionButton("openSimulation", "같은 앱의 별도 실행창 열기 →", (_, _) => OpenSimulation(), 250);
        _openSimulation.Dock = DockStyle.Fill;
        _openSimulation.BackColor = Color.FromArgb(31, 41, 55);
        _openSimulation.ForeColor = Color.White;
        launch.Controls.Add(_simulationTarget, 0, 0);
        launch.Controls.Add(_openSimulation, 0, 1);
        launch.Controls.Add(TextLabel("simulationHint", "시작 · Stop · 상태·알람·응답 확인\n열린 창은 선택한 AMR 대상을 유지합니다.", 8, Color.SlateGray), 0, 2);
        commandBody.Controls.Add(launch);

        ConfigureHistory();
        _lastTick = Stopwatch.GetTimestamp();
        _simulationTimer = new System.Windows.Forms.Timer(components!) { Interval = 100 };
        _simulationTimer.Tick += (_, _) =>
        {
            // 100ms마다 호출돼도 실제 경과 시간은 달라질 수 있어 Stopwatch로 측정한다.
            // 폼은 경과 시간만 전달하고, 좌표 변경은 공급자의 AdvanceSimulations에서 수행한다.
            long now = Stopwatch.GetTimestamp();
            double seconds = Stopwatch.GetElapsedTime(_lastTick, now).TotalSeconds;
            _lastTick = now;
            if (!_closing) ((MockAmrDataProvider)_provider!).AdvanceSimulations(seconds);
        };
        _simulationTimer.Start();
    }

    private void ConfigureHistory()
    {
        var bottom = (TableLayoutPanel)Controls.Find("bottomLayout", true).Single();
        var tabs = new TabControl { Name = "feedbackTabs", Dock = DockStyle.Fill, Margin = Padding.Empty };
        var historyPage = new TabPage("이력") { BackColor = Color.White, Padding = new Padding(6) };
        var devicePage = new TabPage("장비 목록 · 지도와 선택 연동") { BackColor = Color.White };
        devicePage.Controls.Add(deviceGrid);
        foreach (Control control in bottom.Controls.Cast<Control>().ToArray()) control.Dispose();
        bottom.Controls.Clear();
        bottom.ColumnCount = 1;
        bottom.ColumnStyles.Clear();
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.RowStyles.Clear();
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tabs.TabPages.AddRange([historyPage, devicePage]);
        bottom.Controls.Add(tabs, 0, 0);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        _historyAmr = new TextBox { Name = "historyAmr", PlaceholderText = "AMR ID", Width = 90 };
        _historyType = new ComboBox { Name = "historyType", DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
        _historyType.Items.AddRange(["전체", "상태", "알람", "명령"]);
        _historyType.SelectedIndex = 0;
        _historyFrom = HistoryDate("historyFrom");
        _historyTo = HistoryDate("historyTo");
        var search = ActionButton("historySearch", "조회", (_, _) => RenderHistory(true), 54);
        var reset = ActionButton("historyReset", "초기화", (_, _) => ResetHistoryView(), 64);
        search.Enabled = reset.Enabled = true;
        filters.Controls.AddRange([_historyAmr, _historyType, _historyFrom, new Label { Text = "~", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, _historyTo, search, reset]);
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 67));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _historyGrid = ReadOnlyGrid("historyGrid", "이력 조회 결과");
        _historyGrid.MultiSelect = false;
        foreach (var (name, title) in new[] { ("Time", "발생 시각"), ("Amr", "AMR"), ("Type", "유형"), ("Description", "내용") }) _historyGrid.Columns.Add(name, title);
        _historyGrid.Columns[3].FillWeight = 250;
        _historyDetail = TextLabel("historyDetail", "선택 이력 상세 정보\n이력을 선택하세요.", 8, Color.SlateGray);
        _historyDetail.TextAlign = ContentAlignment.TopLeft;
        _historyDetail.Padding = new Padding(8, 0, 0, 0);
        _historyGrid.SelectionChanged += (_, _) => _historyDetail.Text = _historyGrid.SelectedRows.Count > 0 && _historyGrid.SelectedRows[0].Tag is HistoryEntry entry
            ? $"선택 이력 상세 정보\n{entry.ObservedAt.ToLocalTime():MM-dd HH:mm:ss} · {entry.AmrId} · {entry.Type}\n{entry.Description}"
            : "선택 이력 상세 정보\n이력을 선택하세요.";
        split.Controls.Add(_historyGrid, 0, 0);
        split.Controls.Add(_historyDetail, 1, 0);
        _historyCount = TextLabel("historyCount", "", 8, Color.SlateGray);
        layout.Controls.Add(filters, 0, 0);
        layout.Controls.Add(split, 0, 1);
        layout.Controls.Add(_historyCount, 0, 2);
        historyPage.Controls.Add(layout);
    }

    private static DateTimePicker HistoryDate(string name) => new()
    {
        Name = name, AccessibleName = name == "historyFrom" ? "조회 시작" : "조회 종료", Width = 176,
        Format = DateTimePickerFormat.Custom, CustomFormat = "MM-dd HH:mm:ss", ShowCheckBox = true, Checked = false
    };

    private AmrQuery _historyQuery = new();
    private Action? _refreshAlarmDetail;
    // 작은 Mock 세션의 전체 이력 사본을 화면에서 필터링한다. 실제 DB 조회/삭제 기능이 아니다.
    // 조회 버튼은 입력 조건을 적용하고, 일반 데이터 갱신은 기존 조건과 선택을 유지한다.
    private void RenderHistory(bool applyFilters = false, bool clearSelection = false)
    {
        if (_historyGrid is null) return;
        if (applyFilters)
        {
            _historyQuery = new(string.IsNullOrWhiteSpace(_historyAmr!.Text) ? null : _historyAmr.Text.Trim(),
                _historyFrom!.Checked ? new DateTimeOffset(_historyFrom.Value) : null,
                _historyTo!.Checked ? new DateTimeOffset(_historyTo.Value) : null,
                _historyType!.SelectedIndex == 0 ? null : (string)_historyType.SelectedItem!);
        }
        if (_historyQuery.From > _historyQuery.To)
        {
            _historyCount!.Text = "조회 시작은 종료 시각보다 늦을 수 없습니다.";
            _historyGrid.Rows.Clear();
            return;
        }
        object? selected = clearSelection || _historyGrid.SelectedRows.Count == 0 ? null : _historyGrid.SelectedRows[0].Tag;
        _historyGrid.Rows.Clear();
        foreach (var entry in _historyEntries.Reverse().Where(x =>
            (_historyQuery.AmrId is null || x.AmrId == _historyQuery.AmrId) && (_historyQuery.Type is null || x.Type == _historyQuery.Type) &&
            (_historyQuery.From is null || x.ObservedAt >= _historyQuery.From) && (_historyQuery.To is null || x.ObservedAt <= _historyQuery.To)))
        {
            int row = _historyGrid.Rows.Add(entry.ObservedAt.ToLocalTime().ToString("HH:mm:ss"), entry.AmrId, entry.Type, entry.Description);
            _historyGrid.Rows[row].Tag = entry;
        }
        _historyGrid.ClearSelection();
        _historyGrid.CurrentCell = null;
        if (selected is not null)
            foreach (DataGridViewRow row in _historyGrid.Rows) if (Equals(row.Tag, selected)) row.Selected = true;
        _historyCount!.Text = $"조회 {_historyGrid.Rows.Count}건 / 보관 {_historyEntries.Length}건 · 초기화는 조회 조건과 선택만 해제합니다.";
    }

    // 초기화 대상은 조회 입력·조건·선택뿐이다. _historyEntries와 공급자의 원본 이력은 그대로 둔다.
    private void ResetHistoryView()
    {
        _historyAmr!.Clear();
        _historyType!.SelectedIndex = 0;
        _historyFrom!.Value = _historyTo!.Value = DateTime.Now;
        _historyFrom.Checked = _historyTo.Checked = false;
        _historyQuery = new();
        RenderHistory(clearSelection: true);
    }

    private void SetMapZoom(double zoom)
    {
        mapCanvas.SetZoom(zoom);
        _zoomLabel!.Text = $"{mapCanvas.Zoom:P0}";
    }

    // 지도 옆 상세: 선택 AMR의 출발/목적지·담당 호기·이동 거리·현재 좌표를 같은 데이터로 표시한다.
    private void ShowJourney(AmrSnapshot device)
    {
        if (_openSimulation is null || device.Journey is not { } journey) return;
        detailsLabel.Text = $"출발지 · {device.AmrId}\n{journey.Source.Name} ({Number(journey.Source.X)}, {Number(journey.Source.Y)}) m\n" +
            $"출발 시각 {TimeText(journey.StartedAt)}\n\n할당 목적지 · {journey.Destination.Name}\n{device.AmrId} · {SimulationForm.PhaseText(journey.Phase)}\n" +
            $"목표 ({Number(journey.Destination.X)}, {Number(journey.Destination.Y)}) m\n\n" +
            $"출발–목적지 거리 {Number(journey.TotalDistance)} m\n이동 {Number(journey.TraveledDistance)} m · 남은 {Number(journey.RemainingDistance)} m\n" +
            $"현재 ({Number(device.X)}, {Number(device.Y)}) m";
        _simulationTarget!.Text = $"대상 {device.AmrId} · 지도 또는 장비 목록에서 선택";
    }

    // WatchAsync로 전달받은 변경을 알람·이력·열려 있는 AMR별 창에 함께 반영한다.
    private void RefreshFeedback(AmrChange change)
    {
        if (_alarmGrid is null) return;
        var active = change.Alarms.Where(x => x.IsActive).ToArray();
        var selected = _alarmGrid.SelectedRows.Count > 0 ? _alarmGrid.SelectedRows[0].Tag as AlarmSnapshot : null;
        if (!_alarmGrid.Rows.Cast<DataGridViewRow>().Select(x => x.Tag).SequenceEqual(active))
        {
            _alarmGrid.Rows.Clear();
            foreach (var alarm in active)
            {
                int row = _alarmGrid.Rows.Add(alarm.ObservedAt.ToLocalTime().ToString("HH:mm:ss"), alarm.AmrId, MockAmrDataProvider.SeverityText(alarm.Severity), alarm.Message);
                _alarmGrid.Rows[row].Tag = alarm;
                _alarmGrid.Rows[row].Cells[2].Style.ForeColor = alarm.Severity == AlarmSeverity.Error ? Color.Firebrick : Color.DarkGoldenrod;
            }
            var selectedRow = _alarmGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(x => Equals(x.Tag, selected)) ?? _alarmGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault();
            if (selectedRow is not null) { _alarmGrid.CurrentCell = selectedRow.Cells[0]; selectedRow.Selected = true; }
            _refreshAlarmDetail?.Invoke();
        }
        if (!_historyEntries.SequenceEqual(change.History))
        {
            _historyEntries = change.History;
            RenderHistory();
        }
        foreach (var (id, form) in _simulationWindows)
            if (_devices.TryGetValue(id, out var device)) form.Render(device, change.Alarms, change.Commands);
    }

    // 전체는 등록 장비 수이며 연결 여부로 제외하지 않는다. 현재 알람은 IsActive 기준으로 집계한다.
    // 이 Mock 화면의 장비 연결 표시는 Liveness 기준이며 Broker 접속 여부와는 구분한다.
    private void RefreshFeedbackSummary()
    {
        if (_alarmGrid is null) return;
        var active = _alarms.Where(x => x.IsActive).ToArray();
        devicesValue.Text = $"전체(등록) {_devices.Count}대";
        devicesDetail.Text = $"연결 {_devices.Values.Count(x => x.Liveness == LivenessObservation.Alive)}대 · 끊김 {_devices.Values.Count(x => x.Liveness == LivenessObservation.Unresponsive)}대\n미확인 {_devices.Values.Count(x => x.Liveness == LivenessObservation.Unknown)}대";
        communicationValue.Text = "Mock 통신 상태";
        communicationDetail.Text = "가상 데이터 · 실제 MQTT 미연결";
        var connected = _devices.Values.Where(x => x.Liveness == LivenessObservation.Alive).ToArray();
        operationValue.Text = $"대기 {connected.Count(x => x.State == AmrState.IDLE)} · 이동 {connected.Count(x => x.State == AmrState.BUSY)} · 오류 {connected.Count(x => x.State == AmrState.FAULT)}";
        operationDetail.Text = $"연결된 AMR {connected.Length}대 기준 · 정지 {connected.Count(x => x.State == AmrState.STOPPED)}대";
        alarmsValue.Text = $"발생 중인 알람 {active.Length}건";
        alarmsDetail.Text = $"오류 {active.Count(x => x.Severity == AlarmSeverity.Error)}건 · 경고 {active.Count(x => x.Severity == AlarmSeverity.Warning)}건";
        if (active.Any(x => x.Severity == AlarmSeverity.Unknown)) alarmsDetail.Text += $" · 미분류 {active.Count(x => x.Severity == AlarmSeverity.Unknown)}건";
    }

    // AMR ID별로 창을 하나만 열고, 생성 시 대상을 고정한다. 이후 관제 화면의 선택과 독립적이다.
    private void OpenSimulation()
    {
        if (_provider is not MockAmrDataProvider provider || _dispatcher is null || SelectedAmrId is not string id || !_devices.TryGetValue(id, out var device)) return;
        if (_simulationWindows.TryGetValue(id, out var existing)) { existing.Activate(); return; }
        var window = new SimulationForm(device, provider, _dispatcher);
        window.Render(device, _alarms, _commands);
        _simulationWindows.Add(id, window);
        window.FormClosed += (_, _) => _simulationWindows.Remove(id);
        window.Show(this);
    }

    // 부모 폼 종료 순서: 위치 갱신 타이머를 먼저 멈춘 뒤 자식 창을 닫아 각 Mock 이동을 정리한다.
    private void CloseSimulations()
    {
        _simulationTimer?.Stop();
        foreach (var window in _simulationWindows.Values.ToArray()) window.Close();
    }
}
