using System.Collections.Immutable;
using System.Drawing.Drawing2D;
using System.Globalization;
using Amr.Core;

namespace Amr.ControlGui;

// 좌표 표시와 Marker 선택만 담당한다. 이동·경로·연결 상태를 계산하지 않는다.
public sealed class MapCanvas : Control
{
    private ImmutableArray<AmrSnapshot> _devices = [];
    public string? SelectedAmrId { get; private set; }
    public event EventHandler? DeviceSelected;

    public MapCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Color.White;
        Font = new Font("맑은 고딕", 8);
        TabStop = true;
        AccessibleName = "AMR 위치 지도";
        AccessibleRole = AccessibleRole.Graphic;
        SetScene([], null);
    }

    internal void SetScene(IEnumerable<AmrSnapshot> devices, string? selectedAmrId)
    {
        _devices = devices.ToImmutableArray();
        SelectedAmrId = selectedAmrId;
        int invalid = _devices.Count(x => !ValidPosition(x));
        AccessibleDescription = _devices.IsEmpty ? "표시할 장비가 없습니다."
            : $"장비 {_devices.Length}대 · 선택 {SelectedAmrId ?? "없음"} · 잘못된 좌표 {invalid}건. 좌우 방향키로 장비 선택.";
        Invalidate();
    }

    internal bool TryGetViewport(out MapViewport viewport) => MapViewport.TryCreate(
        new RectangleF(44, 24, ClientSize.Width - 62, ClientSize.Height - 66), _devices.Where(ValidPosition).ToArray(), out viewport);

    internal PointF? MarkerPoint(string id) => TryGetViewport(out var viewport) && _devices.FirstOrDefault(x => x.AmrId == id) is { } device && ValidPosition(device)
        ? viewport.Project(device.X, device.Y) : null;

    internal void SelectMarkerAt(PointF location)
    {
        var candidates = _devices.Where(ValidPosition).Where(x => MarkerPoint(x.AmrId) is PointF point &&
            Math.Pow(point.X - location.X, 2) + Math.Pow(point.Y - location.Y, 2) <= 20 * 20).ToArray();
        if (candidates.Length == 0) return;
        // 같은 좌표의 장비도 반복 클릭으로 모두 선택할 수 있다.
        int previous = Array.FindIndex(candidates, x => x.AmrId == SelectedAmrId);
        SelectDevice(candidates[(previous + 1) % candidates.Length].AmrId);
    }

    private void SelectDevice(string id)
    {
        SelectedAmrId = id;
        DeviceSelected?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        SelectMarkerAt(e.Location);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var devices = _devices.Where(ValidPosition).ToArray();
        if (devices.Length == 0 || e.KeyCode is not (Keys.Left or Keys.Right or Keys.Home or Keys.End)) return;
        int index = Array.FindIndex(devices, x => x.AmrId == SelectedAmrId);
        int next = e.KeyCode switch { Keys.Home => 0, Keys.End => devices.Length - 1,
            Keys.Left => (index - 1 + devices.Length) % devices.Length, _ => (index + 1) % devices.Length };
        SelectDevice(devices[next].AmrId);
        e.Handled = e.SuppressKeyPress = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (_devices.IsEmpty) { DrawMessage(graphics, "표시할 장비가 없습니다."); return; }
        if (!TryGetViewport(out var view)) { DrawMessage(graphics, "좌표 범위를 표시할 수 없습니다."); return; }
        using var gridPen = new Pen(Color.FromArgb(226, 232, 240));
        using var framePen = new Pen(Color.FromArgb(148, 163, 184));
        using var plotBrush = new SolidBrush(Color.FromArgb(250, 252, 255));
        graphics.FillRectangle(plotBrush, view.Plot);
        for (int step = 0; step <= 4; step++)
        {
            double x = view.MinX + view.SpanX * (step / 4d);
            double y = view.MinY + view.SpanY * (step / 4d);
            var vertical = view.Project(x, view.MinY);
            var horizontal = view.Project(view.MinX, y);
            graphics.DrawLine(gridPen, vertical.X, view.Plot.Top, vertical.X, view.Plot.Bottom);
            graphics.DrawLine(gridPen, view.Plot.Left, horizontal.Y, view.Plot.Right, horizontal.Y);
            TextRenderer.DrawText(graphics, AxisText(x), Font, Rectangle.Round(new RectangleF(vertical.X - 26, view.Plot.Bottom + 5, 52, 20)), Color.SlateGray, TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(graphics, AxisText(y), Font, Rectangle.Round(new RectangleF(0, horizontal.Y - 9, 39, 20)), Color.SlateGray, TextFormatFlags.Right);
        }
        graphics.DrawRectangle(framePen, Rectangle.Round(view.Plot));
        TextRenderer.DrawText(graphics, "Y (m)", Font, new Point(5, 3), Color.SlateGray);
        TextRenderer.DrawText(graphics, "X (m)", Font, new Point(Math.Max(0, Width - 45), Height - 20), Color.SlateGray);
        foreach (var device in _devices.Where(ValidPosition).OrderBy(x => x.AmrId == SelectedAmrId)) DrawMarker(graphics, view, device);
        int invalid = _devices.Count(x => !ValidPosition(x));
        if (invalid > 0) TextRenderer.DrawText(graphics, $"표시할 수 없는 좌표 {invalid}건", Font, new Point(44, 3), Color.Firebrick);
        if (Focused) ControlPaint.DrawFocusRectangle(graphics, new Rectangle(1, 1, Width - 2, Height - 2));
    }

    private void DrawMarker(Graphics graphics, MapViewport view, AmrSnapshot device)
    {
        var point = view.Project(device.X, device.Y);
        bool selected = device.AmrId == SelectedAmrId;
        Color color = device.State switch { AmrState.BUSY => Color.FromArgb(37, 99, 235), AmrState.FAULT => Color.FromArgb(220, 38, 38),
            AmrState.STOPPED => Color.FromArgb(217, 119, 6), _ => Color.FromArgb(100, 116, 139) };
        using var fill = new SolidBrush(color);
        using var outline = new Pen(selected ? Color.FromArgb(37, 99, 235) : Color.White, selected ? 3 : 2);
        using var lossPen = new Pen(Color.SlateGray, 1.5f) { DashStyle = DashStyle.Dash };
        graphics.FillEllipse(fill, point.X - 9, point.Y - 9, 18, 18);
        graphics.DrawEllipse(outline, point.X - 13, point.Y - 13, 26, 26);
        if (device.BrokerConnection == ConnectionObservation.Disconnected || device.Liveness == LivenessObservation.Unresponsive)
            graphics.DrawEllipse(lossPen, point.X - 18, point.Y - 18, 36, 36);
        string label = device.AmrId;
        Size labelSize = TextRenderer.MeasureText(label, Font);
        var bounds = new Rectangle((int)Math.Clamp(point.X - labelSize.Width / 2, view.Plot.Left, Math.Max(view.Plot.Left, view.Plot.Right - labelSize.Width)),
            (int)(point.Y > view.Plot.Top + 35 ? point.Y - 35 : point.Y + 19), labelSize.Width, labelSize.Height);
        TextRenderer.DrawText(graphics, label, Font, bounds, selected ? Color.FromArgb(29, 78, 216) : Color.FromArgb(51, 65, 85), Color.White);
    }

    private void DrawMessage(Graphics graphics, string message) => TextRenderer.DrawText(graphics, message, Font,
        ClientRectangle, Color.SlateGray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    private static bool ValidPosition(AmrSnapshot device) => double.IsFinite(device.X) && double.IsFinite(device.Y);
    private static string AxisText(double value) => value.ToString(Math.Abs(value) >= 10000 ? "0.#E+0" : "0.##", CultureInfo.InvariantCulture);
}

// X/Y에 같은 배율을 사용하고 화면 Y만 반전한다. 운영 지도 범위를 제한하지 않는다.
internal readonly record struct MapViewport(RectangleF Plot, double MinX, double MinY, double SpanX, double SpanY)
{
    internal PointF Project(double x, double y) => new(
        (float)(Plot.Left + (x - MinX) / SpanX * Plot.Width),
        (float)(Plot.Bottom - (y - MinY) / SpanY * Plot.Height));

    internal static bool TryCreate(RectangleF available, IReadOnlyList<AmrSnapshot> devices, out MapViewport viewport)
    {
        viewport = default;
        if (available.Width < 50 || available.Height < 50 || devices.Count == 0) return false;
        double minX = Math.Min(0, devices.Min(x => x.X)), maxX = Math.Max(10, devices.Max(x => x.X));
        double minY = Math.Min(0, devices.Min(x => x.Y)), maxY = Math.Max(6, devices.Max(x => x.Y));
        double spanX = maxX - minX, spanY = maxY - minY;
        minX -= spanX * .15; minY -= spanY * .15;
        spanX *= 1.3; spanY *= 1.3;
        double scale = Math.Min(available.Width / spanX, available.Height / spanY);
        if (!double.IsFinite(minX) || !double.IsFinite(minY) || !double.IsFinite(spanX) || !double.IsFinite(spanY) ||
            !double.IsFinite(minX + spanX) || !double.IsFinite(minY + spanY) || !double.IsFinite(scale) || scale <= 0) return false;
        float width = (float)(spanX * scale), height = (float)(spanY * scale);
        if (width < 1 || height < 1) return false;
        viewport = new(new RectangleF(available.Left + (available.Width - width) / 2,
            available.Top + (available.Height - height) / 2, width, height), minX, minY, spanX, spanY);
        return true;
    }
}
