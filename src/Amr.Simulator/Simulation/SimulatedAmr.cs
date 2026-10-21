using Amr.Simulator.Configuration;

namespace Amr.Simulator.Simulation;

/// <summary>#32의 초기 AMR. 이동과 상태 전이는 #33에서 추가한다.</summary>
public sealed class SimulatedAmr
{
    public SimulatedAmr(SimulatorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
        Position = settings.InitialPosition;
    }

    public SimulatorSettings Settings { get; }
    public string Id => Settings.AmrId;
    public Position2D Position { get; }
    public string State => "IDLE";
    public double CurrentSpeedMps => 0;
}
