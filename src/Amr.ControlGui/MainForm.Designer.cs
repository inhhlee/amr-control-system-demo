#nullable enable

namespace Amr.ControlGui;

partial class MainForm
{
    private System.ComponentModel.IContainer? components;
    private Label modeLabel = null!, detailsLabel = null!, statusLabel = null!, submissionLabel = null!;
    private Label communicationValue = null!, communicationDetail = null!, devicesValue = null!, devicesDetail = null!;
    private Label operationValue = null!, operationDetail = null!, alarmsValue = null!, alarmsDetail = null!, alarmSummary = null!;
    private DataGridView deviceGrid = null!, commandGrid = null!;
    private Button advanceButton = null!, submitButton = null!, completeSuccessButton = null!, completeFailureButton = null!;
    private NumericUpDown targetX = null!, targetY = null!;
    private MapCanvas mapCanvas = null!;
    private Label mapCaption = null!, mapSource = null!, commandTitle = null!, historyNote = null!, gridDetails = null!;
    private FlowLayoutPanel mockInputs = null!, gridActions = null!;
    private Button gridStartButton = null!, gridCancelButton = null!, gridResetButton = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        SuspendLayout();
        Font = new Font("맑은 고딕", 9);
        BackColor = Color.FromArgb(242, 245, 249);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var dashboard = Section("대시보드", out var dashboardBody);
        var badgeRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 24, ColumnCount = 2 };
        badgeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        badgeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        var description = TextLabel("summarySource", "공급자의 현재 상태 기준", 8, Color.SlateGray);
        modeLabel = TextLabel("modeLabel", "MOCK · 시험 데이터", 9, Color.FromArgb(37, 99, 235));
        modeLabel.TextAlign = ContentAlignment.MiddleRight;
        badgeRow.Controls.Add(description, 0, 0);
        badgeRow.Controls.Add(modeLabel, 1, 0);
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Padding = new Padding(0, 2, 0, 0) };
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        cards.Controls.Add(SummaryCard("통신 상태", "communication", out communicationValue, out communicationDetail), 0, 0);
        cards.Controls.Add(SummaryCard("AMR 연결 현황", "devices", out devicesValue, out devicesDetail), 1, 0);
        cards.Controls.Add(SummaryCard("AMR 운행 상태", "operation", out operationValue, out operationDetail), 2, 0);
        cards.Controls.Add(SummaryCard("알람 · 명령 요약", "alarms", out alarmsValue, out alarmsDetail), 3, 0);
        dashboardBody.Controls.Add(cards);
        dashboardBody.Controls.Add(badgeRow);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        var mapSection = Section("지도", out var mapBody);
        var mapLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        mapLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        mapLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mapLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        mapCaption = TextLabel("mapCaption", "AMR 위치 · 단위 m · 전체 좌표 자동 맞춤", 8, Color.SlateGray);
        var mapSplit = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        mapSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        mapSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        mapCanvas = new MapCanvas { Name = "mapCanvas", Dock = DockStyle.Fill, Margin = Padding.Empty };
        mapCanvas.DeviceSelected += MapDeviceSelected;
        mapCanvas.GridObstacleRequested += GridObstacleRequested;
        var detailSection = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 250, 252),
            Padding = new Padding(10), ColumnCount = 1, RowCount = 2, Margin = new Padding(6, 0, 0, 0), AutoScroll = true };
        detailSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        detailSection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detailSection.Controls.Add(TextLabel("selectedTitle", "선택한 AMR 정보", 9, Color.FromArgb(30, 41, 59), true), 0, 0);
        detailsLabel = TextLabel("detailsLabel", "장비를 선택하세요.", 9, Color.FromArgb(51, 65, 85));
        detailsLabel.TextAlign = ContentAlignment.TopLeft;
        detailSection.Controls.Add(detailsLabel, 0, 1);
        mapSplit.Controls.Add(mapCanvas, 0, 0);
        mapSplit.Controls.Add(detailSection, 1, 0);
        var legend = new Panel { Name = "mapLegend", Dock = DockStyle.Fill };
        var legendItems = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 23, Margin = Padding.Empty };
        foreach (var item in new[] { ("● 대기", Color.FromArgb(100, 116, 139)), ("● 이동", Color.FromArgb(37, 99, 235)),
            ("● 정지", Color.FromArgb(217, 119, 6)), ("● 오류", Color.FromArgb(220, 38, 38)),
            ("◎ 선택", Color.FromArgb(37, 99, 235)), ("◌ 연결 끊김/응답 없음", Color.SlateGray) })
            legendItems.Controls.Add(new Label { Text = item.Item1, ForeColor = item.Item2, Font = new Font("맑은 고딕", 8), AutoSize = true, Margin = new Padding(0, 1, 8, 0) });
        mapSource = TextLabel("mapSource", "좌표 출처: Mock · 경로 데이터 없음", 8, Color.SlateGray);
        mapSource.Dock = DockStyle.Bottom;
        mapSource.Height = 22;
        legend.Controls.Add(legendItems);
        legend.Controls.Add(mapSource);
        mapLayout.Controls.Add(mapCaption, 0, 0);
        mapLayout.Controls.Add(mapSplit, 0, 1);
        mapLayout.Controls.Add(legend, 0, 2);
        mapBody.Controls.Add(mapLayout);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var alarmSection = Section("알람", out var alarmBody);
        alarmSummary = TextLabel("alarmSummary", "현재 알람 없음\n수신된 알람 0건", 9, Color.SlateGray);
        alarmBody.Controls.Add(alarmSummary);
        var commandSection = Section("명령 · Mock 확인", out var commandBody);
        commandTitle = commandSection.Controls.OfType<Label>().Single();
        var commandLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        commandLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        commandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        commandLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var inputs = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        mockInputs = inputs;
        targetX = CoordinateInput("targetX", "목표 X", 10);
        targetY = CoordinateInput("targetY", "목표 Y", 5);
        submitButton = ActionButton("submitButton", "MOVE 요청", SubmitClicked, 88);
        advanceButton = ActionButton("advanceButton", "다음 상태", AdvanceClicked, 76);
        completeSuccessButton = ActionButton("completeSuccessButton", "성공 (Mock)", CompleteSuccessClicked, 90);
        completeFailureButton = ActionButton("completeFailureButton", "실패 (Mock)", CompleteFailureClicked, 90);
        inputs.Controls.Add(new Label { Text = "X", AutoSize = true, Margin = new Padding(0, 9, 2, 0) });
        inputs.Controls.Add(targetX);
        inputs.Controls.Add(new Label { Text = "Y", AutoSize = true, Margin = new Padding(4, 9, 2, 0) });
        inputs.Controls.Add(targetY);
        inputs.Controls.Add(submitButton);
        inputs.SetFlowBreak(submitButton, true);
        inputs.Controls.Add(advanceButton);
        inputs.Controls.Add(completeSuccessButton);
        inputs.Controls.Add(completeFailureButton);
        submissionLabel = TextLabel("submissionLabel", "MOVE 접수 후 별도 시험 결과를 지정하세요.", 8, Color.SlateGray);
        submissionLabel.AutoEllipsis = true;
        commandGrid = ReadOnlyGrid("commandGrid", "명령 결과 목록");
        // 두 줄의 입력에 필요한 높이만 쓰고 나머지는 결과 행과 스크롤바에 배정한다.
        commandGrid.Margin = Padding.Empty;
        commandGrid.Columns.Add("AmrId", "장비");
        commandGrid.Columns.Add("CommandId", "요청 ID");
        commandGrid.Columns.Add("Submission", "접수");
        commandGrid.Columns.Add("Result", "결과");
        commandGrid.Columns.Add("Reason", "설명");
        commandGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        commandGrid.Columns[0].Width = 80;
        commandGrid.Columns[1].Width = 265;
        commandGrid.Columns[2].Width = 65;
        commandGrid.Columns[3].Width = 100;
        commandGrid.Columns[4].Width = 150;
        // 작은 명령 영역에서도 접수/결과를 먼저 확인하고 ID는 가로 스크롤로 조회한다.
        commandGrid.Columns[3].DisplayIndex = 1;
        commandGrid.Columns[2].DisplayIndex = 2;
        var actionHost = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        actionHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actionHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        gridActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Visible = false, Margin = Padding.Empty };
        gridStartButton = ActionButton("gridStartButton", "출발", GridStartClicked, 70);
        gridCancelButton = ActionButton("gridCancelButton", "이동 중단", GridCancelClicked, 80);
        gridResetButton = ActionButton("gridResetButton", "지도 초기화", GridResetClicked, 90);
        gridActions.Controls.AddRange([gridStartButton, gridCancelButton, gridResetButton]);
        actionHost.Controls.Add(inputs, 0, 0);
        actionHost.Controls.Add(gridActions, 0, 1);
        commandLayout.Controls.Add(actionHost, 0, 0);
        commandLayout.Controls.Add(submissionLabel, 0, 1);
        var resultHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        resultHost.Controls.Add(commandGrid);
        gridDetails = TextLabel("gridDetails", "", 8, Color.FromArgb(51, 65, 85));
        gridDetails.TextAlign = ContentAlignment.TopLeft;
        gridDetails.Visible = false;
        gridDetails.AutoEllipsis = true;
        resultHost.Controls.Add(gridDetails);
        commandLayout.Controls.Add(resultHost, 0, 2);
        commandBody.Controls.Add(commandLayout);
        right.Controls.Add(alarmSection, 0, 0);
        right.Controls.Add(commandSection, 0, 1);
        body.Controls.Add(mapSection, 0, 0);
        body.Controls.Add(right, 1, 0);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        var listSection = Section("장비 목록 · 지도와 선택 연동", out var listBody);
        deviceGrid = ReadOnlyGrid("deviceGrid", "장비 목록");
        deviceGrid.MultiSelect = false;
        deviceGrid.Columns.Add("AmrId", "장비 ID");
        deviceGrid.Columns.Add("X", "X (m)");
        deviceGrid.Columns.Add("Y", "Y (m)");
        deviceGrid.Columns.Add("Speed", "속도 (m/s)");
        deviceGrid.Columns.Add("State", "운행 상태");
        deviceGrid.SelectionChanged += DeviceSelectionChanged;
        listBody.Controls.Add(deviceGrid);
        var historySection = Section("이력", out var historyBody);
        historyNote = TextLabel("historyNote", "현재 Mock 이력 0건\n이력 조회 화면은 후속 구현", 8, Color.SlateGray);
        historyNote.AutoEllipsis = true;
        historyBody.Controls.Add(historyNote);
        bottom.Controls.Add(listSection, 0, 0);
        bottom.Controls.Add(historySection, 1, 0);
        statusLabel = TextLabel("statusLabel", "초기 상태를 기다리는 중입니다.", 8, Color.SlateGray);
        root.Controls.Add(dashboard, 0, 0);
        root.Controls.Add(body, 0, 1);
        root.Controls.Add(bottom, 0, 2);
        root.Controls.Add(statusLabel, 0, 3);
        Controls.Add(root);
        AutoScaleMode = AutoScaleMode.Font;
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 960);
        ClientSize = new Size(Math.Min(1200, workArea.Width - 40), Math.Min(880, workArea.Height - 65));
        MinimumSize = new Size(1000, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "AMR 관제 · 대시보드와 지도";
        FormClosing += MainFormClosing;
        ResumeLayout(false);
    }

    private static Panel Section(string title, out Panel body)
    {
        var section = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(10, 6, 10, 8), Margin = new Padding(0, 0, 10, 10) };
        var heading = TextLabel("", title, 10, Color.FromArgb(30, 41, 59), true);
        heading.Dock = DockStyle.Top;
        heading.Height = 25;
        body = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        section.Controls.Add(body);
        section.Controls.Add(heading);
        return section;
    }

    private static Label TextLabel(string name, string text, float size, Color color, bool bold = false) => new()
    {
        Name = name, Text = text, Dock = DockStyle.Fill, AutoSize = false, Margin = Padding.Empty,
        Font = new Font("맑은 고딕", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color,
        TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false
    };

    private static Panel SummaryCard(string title, string name, out Label value, out Label detail)
    {
        var card = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(10, 4, 8, 4), Margin = new Padding(0, 0, 8, 0) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        value = TextLabel(name + "Value", "데이터 대기", 13, Color.FromArgb(51, 65, 85), true);
        detail = TextLabel(name + "Detail", "", 8, Color.SlateGray);
        layout.Controls.Add(TextLabel("", title, 8, Color.SlateGray), 0, 0);
        layout.Controls.Add(value, 0, 1);
        layout.Controls.Add(detail, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private static NumericUpDown CoordinateInput(string name, string accessibleName, decimal value) => new()
    {
        Name = name, AccessibleName = accessibleName, Minimum = -10000, Maximum = 10000,
        DecimalPlaces = 2, Value = value, Width = 64, Margin = new Padding(0, 3, 2, 3)
    };

    private static Button ActionButton(string name, string text, EventHandler handler, int width)
    {
        var button = new Button { Name = name, Text = text, Width = width, Height = 28, FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(248, 250, 252), ForeColor = Color.FromArgb(51, 65, 85), Font = new Font("맑은 고딕", 8),
            Enabled = false, Margin = new Padding(0, 2, 4, 2) };
        button.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        button.Click += handler;
        return button;
    }

    private static DataGridView ReadOnlyGrid(string name, string accessibleName)
    {
        var grid = new DataGridView { Name = name, AccessibleName = accessibleName, Dock = DockStyle.Fill, ReadOnly = true,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None, EnableHeadersVisualStyles = false, RowTemplate = { Height = 25 },
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 26 };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(51, 65, 85);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
        grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 64, 175);
        grid.GridColor = Color.FromArgb(226, 232, 240);
        return grid;
    }
}
