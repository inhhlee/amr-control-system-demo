using System.Diagnostics;

namespace Amr.Simulator.Simulation;

/// <summary>실제 대기와 단조 시각을 이동 계산에서 분리한다.</summary>
public interface ISimulationClock
{
    long GetTimestamp();
    double GetElapsedSeconds(long start, long end);
    Task DelayAsync(long milliseconds, CancellationToken cancellationToken);
}

internal sealed class SystemSimulationClock : ISimulationClock
{
    public long GetTimestamp() => Stopwatch.GetTimestamp();
    public double GetElapsedSeconds(long start, long end) => Stopwatch.GetElapsedTime(start, end).TotalSeconds;

    public async Task DelayAsync(long milliseconds, CancellationToken cancellationToken)
    {
        // 설정은 양수 Int64 전체를 허용한다. Task.Delay의 Int32 제한으로 축소하지 않는다.
        while (milliseconds > 0)
        {
            int chunk = (int)Math.Min(milliseconds, int.MaxValue);
            await Task.Delay(chunk, cancellationToken);
            milliseconds -= chunk;
        }
    }
}
