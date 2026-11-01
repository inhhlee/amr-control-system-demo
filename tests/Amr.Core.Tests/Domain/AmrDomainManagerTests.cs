using Amr.Core;
using Xunit;

namespace Amr.Core.Tests;

public sealed class AmrDomainManagerTests
{
    [Fact]
    public void TimeoutHoldsOnlyAffectedMissionAndRecoveryRequiresExplicitResume()
    {
        var run = new Fixture();
        run.Alive("A", 1); run.Alive("B", 1);
        Assert.True(run.Domain.StartMission("A", "M_A"));
        Assert.True(run.Domain.StartMission("B", "M_B"));
        run.Timeout("A", 2);
        var lost = run.Domain.Read();
        Assert.Equal(AmrCommunicationState.COMM_LOST, lost.Statuses[0].Communication);
        Assert.Equal(AmrMissionState.OnHold, lost.Statuses[0].Mission.State);
        Assert.Equal(AmrMissionState.Running, lost.Statuses[1].Mission.State);
        Assert.Equal("A", Assert.Single(lost.Alarms).AmrId);
        run.Timeout("A", 3);
        Assert.Single(run.Domain.Read().Alarms);
        run.Alive("A", 4);
        Assert.Empty(run.Domain.Read().Alarms);
        Assert.Equal(AmrMissionState.OnHold, run.Domain.Read().Statuses[0].Mission.State);
        Assert.True(run.Domain.ResumeMission("A"));
        Assert.Equal(AmrMissionState.Running, run.Domain.Read().Statuses[0].Mission.State);
    }

    [Fact]
    public void CompletionDuringHoldIsLatchedUntilExplicitResume()
    {
        var run = new Fixture(); run.Alive("A", 1);
        run.Domain.StartMission("A", "M1"); run.Timeout("A", 2);
        Assert.True(run.Domain.CompleteMission("A", "M1"));
        Assert.Equal(AmrMissionState.OnHold, run.Domain.Read().Statuses[0].Mission.State);
        Assert.False(run.Domain.ResumeMission("A"));
        run.Alive("A", 3);
        Assert.True(run.Domain.ResumeMission("A"));
        Assert.Equal(AmrMissionState.Completed, run.Domain.Read().Statuses[0].Mission.State);
    }

    [Fact]
    public void BrokerLossHoldsBothWithoutRewritingLastTelemetryOrOperationalState()
    {
        var run = new Fixture(); run.Alive("A", 1); run.Alive("B", 1);
        run.Telemetry("A", 1); run.Telemetry("B", 2);
        run.Domain.StartMission("A", "M_A"); run.Domain.StartMission("B", "M_B");
        foreach (string id in new[] { "A", "B" }) run.Domain.Observe(new(run.Session, 2, id,
            ConnectionObservation.Disconnected, LivenessObservation.Unknown, false, null, run.Now, "BrokerDisconnected"));
        var state = run.Domain.Read();
        Assert.All(state.Statuses, status => {
            Assert.Equal(AmrCommunicationState.BROKER_LOST, status.Communication);
            Assert.Equal(AmrMissionState.OnHold, status.Mission.State);
        });
        Assert.Equal(2, state.Alarms.Length);
        Assert.Equal(new[] { 1d, 2d }, state.Devices.Select(x => x.X));
        Assert.All(state.Devices, x => Assert.Equal(AmrState.BUSY, x.State));
        Assert.All(state.Devices, x => Assert.Equal(run.Now, x.LastHeartbeatReceivedAt));
        Assert.False(run.Telemetry("A", 99));
        Assert.Equal(1, run.Domain.Read().Devices[0].X);
    }

    [Fact]
    public void UnknownIdDuplicateAndOutOfOrderObservationsCannotChangeDomain()
    {
        var run = new Fixture(); run.Alive("A", 10);
        long revision = run.Domain.Read().Revision;
        Assert.False(run.Timeout("OTHER", 11));
        Assert.False(run.Timeout("A", 9));
        Assert.False(run.Timeout("A", 10));
        Assert.Equal(revision, run.Domain.Read().Revision);
        Assert.Empty(run.Domain.Read().Alarms);
    }

