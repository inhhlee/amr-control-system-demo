using Amr.Core;
using Xunit;

namespace Amr.ControlGui.Tests;

public sealed class MockCommandDispatcherTests
{
    [Theory]
    [InlineData(true, CommandResult.Success)]
    [InlineData(false, CommandResult.Failure)]
    public async Task AcceptanceIsRecordedBeforeReturnAndFinalResultIsSeparate(bool success, CommandResult expected)
    {
        await using var provider = new MockAmrDataProvider(8, 16);
        var dispatcher = new MockCommandDispatcher(provider);
        var before = await provider.GetDeviceAsync("AMR_001");
        var accepted = await dispatcher.DispatchAsync(new("AMR_001", CommandKind.MOVE, 10, 5));
        Assert.True(accepted.Accepted);
        Assert.NotNull(accepted.CommandId);
        var command = await provider.GetCommandAsync("AMR_001", accepted.CommandId);
        Assert.NotNull(command);
        Assert.Equal(CommandResult.Unconfirmed, command.Result);
        Assert.Equal(PublishObservation.MockOnly, command.Publish);
        Assert.Equal(10, command.TargetX);
        Assert.Equal(5, command.TargetY);
        Assert.Null(await provider.GetCommandAsync("AMR_002", accepted.CommandId));
        Assert.Null(await provider.GetCommandAsync("AMR_001", "unknown"));
        await provider.CompleteCommandAsync("AMR_001", accepted.CommandId, success);
        var completed = await provider.GetCommandAsync("AMR_001", accepted.CommandId);
        Assert.Equal(expected, completed!.Result);
        Assert.Equal(CommandResult.Unconfirmed, command.Result);
        Assert.Null(completed.AckResult);
        Assert.Null(completed.AckReceivedAt);
        Assert.Equal(before, await provider.GetDeviceAsync("AMR_001"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteCommandAsync("AMR_001", accepted.CommandId, !success));
    }

    [Fact]
    public async Task RejectionIsSeparateFromCancellationAndRequestsStayWithinCapacity()
    {
        await using var provider = new MockAmrDataProvider(8, 1);
        var dispatcher = new MockCommandDispatcher(provider);
        Assert.Equal(CommandRejection.UnknownDevice, (await dispatcher.DispatchAsync(new("missing", CommandKind.MOVE, 1, 2))).Rejection);
        Assert.Equal(CommandRejection.UnsupportedInMock, (await dispatcher.DispatchAsync(new("AMR_001", CommandKind.STOP))).Rejection);
        Assert.Equal(CommandRejection.InvalidParameters, (await dispatcher.DispatchAsync(new("AMR_001", CommandKind.MOVE, double.NaN, 2))).Rejection);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.DispatchAsync(new("AMR_001", CommandKind.MOVE, 1, 2), canceled.Token));
        using var laterCancellation = new CancellationTokenSource();
        var accepted = await dispatcher.DispatchAsync(new("AMR_001", CommandKind.MOVE, 1, 2), laterCancellation.Token);
        laterCancellation.Cancel();
        Assert.True(accepted.Accepted);
        Assert.Equal(CommandResult.Unconfirmed, (await provider.GetCommandAsync("AMR_001", accepted.CommandId!))!.Result);
        var rejected = await dispatcher.DispatchAsync(new("AMR_001", CommandKind.MOVE, 3, 4));
        Assert.False(rejected.Accepted);
        Assert.Null(rejected.CommandId);
        Assert.Equal(CommandRejection.MockCapacityReached, rejected.Rejection);
    }

    [Fact]
    public async Task ConcurrentRequestsHaveUniqueIdsAndOutOfOrderResultsStayWithTheirTargets()
    {
        await using var provider = new MockAmrDataProvider(64, 32);
        var dispatcher = new MockCommandDispatcher(provider);
        var requests = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            Task.Run(() => dispatcher.DispatchAsync(new(i % 2 == 0 ? "AMR_001" : "AMR_002", CommandKind.MOVE, i, 5)))));
        Assert.All(requests, x => Assert.True(x.Accepted));
        Assert.Equal(20, requests.Select(x => x.CommandId).Distinct().Count());
        for (int i = requests.Length - 1; i >= 0; i--)
        {
            string target = i % 2 == 0 ? "AMR_001" : "AMR_002";
            string other = i % 2 == 0 ? "AMR_002" : "AMR_001";
            await Assert.ThrowsAsync<ArgumentException>(() => provider.CompleteCommandAsync(other, requests[i].CommandId!, true));
            await provider.CompleteCommandAsync(target, requests[i].CommandId!, i % 3 == 0);
            Assert.Equal(i % 3 == 0 ? CommandResult.Success : CommandResult.Failure,
                (await provider.GetCommandAsync(target, requests[i].CommandId!))!.Result);
        }
    }

    [Fact]
    public async Task CancellationAfterAcceptanceBoundaryStillReturnsTheRecordedId()
    {
        using var cancellation = new CancellationTokenSource();
        await using var provider = new MockAmrDataProvider(8, 16, new CancelingClock(cancellation));
        var dispatcher = new MockCommandDispatcher(provider);
        var accepted = await dispatcher.DispatchAsync(new("AMR_001", CommandKind.MOVE, 1, 2), cancellation.Token);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(accepted.Accepted);
        Assert.NotNull(await provider.GetCommandAsync("AMR_001", accepted.CommandId!));
    }

    private sealed class CancelingClock(CancellationTokenSource cancellation) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            cancellation.Cancel();
            return DateTimeOffset.UnixEpoch;
        }
    }

    [Fact]
    public async Task InitialAndLaterNotificationsContainTrackedRequests()
    {
        await using var provider = new MockAmrDataProvider(8, 16);
        var dispatcher = new MockCommandDispatcher(provider);
        var accepted = await dispatcher.DispatchAsync(new("AMR_001", CommandKind.MOVE, 10, 5));
        await using var stream = provider.WatchAsync().GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(accepted.CommandId, Assert.Single(stream.Current.Commands).CommandId);
        await provider.CompleteCommandAsync("AMR_001", accepted.CommandId!, true);
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(AmrChangeKind.CommandChanged, stream.Current.Kind);
        Assert.Equal(accepted.CommandId, stream.Current.CommandId);
        Assert.Equal(CommandResult.Success, Assert.Single(stream.Current.Commands).Result);
    }
}
