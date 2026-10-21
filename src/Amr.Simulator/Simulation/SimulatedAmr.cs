using Amr.Simulator.Configuration;

namespace Amr.Simulator.Simulation;

/// <summary>초 단위 경과 시간을 받아 설정 경로를 이동한다. 호출자는 갱신을 순차 실행한다.</summary>
public sealed class SimulatedAmr
{
    private int _nextWaypointIndex;
    private bool _isMoving;

    public SimulatedAmr(SimulatorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
        Position = settings.InitialPosition;
        SkipReachedWaypoints();
    }

    public SimulatorSettings Settings { get; }
    public string Id => Settings.AmrId;
    public Position2D Position { get; private set; }
    public string State => _isMoving ? "BUSY" : "IDLE";
    public double CurrentSpeedMps => _isMoving ? Settings.SpeedMps : 0;
    /// <summary>다음 목표의 0부터 시작하는 첨자. 완료 시 경유점 수와 같다.</summary>
    public int NextWaypointIndex => _nextWaypointIndex;
    public bool IsCompleted => _nextWaypointIndex == Settings.Waypoints.Count;

    public void Advance(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), elapsedSeconds,
                "경과 시간은 0 이상의 유한한 숫자여야 합니다 (초).");
        if (elapsedSeconds == 0 || IsCompleted)
            return;

        // 직전 부분 이동의 반올림 결과가 목표와 같아진 경우도 같은 위치로 처리한다.
        SkipReachedWaypoints();
        if (IsCompleted)
        {
            _isMoving = false;
            return;
        }

        int speedExponent = Math.ILogB(Settings.SpeedMps);
        double speedMantissa = Math.ScaleB(Settings.SpeedMps, -speedExponent);
        double remainingSeconds = elapsedSeconds;
        while (!IsCompleted)
        {
            var target = Settings.Waypoints[_nextWaypointIndex];
            var (distanceMantissa, distanceExponent) = ScaledDistance(Position, target);
            int timeExponent = Math.ILogB(remainingSeconds);
            // (속도 × 시간) / 거리. 중간 곱·거리의 overflow/underflow를 피한다.
            double fraction = Math.ScaleB(
                speedMantissa * Math.ScaleB(remainingSeconds, -timeExponent) / distanceMantissa,
                speedExponent + timeExponent - distanceExponent);
            if (fraction < 1)
            {
                Position = new Position2D(Interpolate(Position.X, target.X, fraction),
                    Interpolate(Position.Y, target.Y, fraction));
                _isMoving = true;
                return;
            }

            double secondsToTarget = Math.ScaleB(distanceMantissa / speedMantissa,
                distanceExponent - speedExponent);
            Position = target; // 도착 반경을 쓰지 않고 목표 좌표에 정확히 맞춘다.
            remainingSeconds = Math.Max(0, remainingSeconds - secondsToTarget);
            _nextWaypointIndex++;
            SkipReachedWaypoints();
            _isMoving = !IsCompleted;
            if (remainingSeconds == 0)
                return;
        }
    }

    private void SkipReachedWaypoints()
    {
        while (!IsCompleted && Settings.Waypoints[_nextWaypointIndex] == Position)
            _nextWaypointIndex++;
    }

    // 거리 = mantissa × 2^exponent. 유한 좌표끼리의 차가 범위를 넘는 경우도 계산한다.
    private static (double Mantissa, int Exponent) ScaledDistance(Position2D start, Position2D end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        int adjustment = 0;
        if (!double.IsFinite(dx) || !double.IsFinite(dy))
        {
            dx = end.X * 0.5 - start.X * 0.5;
            dy = end.Y * 0.5 - start.Y * 0.5;
            adjustment = 1;
        }
        double scale = Math.Max(Math.Abs(dx), Math.Abs(dy));
        int exponent = Math.ILogB(scale);
        double x = dx / scale;
        double y = dy / scale;
        return (Math.ScaleB(scale, -exponent) * Math.Sqrt(x * x + y * y), exponent + adjustment);
    }

    private static double Interpolate(double start, double end, double fraction)
    {
        // 부호가 다르면 end-start가 overflow할 수 있어 가중합을 사용한다.
        double value = (start < 0) != (end < 0)
            ? (1 - fraction) * start + fraction * end
            : start + (end - start) * fraction;
        return Math.Clamp(value, Math.Min(start, end), Math.Max(start, end));
    }
}
