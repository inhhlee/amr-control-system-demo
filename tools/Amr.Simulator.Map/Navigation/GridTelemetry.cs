using Amr.Communication.Models;
using Amr.Simulator.Simulation;

namespace Amr.Simulator.Map.Navigation;

public static class GridTelemetry
{
    public const double CellSizeMeters = 1;

    public static SimulationSnapshot Capture(string id, GridSnapshot grid, bool stopped = false, int stepIntervalMs = GridNavigationSession.DefaultStepIntervalMs) =>
        new(id, grid.Current.Col * CellSizeMeters, -grid.Current.Row * CellSizeMeters,
            !stopped && grid.Phase == "Running" ? CellSizeMeters * 1000 / stepIntervalMs : 0,
            !stopped && grid.Phase == "Running" ? "BUSY" : "IDLE");

    public static GridCell? ReceivedCell(TelemetryMessage? message, int rows = 5, int cols = 6)
    {
        if (message is null) return null;
        double row = -message.Position.Y / CellSizeMeters, col = message.Position.X / CellSizeMeters;
        if (!double.IsFinite(row) || !double.IsFinite(col) || row < 0 || row >= rows || col < 0 || col >= cols ||
            row != Math.Truncate(row) || col != Math.Truncate(col)) return null;
        return new((int)row, (int)col);
    }
}
