using Amr.Core;

namespace Amr.ControlGui;

// 실행창에 종속된 모달 창. 공급자를 직접 변경하지 않고 명시적으로 선택한 출발지만 반환한다.
internal sealed class SourceSelectionForm : Form
{
    internal MapStation? SelectedSource { get; private set; }

    internal SourceSelectionForm(string amrId, MapStation currentSource)
    {
        Name = "sourceSelectionWindow";
        Text = $"출발지 설정 · {amrId}";
        Font = new Font("맑은 고딕", 10);
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(600, 450);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 24, 16), ColumnCount = 1, RowCount = 6 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (float height in new[] { 46f, 58f, 106f, 106f, 50f, 52f })
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.Controls.Add(SimulationForm.TextBlock("sourceSelectionTitle", $"출발지 설정 · {amrId}", 17, true), 0, 0);
        root.Controls.Add(SimulationForm.TextBlock("sourceSelectionDescription",
            "이 AMR이 가상 실행을 시작할 위치를 선택하세요.\n선택 시 출발 좌표와 준비 상태의 시작 위치에 반영됩니다.", 10), 0, 1);

        int row = 2;
        foreach (var station in MockAmrDataProvider.SimulationSources)
        {
            bool selected = station == currentSource;
            var card = new TableLayoutPanel { Name = "sourceCard" + station.Name.Replace("-", ""), Dock = DockStyle.Fill,
                Padding = new Padding(14, 8, 14, 8), Margin = new Padding(0, 5, 0, 5), ColumnCount = 2, RowCount = 2,
                BorderStyle = BorderStyle.FixedSingle, BackColor = selected ? Color.FromArgb(239, 246, 255) : Color.FromArgb(248, 250, 252) };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            var name = SimulationForm.TextBlock("sourceOption" + station.Name.Replace("-", ""), station.Name, 13, true);
            if (selected) name.ForeColor = Color.FromArgb(37, 99, 235);
            card.Controls.Add(name, 0, 0);
            card.Controls.Add(SimulationForm.TextBlock("", $"출발 좌표 ({station.X:0.0}, {station.Y:0.0}) m", 10), 0, 1);
            var apply = new Button { Name = "applySource" + station.Name.Replace("-", ""), Text = "선택·적용",
                AccessibleName = $"출발지 {station.Name} 선택·적용", Dock = DockStyle.Fill, Margin = new Padding(8, 15, 0, 15),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(31, 41, 55), ForeColor = Color.White, UseVisualStyleBackColor = false };
            apply.Click += (_, _) => { SelectedSource = station; DialogResult = DialogResult.OK; Close(); };
            card.Controls.Add(apply, 1, 0);
            card.SetRowSpan(apply, 2);
            root.Controls.Add(card, 0, row++);
        }

        root.Controls.Add(SimulationForm.TextBlock("sourceSelectionHint", "실행 전 설정 가능 · 실행 중 출발지 변경 비활성", 10), 0, 4);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.Controls.Add(SimulationForm.TextBlock("", "Mock AMR · 가상 실행의 출발 위치 설정", 9), 0, 0);
        var close = new Button { Name = "closeSourceSelection", Text = "닫기", DialogResult = DialogResult.Cancel,
            Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 8), FlatStyle = FlatStyle.Flat, BackColor = Color.White };
        footer.Controls.Add(close, 1, 0);
        CancelButton = close;
        root.Controls.Add(footer, 0, 5);
        Controls.Add(root);
    }
}
