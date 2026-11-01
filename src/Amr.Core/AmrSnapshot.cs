namespace Amr.Core;

public enum AmrState { IDLE, BUSY, STOPPED, FAULT }
public enum ConnectionObservation { Unknown, Connected, Disconnected }
public enum LivenessObservation { Unknown, Alive, Unresponsive }

public sealed record AmrSnapshot(
    string AmrId,
    double X,
    double Y,
    double SpeedMps,
    AmrState State,
    ConnectionObservation BrokerConnection,
    LivenessObservation Liveness,
    DateTimeOffset? LastHeartbeatReceivedAt,
    DateTimeOffset? LastTelemetryReceivedAt,
    long Revision)
{
    // 출발/목적지 정보가 없는 기존 공급자도 사용할 수 있도록 선택적으로 둔다.
    public AmrJourney? Journey { get; init; }
}

// 화면의 '도착'은 운행 상태 IDLE과 별도로 표현한다. Ready와 Arrived 모두 속도는0일 수 있다.
public enum SimulationPhase { Ready, Moving, Stopped, Arrived }
public sealed record MapStation(string Name, double X, double Y);
// 거리는 공급자의 이동 방식에 따른 값이다. 현재 Mock은 X축 후 Y축 구간 거리의 합을 전달한다.
public sealed record AmrJourney(MapStation Source, MapStation Destination, SimulationPhase Phase,
    double TotalDistance, double TraveledDistance, DateTimeOffset? StartedAt = null)
{
    public double RemainingDistance => Math.Max(0, TotalDistance - TraveledDistance);
}
