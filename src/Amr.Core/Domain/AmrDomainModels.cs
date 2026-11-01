using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Amr.Core;

[JsonConverter(typeof(JsonStringEnumConverter<AmrCommunicationState>))]
public enum AmrCommunicationState { UNKNOWN, CONNECTED, COMM_LOST, BROKER_LOST, SESSION_STOPPED }
[JsonConverter(typeof(JsonStringEnumConverter<AmrMissionState>))]
public enum AmrMissionState { None, Running, OnHold, Completed, Canceled }

// 연결 계층이 만드는 프로토콜 독립 입력. Core에는 MQTT/Timer/GUI 참조가 없다.
public sealed record AmrCommunicationInput(Guid SessionId, long Sequence, string AmrId,
    ConnectionObservation BrokerConnection, LivenessObservation Liveness, bool Monitoring,
    DateTimeOffset? LastHeartbeatReceivedAt, DateTimeOffset ObservedAt, string? Reason = null);
public sealed record AmrTelemetryInput(Guid SessionId, string AmrId, double X, double Y, double SpeedMps,
    AmrState State, DateTimeOffset ReceivedAt);
public sealed record AmrMissionSnapshot(string? MissionId, AmrMissionState State, bool CompletionObserved);
public sealed record AmrDomainStatus(string AmrId, AmrCommunicationState Communication,
    ConnectionObservation BrokerConnection, LivenessObservation Liveness, DateTimeOffset? LastHeartbeatReceivedAt,
    AmrMissionSnapshot Mission, string? Reason);
public sealed record AmrDomainSnapshot(long Revision, ImmutableArray<AmrSnapshot> Devices,
    ImmutableArray<AmrDomainStatus> Statuses, ImmutableArray<AlarmSnapshot> Alarms);
