using Amr.Simulator.Simulation;

namespace Amr.Simulator.Configuration;

/// <summary>로더가 검사한 설정. 실행 중 설정값은 보존한다.</summary>
public sealed class SimulatorSettings
{
    internal SimulatorSettings(string amrId, Position2D initialPosition, double speedMps,
        long updateIntervalMs, Position2D[] waypoints, IReadOnlyList<FaultScenario>? faults = null)
    {
        AmrId = amrId;
        InitialPosition = initialPosition;
        SpeedMps = speedMps;
        UpdateIntervalMs = updateIntervalMs;
        Waypoints = Array.AsReadOnly((Position2D[])waypoints.Clone());
        Faults = Array.AsReadOnly((faults ?? []).ToArray());
    }

    public string AmrId { get; }
    public Position2D InitialPosition { get; }
    public double SpeedMps { get; }
    public long UpdateIntervalMs { get; }
    public IReadOnlyList<Position2D> Waypoints { get; }
    public IReadOnlyList<FaultScenario> Faults { get; }
}
