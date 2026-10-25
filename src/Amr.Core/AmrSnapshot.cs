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
    long Revision);
