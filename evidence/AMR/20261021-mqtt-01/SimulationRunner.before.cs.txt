namespace Amr.Simulator.Simulation;

/// <summary>단일 소유자가 대기·위치 갱신·출력을 순차 실행한다. 반환 시 대기 작업도 끝난다.</summary>
public sealed class SimulationRunner(ISimulationClock? clock = null)
{
    private readonly ISimulationClock _clock = clock ?? new SystemSimulationClock();

    public async Task RunAsync(SimulatedAmr amr, TextWriter output, CancellationToken cancellationToken)
    {
        double elapsed = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteSnapshotAsync(output, "SimulationStarted", amr, elapsed);
            long previous = _clock.GetTimestamp();
            while (!amr.IsCompleted)
            {
                await _clock.DelayAsync(amr.Settings.UpdateIntervalMs, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                long now = _clock.GetTimestamp();
                double delta = _clock.GetElapsedSeconds(previous, now);
                previous = now;
                int next = amr.NextWaypointIndex;
                amr.Advance(delta);
                elapsed += delta;
                // 한 번의 갱신에 여러 점을 통과해도 경로 순서대로 기록한다.
                for (int index = next; index < amr.NextWaypointIndex; index++)
                {
                    var point = amr.Settings.Waypoints[index];
                    await output.WriteLineAsync(FormattableString.Invariant(
                        $"WaypointReached AmrId={amr.Id} Index={index} X={point.X:R} Y={point.Y:R} ObservedElapsedSeconds={elapsed:F3}"));
                }
                if (!amr.IsCompleted)
                    await WriteSnapshotAsync(output, "AmrMoved", amr, elapsed);
            }
            await WriteSnapshotAsync(output, "SimulationCompleted", amr, elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 마지막으로 계산한 상태를 남긴다. STOP 명령이나 도착으로 표시하지 않는다.
            await WriteSnapshotAsync(output, "SimulationCanceled", amr, elapsed);
        }
    }

    private static Task WriteSnapshotAsync(TextWriter output, string name, SimulatedAmr amr, double elapsed) =>
        output.WriteLineAsync(FormattableString.Invariant(
            $"{name} AmrId={amr.Id} X={amr.Position.X:R} Y={amr.Position.Y:R} State={amr.State} CurrentSpeedMps={amr.CurrentSpeedMps:R} ElapsedSeconds={elapsed:F3}"));
}
