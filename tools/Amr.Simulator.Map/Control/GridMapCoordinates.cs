using Amr.Communication.Models;
using Amr.Simulator.Navigation;

namespace Amr.Simulator.Map.Control;

/// <summary>관제에서 받은 실좌표를 지도 칸으로 바꾸고 표시 가능한 범위를 검사한다.</summary>
public static class GridMapCoordinates
{
    public static GridCell? ReceivedCell(TelemetryMessage? message, int rows = 5, int cols = 6)
    {
        if (message is null) return null;
        double row = -message.Position.Y / GridTelemetry.CellSizeMeters, col = message.Position.X / GridTelemetry.CellSizeMeters;
        if (!double.IsFinite(row) || !double.IsFinite(col) || row < 0 || row >= rows || col < 0 || col >= cols ||
            row != Math.Truncate(row) || col != Math.Truncate(col)) return null;
        return new((int)row, (int)col);
    }
}
