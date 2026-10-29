using Amr.Communication.Monitoring;
using Xunit;

namespace Amr.Communication.Tests.Monitoring;

public sealed class SingleAmrHeartbeatMonitorTests
{
    [Fact]
    public void FirstNormalHeartbeatRegistersAndExactFiveSecondBoundaryNotifiesOnlyOnce()
    {
        var clock=new Clock();var monitor=new SingleAmrHeartbeatMonitor("A",clock:clock);
        monitor.Start();clock.Advance(20);
        Assert.Null(monitor.Check());Assert.Equal(HeartbeatObservationState.AwaitingFirst,monitor.Read().State);
        Assert.Null(monitor.Observe("B",clock.GetUtcNow(),clock.GetTimestamp()));
        Assert.Equal("MonitoringStarted",monitor.Observe("A",clock.GetUtcNow(),clock.GetTimestamp())!.Kind);
        clock.Advance(4.999);Assert.Null(monitor.Check());
        clock.Advance(.001);Assert.Equal("TimedOut",monitor.Check()!.Kind);
        Assert.Equal(HeartbeatObservationState.Unresponsive,monitor.Read().State);
        clock.Advance(10);Assert.Null(monitor.Check());Assert.Equal(1,monitor.Read().TimeoutCount);
        Assert.Equal("Recovered",monitor.Observe("A",clock.GetUtcNow(),clock.GetTimestamp())!.Kind);
        Assert.Null(monitor.Observe("A",clock.GetUtcNow(),clock.GetTimestamp()));
        clock.Advance(5);Assert.Equal("TimedOut",monitor.Check()!.Kind);
        Assert.Equal(2,monitor.Read().TimeoutCount);Assert.Equal(1,monitor.Read().RecoveryCount);
    }
    [Fact]
    public void WallClockChangesAndOldReceiptsCannotExtendLifeOrFalselyRecover()
    {
        var clock=new Clock();var monitor=new SingleAmrHeartbeatMonitor("A",clock:clock);
        monitor.Start();long first=clock.GetTimestamp();var received=clock.GetUtcNow();
        monitor.Observe("A",received,first);clock.Utc=clock.Utc.AddDays(-100);clock.Advance(5);
        Assert.NotNull(monitor.Check());
        Assert.Null(monitor.Observe("A",clock.GetUtcNow(),first));
        Assert.Null(monitor.Observe("A",clock.GetUtcNow(),first-1));
        Assert.Equal(HeartbeatObservationState.Unresponsive,monitor.Read().State);
        Assert.Equal(received,monitor.Read().LastReceivedAtUtc);
    }
    [Fact]
    public void StopRejectsLateReceiptsAndFreshSessionDoesNotReusePreviousRegistration()
    {
        var clock=new Clock();var monitor=new SingleAmrHeartbeatMonitor("A",clock:clock);
        monitor.Start();monitor.Observe("A",clock.GetUtcNow(),clock.GetTimestamp());
        monitor.Stop();clock.Advance(100);
        Assert.Null(monitor.Check());Assert.Null(monitor.Observe("A",clock.GetUtcNow(),clock.GetTimestamp()));
        Assert.False(monitor.Read().Monitoring);Assert.Null(monitor.Read().LastReceivedAtUtc);
        monitor.Start();Assert.Equal(HeartbeatObservationState.AwaitingFirst,monitor.Read().State);Assert.Null(monitor.Check());
        Assert.Equal(0,monitor.Read().TimeoutCount);Assert.Equal(0,monitor.Read().RecoveryCount);
    }
    [Theory]
    [InlineData(0,500)] [InlineData(-1,500)] [InlineData(5000,0)] [InlineData(5000,5001)] [InlineData(86400001,500)]
    public void InvalidTimingIsRejected(int timeout,int check) => Assert.Throws<ArgumentOutOfRangeException>(()=>new SingleAmrHeartbeatMonitor("A",new(){Timeout=TimeSpan.FromMilliseconds(timeout),CheckInterval=TimeSpan.FromMilliseconds(check)}));
    private sealed class Clock:TimeProvider
    {
        private long _stamp=100;
        public DateTimeOffset Utc=DateTimeOffset.UnixEpoch;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>_stamp;
        public override DateTimeOffset GetUtcNow()=>Utc;
        public void Advance(double seconds){long ticks=(long)Math.Round(seconds*TimeSpan.TicksPerSecond);_stamp+=ticks;Utc=Utc.AddTicks(ticks);}
    }
}
