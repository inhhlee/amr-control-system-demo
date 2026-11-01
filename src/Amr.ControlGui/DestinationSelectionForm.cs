using System.Globalization;
using Amr.Core;

namespace Amr.ControlGui;

internal sealed class DestinationSelectionForm : Form
{
    internal MapStation? Destination { get; private set; }
    internal DestinationSelectionForm(MapStation? current)
    {
        Name = "destinationSelectionWindow"; Text = "실제 이동 목적지 설정";
        Font = new Font("맑은 고딕", 10); ClientSize = new Size(430, 230);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 5 };
        root.ColumnStyles.Add(new(SizeType.Absolute, 85)); root.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) root.RowStyles.Add(new(SizeType.Percent, 20));
        var x = new TextBox { Name = "destinationX", Dock = DockStyle.Fill, Text = current?.X.ToString(CultureInfo.InvariantCulture) ?? "" };
        var y = new TextBox { Name = "destinationY", Dock = DockStyle.Fill, Text = current?.Y.ToString(CultureInfo.InvariantCulture) ?? "" };
        root.Controls.Add(new Label { Text = "X (m)", AutoSize = true }, 0, 0); root.Controls.Add(x, 1, 0);
        root.Controls.Add(new Label { Text = "Y (m)", AutoSize = true }, 0, 1); root.Controls.Add(y, 1, 1);
        var error = new Label { Name = "destinationError", Dock = DockStyle.Fill, ForeColor = Color.Firebrick };
        root.Controls.Add(error, 0, 2); root.SetColumnSpan(error, 2);
        var apply = new Button { Name = "applyDestination", Text = "적용", Dock = DockStyle.Fill };
        apply.Click += (_, _) =>
        {
            if (!double.TryParse(x.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) || !double.IsFinite(px) ||
                !double.TryParse(y.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var py) || !double.IsFinite(py))
            { error.Text = "유한한 X/Y 좌표를 입력하세요."; return; }
            Destination = new("목적 좌표", px, py); DialogResult = DialogResult.OK; Close();
        };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Dock = DockStyle.Fill };
        root.Controls.Add(cancel, 0, 4); root.Controls.Add(apply, 1, 4);
        AcceptButton = apply; CancelButton = cancel; Controls.Add(root);
    }
}