    [Fact]
    public void OldSessionCannotRecoverNewSessionOrDeliverTelemetry()
    {
        var run = new Fixture(); run.Alive("A", 1); run.Telemetry("A", 1);
        run.Domain.StartMission("A", "M_A");
        run.Domain.BeginSession(Guid.NewGuid());
        Assert.False(run.Alive("A", 999));
        Assert.False(run.Telemetry("A", 99));
        var state = run.Domain.Read();
        Assert.Equal(AmrCommunicationState.UNKNOWN, state.Statuses[0].Communication);
        Assert.Equal(AmrMissionState.OnHold, state.Statuses[0].Mission.State);
        Assert.Equal(1, state.Devices[0].X);
    }

    [Fact]
    public void AwaitingFirstCannotStartMissionAndTelemetryDoesNotDeclareLiveness()
    {
        var run = new Fixture();
        run.Domain.Observe(new(run.Session, 1, "A", ConnectionObservation.Connected, LivenessObservation.Unknown, true, null, run.Now));
        Assert.True(run.Telemetry("A", 1));
        Assert.False(run.Domain.StartMission("A", "M"));
        Assert.Equal(LivenessObservation.Unknown, run.Domain.Read().Devices[0].Liveness);
    }

    [Fact]
    public void LateCompletionFromCanceledOrDifferentMissionIsIgnored()
    {
        var run = new Fixture(); run.Alive("A", 1);
        run.Domain.StartMission("A", "M1"); run.Domain.CancelMission("A");
        Assert.False(run.Domain.CompleteMission("A", "M1"));
        run.Domain.StartMission("A", "M2");
        Assert.False(run.Domain.CompleteMission("A", "M1"));
        Assert.Equal(AmrMissionState.Running, run.Domain.Read().Statuses[0].Mission.State);
    }

    [Fact]
    public async Task ConcurrentQueuesUpdateDifferentAmrsAndSnapshotsRemainImmutable()
    {
        var run = new Fixture(); run.Alive("A", 1); run.Alive("B", 1);
        var original = run.Domain.Read();
        await Task.WhenAll(Task.Run(() => { for (int i = 0; i < 100; i++) run.Telemetry("A", i); }),
            Task.Run(() => { for (int i = 0; i < 100; i++) run.Telemetry("B", -i); }));
        Assert.Empty(original.Devices);
        var current = run.Domain.Read();
        Assert.Equal(99, current.Devices.Single(x => x.AmrId == "A").X);
        Assert.Equal(-99, current.Devices.Single(x => x.AmrId == "B").X);
    }

    [Fact]
    public void SessionStopHoldsMissionButDoesNotInventBrokerLossOrPhysicalStop()
    {
        var run = new Fixture(); run.Alive("A", 1); run.Telemetry("A", 1); run.Domain.StartMission("A", "M");
        run.Domain.Observe(new(run.Session, 2, "A", ConnectionObservation.Unknown, LivenessObservation.Unknown, false, null, run.Now));
        var state = run.Domain.Read();
        Assert.Equal(AmrCommunicationState.SESSION_STOPPED, state.Statuses[0].Communication);
        Assert.Equal(AmrMissionState.OnHold, state.Statuses[0].Mission.State);
        Assert.Empty(state.Alarms);
        Assert.Equal(AmrState.BUSY, state.Devices[0].State);
    }

    private sealed class Fixture
    {
        public Guid Session = Guid.NewGuid();
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public AmrDomainManager Domain = new(["A", "B"]);
        public Fixture() => Domain.BeginSession(Session);
        public bool Alive(string id, long sequence) => Domain.Observe(new(Session, sequence, id,
            ConnectionObservation.Connected, LivenessObservation.Alive, true, Now, Now));
        public bool Timeout(string id, long sequence) => Domain.Observe(new(Session, sequence, id,
            ConnectionObservation.Connected, LivenessObservation.Unresponsive, true, Now, Now));
        public bool Telemetry(string id, double x) => Domain.ReceiveTelemetry(new(Session, id, x, 0, 1, AmrState.BUSY, Now));
    }
}
