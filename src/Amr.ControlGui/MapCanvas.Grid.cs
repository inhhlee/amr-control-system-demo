using System.Drawing.Drawing2D;

namespace Amr.ControlGui;

public sealed partial class MapCanvas
{
    private GridMapState? _gridState;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool GridEditingEnabled { get; set; }
    internal event EventHandler<GridCellEdit>? GridObstacleRequested;

    internal void SetGridState(GridMapState? state)
    {
        _gridState = state;
        if (state is not null)
            AccessibleDescription = $"격자 {state.Navigation.Map.Length}행 {state.Navigation.Map[0].Length}열 · " +
                $"위치 {(state.Position is { } cell ? $"수신 ({cell.Row},{cell.Col})" : "수신 대기")} · " +
                $"{state.Navigation.Moves}회 이동 · {state.Navigation.NextWaypointIndex}/{state.Navigation.Waypoints.Length} 방문 · 빈 칸 클릭으로 장애물 편집";
        Invalidate();
    }

    private bool TryGetGridViewport(out MapViewport view)
    {
        view = default;
        if (_gridState is null || Width < 100 || Height < 100) return false;
        int rows = _gridState.Navigation.Map.Length, cols = _gridState.Navigation.Map[0].Length;
        float scale = Math.Min((Width - 50f) / cols, (Height - 48f) / rows);
        var plot = new RectangleF(36 + (Width - 50 - cols * scale) / 2, 15 + (Height - 48 - rows * scale) / 2, cols * scale, rows * scale);
        view = new(plot, -.5, -rows + .5, cols, rows);
        return true;
    }

    internal RectangleF? GridCellBounds(int row, int col)
    {
        if (_gridState is null || !TryGetGridViewport(out var view)) return null;
        var point = view.Project(col, -row);
        float size = view.Plot.Width / _gridState.Navigation.Map[0].Length;
        return new RectangleF(point.X - size / 2, point.Y - size / 2, size, size);
    }

    internal bool TryEditGrid(PointF location)
    {
        if (!GridEditingEnabled || _gridState is null || !TryGetGridViewport(out var view) || !view.Plot.Contains(location)) return false;
        var navigation = _gridState.Navigation;
        int row = (int)((location.Y - view.Plot.Top) / (view.Plot.Height / navigation.Map.Length));
        int col = (int)((location.X - view.Plot.Left) / (view.Plot.Width / navigation.Map[0].Length));
        var cell = new GridCell(row, col);
        if (cell == navigation.Start || cell == navigation.Goal || cell == navigation.Current || navigation.FixedObstacles[row][col]) return false;
        GridObstacleRequested?.Invoke(this, new(row, col, navigation.Map[row][col] == 0));
        return true;
    }

    private void DrawGrid(Graphics graphics)
    {
        if (_gridState is null || !TryGetGridViewport(out var view)) { DrawMessage(graphics, "격자를 표시할 공간이 부족합니다."); return; }
        var state = _gridState;
        var navigation = state.Navigation;
        using var gridPen = new Pen(Color.FromArgb(210, 219, 230));
        using var wall = new SolidBrush(Color.FromArgb(71, 85, 105));
        using var obstacle = new SolidBrush(Color.FromArgb(254, 215, 170));
        using var routePen = new Pen(Color.FromArgb(37, 99, 235), 2) { DashStyle = DashStyle.Dash };
        using var trailPen = new Pen(Color.FromArgb(13, 148, 136), 3);
        using var waypointPen = new Pen(Color.FromArgb(217, 119, 6), 2);
        for (int row = 0; row < navigation.Map.Length; row++)
        for (int col = 0; col < navigation.Map[row].Length; col++)
        {
            var bounds = GridCellBounds(row, col)!.Value;
            if (navigation.Map[row][col] == 1) graphics.FillRectangle(navigation.FixedObstacles[row][col] ? wall : obstacle, bounds);
            graphics.DrawRectangle(gridPen, Rectangle.Round(bounds));
        }
        PointF Project(GridCell cell) => view.Project(cell.Col, -cell.Row);
        if (navigation.Path.Length > 1) graphics.DrawLines(routePen, navigation.Path.Select(Project).ToArray());
        if (state.ReceivedTrail.Length > 1) graphics.DrawLines(trailPen, state.ReceivedTrail.Select(Project).ToArray());
        void Mark(GridCell cell, string text)
        {
            var bounds = Rectangle.Round(GridCellBounds(cell.Row, cell.Col)!.Value);
            bounds.Inflate(-3, -3);
            graphics.DrawRectangle(waypointPen, bounds);
            TextRenderer.DrawText(graphics, text, Font, bounds, Color.FromArgb(120, 53, 15), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        Mark(navigation.Start, "S");
        for (int index = 0; index < navigation.Waypoints.Length; index++)
            Mark(navigation.Waypoints[index], index == navigation.Waypoints.Length - 1 ? "G" : $"W{index + 1}");
        for (int row = 0; row < navigation.Map.Length; row++)
            TextRenderer.DrawText(graphics, (-row).ToString(), Font, new Rectangle(0, (int)Project(new(row, 0)).Y - 9, 30, 20), Color.SlateGray, TextFormatFlags.Right);
        for (int col = 0; col < navigation.Map[0].Length; col++)
            TextRenderer.DrawText(graphics, col.ToString(), Font, new Rectangle((int)Project(new(0, col)).X - 15, (int)view.Plot.Bottom + 3, 30, 20), Color.SlateGray, TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(graphics, "Y(m)", Font, new Point(0, 0), Color.SlateGray);
        TextRenderer.DrawText(graphics, "X(m)", Font, new Point(Math.Max(0, Width - 36), Height - 20), Color.SlateGray);
        foreach (var device in _devices.Where(ValidPosition)) DrawMarker(graphics, view, device);
        if (state.Position is null)
            TextRenderer.DrawText(graphics, "MQTT 위치 수신 대기", Font, new Point(36, Height - 18), Color.SlateGray);
    }
}
