using System.Collections.Concurrent;
using Amr.Communication.Models;
using Amr.Communication.Mqtt;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class AmrReceiveQueueShutdownTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Stop_CancelsActiveHandler_DiscardsWaitingAndClosesAdmission()
    {
        var started = Signal();
        var events = new ConcurrentQueue<QueueProcessingEvent>();
        int calls = 0;
        var queue = new AmrReceiveQueue(async (_, token) =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        }, events.Enqueue);
        Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message(1)));
        await started.Task.WaitAsync(Limit);
        Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message(2)));
        Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message(3)));
        Task stopping = queue.RequestStop();
        Assert.Same(stopping, queue.RequestStop());
        Assert.Equal(QueueAdmission.NotAccepting, queue.TryEnqueue(Message(4)));
        await queue.StopAsync(Limit);
        Assert.Equal(1, calls);
        Assert.Equal(2, Assert.Single(events, entry => entry.Code == "ShutdownDiscarded").Count);
        Assert.Single(events, entry => entry.Code == "HandlerCancelled");
        Assert.Single(events, entry => entry.Code == "Stopped");
        Assert.DoesNotContain(events, entry => entry.Code == "HandlerFailed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timeout_DiscardsPendingAndObservesLateCompletionOrFailure(bool failLate)
    {
        var started = Signal();
        var release = Signal();
        var events = new ConcurrentQueue<QueueProcessingEvent>();
        int calls = 0;
        var queue = new AmrReceiveQueue(async (_, token) =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await release.Task; // 취소를 무시하는 대역
            if (failLate) throw new InvalidOperationException("PRIVATE_QUEUE_INPUT");
        }, events.Enqueue);
        try
        {
            Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message(1)));
            await started.Task.WaitAsync(Limit);
            Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message(2)));
            await Assert.ThrowsAsync<TimeoutException>(() => queue.StopAsync(TimeSpan.FromMilliseconds(50)));
            Assert.Equal(1, Assert.Single(events, entry => entry.Code == "ShutdownDiscarded").Count);
            Assert.Single(events, entry => entry.Code == "StopTimeout");
            Assert.DoesNotContain(events, entry => entry.Code == "Stopped");
            Assert.Equal(QueueAdmission.NotAccepting, queue.TryEnqueue(Message(3)));
        }
        finally
        {
            release.TrySetResult();
            await queue.RequestStop().WaitAsync(Limit);
        }
        Assert.Equal(1, calls);
        Assert.Single(events, entry => entry.Code == "Stopped");
        Assert.Equal(failLate ? 1 : 0, events.Count(entry => entry.Code == "HandlerFailed"));
        Assert.DoesNotContain("PRIVATE_QUEUE_INPUT", string.Join(' ', events));
    }

    [Fact]
    public async Task NewQueue_StartsWithFreshAmrRegistration()
    {
        var first = new AmrReceiveQueue((_, _) => Task.CompletedTask, amrLimit: 1);
        Assert.Equal(QueueAdmission.Accepted, first.TryEnqueue(Message(1)));
        await first.StopAsync(Limit);
        var second = new AmrReceiveQueue((_, _) => Task.CompletedTask, amrLimit: 1);
        Assert.Equal(QueueAdmission.Accepted, second.TryEnqueue(new("AMR_002", "heartbeat",
            new HeartbeatMessage("AMR_002", 2, "OK", DateTimeOffset.UtcNow), DateTimeOffset.UtcNow)));
        await second.StopAsync(Limit);
    }

    [Fact]
    public async Task ProcessingLogFailure_DoesNotStopFollowingMessage()
    {
        var finished = Signal();
        int calls = 0;
        var queue = new AmrReceiveQueue((_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("private");
            finished.TrySetResult();
            return Task.CompletedTask;
        }, _ => throw new ApplicationException("private log"));
        queue.TryEnqueue(Message(1));
        queue.TryEnqueue(Message(2));
        await finished.Task.WaitAsync(Limit);
        await queue.StopAsync(Limit);
        Assert.Equal(2, calls);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ReceivedMessage Message(long ts) => new("AMR_001", "heartbeat",
        new HeartbeatMessage("AMR_001", ts, "OK", DateTimeOffset.UtcNow), DateTimeOffset.UtcNow);
}
