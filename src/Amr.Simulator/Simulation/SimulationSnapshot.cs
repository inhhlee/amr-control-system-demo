namespace Amr.Simulator.Simulation;

// 실행 루프의 단일 소유자가 만든 불변 복사본. 송신자는 가변 AMR을 직접 읽지 않는다.
public sealed record SimulationSnapshot(string Id, double X, double Y, double Speed, string State)
{
    public static SimulationSnapshot Capture(SimulatedAmr amr) =>
        new(amr.Id, amr.Position.X, amr.Position.Y, amr.CurrentSpeedMps, amr.State);
}
