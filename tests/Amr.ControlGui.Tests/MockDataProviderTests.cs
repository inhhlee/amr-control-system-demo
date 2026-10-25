using Amr.Core;
using Xunit;

namespace Amr.ControlGui.Tests;

public sealed class MockDataProviderTests
{
    [Fact]
    public async Task QueriesReturnImmutableIndependentSnapshotsAndUnknownRealConnections()
    {
        await using var provider = new MockAmrDataProvider(8, 16);
        var before = await provider.GetDevicesAsync();
        Assert.Equal(ProviderMode.Mock, provider.Mode);
        Assert.Equal(new[] { "AMR_001", "AMR_002" }, before.Select(x => x.AmrId));
        Assert.All(before, x =>
        {
            Assert.Equal(ConnectionObservation.Unknown, x.BrokerConnection);
            Assert.Equal(LivenessObservation.Unknown, x.Liveness);
            Assert.Null(x.LastHeartbeatReceivedAt);
            Assert.Null(x.LastTelemetryReceivedAt);
        });
        await provider.AdvanceDeviceAsync("AMR_001");
        Assert.Equal(0, before[0].X);
        Assert.Equal(AmrState.IDLE, before[0].State);
        Assert.Equal(before[1], await provider.GetDeviceAsync("AMR_002"));
        Assert.Equal(1, (await provider.GetDeviceAsync("AMR_001"))!.X);
        Assert.Throws<NotSupportedException>(() => ((IList<AmrSnapshot>)before)[0] = before[1]);
        Assert.Null(await provider.GetDeviceAsync("AMR_MISSING"));
        Assert.Empty(await provider.GetAlarmsAsync(new()));
        Assert.Empty((await provider.GetHistoryAsync(new())).Entries);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("AMR/001")]
    [InlineData("AMR+001")]
    [InlineData("AMR#001")]
    [InlineData("AMR\n001")]
    public async Task InvalidIdentifiersAreFailuresRatherThanMissingDevices(string id)
    {
        await using var provider = new MockAmrDataProvider(8, 16);
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetDeviceAsync(id));
    }

    [Fact]
    public async Task InvalidQueriesAndCancellationAreNotEmptySuccesses()
    {
        await using var provider = new MockAmrDataProvider(8, 16);
        var now = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetHistoryAsync(new(From: now, To: now.AddSeconds(-1))));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetAlarmsAsync(new(Type: " ")));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetDevicesAsync(canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.AdvanceDeviceAsync("AMR_001", canceled.Token));
        Assert.Equal(0, (await provider.GetDeviceAsync("AMR_001"))!.X);
    }

    [Fact]
    public async Task SubscriptionStartsAtCurrentSnapshotThenPreservesEachChange()
    {
        await using var provider = new MockAmrDataProvider(8, 16);
        await provider.AdvanceDeviceAsync("AMR_001");
        await using var stream = provider.WatchAsync().GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        var initial = stream.Current;
        Assert.Equal(AmrChangeKind.InitialSnapshot, initial.Kind);
        Assert.Equal(1, initial.Revision);
        Assert.Equal(1, initial.Devices[0].X);
        await provider.AdvanceDeviceAsync("AMR_001");
        await provider.AdvanceDeviceAsync("AMR_002");
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(2, stream.Current.Revision);
        Assert.Equal("AMR_001", stream.Current.AmrId);
        Assert.Equal(initial.Devices[1], stream.Current.Devices[1]);
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(3, stream.Current.Revision);
        Assert.Equal("AMR_002", stream.Current.AmrId);
        Assert.Equal(1, initial.Devices[0].X);
    }

    [Fact]
    public async Task ConcurrentRegistrationNeverLosesTheChangeAtTheBoundary()
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            await using var provider = new MockAmrDataProvider(8, 16);
            await using var stream = provider.WatchAsync().GetAsyncEnumerator();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var subscribe = Task.Run(async () => { await start.Task; return await stream.MoveNextAsync(); });
            var update = Task.Run(async () => { await start.Task; await provider.AdvanceDeviceAsync("AMR_001"); });
            start.SetResult();
            Assert.True(await subscribe.WaitAsync(TimeSpan.FromSeconds(5)));
            await update.WaitAsync(TimeSpan.FromSeconds(5));
            if (stream.Current.Revision == 0)
                Assert.True(await stream.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, stream.Current.Revision);
            Assert.Equal(1, stream.Current.Devices[0].X);
        }
    }

    [Fact]
    public async Task SlowConsumerGetsExplicitOverflowAndCanResynchronize()
    {
        await using var provider = new MockAmrDataProvider(1, 16);
        await using var stream = provider.WatchAsync().GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        await provider.AdvanceDeviceAsync("AMR_001");
        await provider.AdvanceDeviceAsync("AMR_001");
        await Assert.ThrowsAsync<AmrResyncRequiredException>(() => stream.MoveNextAsync().AsTask());
        Assert.Equal(0, provider.SubscriptionCount);
        await using var fresh = provider.WatchAsync().GetAsyncEnumerator();
        Assert.True(await fresh.MoveNextAsync());
        Assert.Equal(AmrChangeKind.InitialSnapshot, fresh.Current.Kind);
        Assert.Equal(2, fresh.Current.Revision);
        Assert.Equal(2, fresh.Current.Devices[0].X);
    }

    [Fact]
    public async Task SlowConsumerOverflowDoesNotInterruptAHealthyConsumer()
    {
        await using var provider = new MockAmrDataProvider(1, 16);
        await using var slow = provider.WatchAsync().GetAsyncEnumerator();
        await using var healthy = provider.WatchAsync().GetAsyncEnumerator();
        Assert.True(await slow.MoveNextAsync());
        Assert.True(await healthy.MoveNextAsync());
        for (int revision = 1; revision <= 3; revision++)
        {
            await provider.AdvanceDeviceAsync("AMR_001");
            Assert.True(await healthy.MoveNextAsync());
            Assert.Equal(revision, healthy.Current.Revision);
        }
        await Assert.ThrowsAsync<AmrResyncRequiredException>(() => slow.MoveNextAsync().AsTask());
        Assert.Equal(1, provider.SubscriptionCount);
    }

    [Fact]
    public async Task DisposingDropsBufferedChangesAndRejectsNewSubscriptions()
    {
        var provider = new MockAmrDataProvider(8, 16);
        await using var stream = provider.WatchAsync().GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        await provider.AdvanceDeviceAsync("AMR_001");
        await provider.DisposeAsync();
        Assert.False(await stream.MoveNextAsync());
        await using var newStream = provider.WatchAsync().GetAsyncEnumerator();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => newStream.MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task CancelingOneConsumerLeavesOtherConsumersRunning()
    {
        await using var provider = new MockAmrDataProvider(8, 16);
        using var cancellation = new CancellationTokenSource();
        await using var first = provider.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        await using var second = provider.WatchAsync().GetAsyncEnumerator();
        Assert.True(await first.MoveNextAsync());
        Assert.True(await second.MoveNextAsync());
        var pending = first.MoveNextAsync().AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, provider.SubscriptionCount);
        await provider.AdvanceDeviceAsync("AMR_002");
        Assert.True(await second.MoveNextAsync());
        Assert.Equal("AMR_002", second.Current.AmrId);
    }

    [Fact]
    public async Task EndingEnumerationDetachesAndDisposalUnblocksWaitingReaders()
    {
        var provider = new MockAmrDataProvider(8, 16);
        var first = provider.WatchAsync().GetAsyncEnumerator();
        Assert.True(await first.MoveNextAsync());
        await first.DisposeAsync();
        Assert.Equal(0, provider.SubscriptionCount);
        await using var second = provider.WatchAsync().GetAsyncEnumerator();
        Assert.True(await second.MoveNextAsync());
        var pending = second.MoveNextAsync().AsTask();
        await provider.DisposeAsync();
        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, provider.SubscriptionCount);
        await provider.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.GetDevicesAsync());
    }
}
