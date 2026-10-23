using Amr.Simulator.Simulation;

namespace Amr.Simulator.Navigation;

public static class GridTelemetry
{
    public const double CellSizeMeters = 1;

    public static SimulationSnapshot Capture(string id, GridSnapshot grid, bool stopped = false,
        int stepIntervalMs = GridSimulation.DefaultStepIntervalMs) =>
        new(id, grid.Current.Col * CellSizeMeters, -grid.Current.Row * CellSizeMeters,
            !stopped && grid.Phase == "Running" ? CellSizeMeters * 1000 / stepIntervalMs : 0,
            !stopped && grid.Phase == "Running" ? "BUSY" : "IDLE");
}
