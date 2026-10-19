using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Amr.Communication.Models;
using Amr.Communication.Mqtt;
using Amr.Communication.Validation;
using MQTTnet;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class AmrReceiveQueueBoundaryTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Stop_AfterDequeueBeforeInvocation_DiscardsReservedMessageWithoutCallingHandler()
    {
        var events = new ConcurrentQueue<QueueProcessingEvent>();
        int calls = 0;
        var queue = new AmrReceiveQueue((_, _) => { Interlocked.Increment(ref calls); return Task.CompletedTask; }, events.Enqueue);
        // 실제 호출 직전만 멈춰 경합 순서를 고정한다. 제품 API에 시험용 콜백을 추가하지 않는다.
        var dispatch = Assert.IsType<ReaderWriterLockSlim>(typeof(AmrReceiveQueue)
            .GetField("_dispatchGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue));
        var held = Signal();
        using var release = new ManualResetEventSlim();
        Task blocker = Task.Run(() =>
        {
            dispatch.EnterWriteLock();
            try { held.SetResult(); Assert.True(release.Wait(Limit)); }
            finally { dispatch.ExitWriteLock(); }
        });
        try
        {
            await held.Task.WaitAsync(Limit);
            Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message("AMR_001", 1)));
            await WaitUntilAsync(() => dispatch.WaitingReadCount == 1);
            Task stopping = queue.RequestStop();
            Assert.Equal(QueueAdmission.NotAccepting, queue.TryEnqueue(Message("AMR_001", 2)));
            Assert.Equal(0, Volatile.Read(ref calls));
            release.Set();
            await stopping.WaitAsync(Limit);
        }
        finally
        {
            release.Set();
            await blocker.WaitAsync(Limit);
            await queue.StopAsync(Limit);
        }
        Assert.Equal(0, calls);
        Assert.Equal(1, events.Where(entry => entry.Code == "ShutdownDiscarded").Sum(entry => entry.Count));
        Assert.Single(events, entry => entry.Code == "Stopped");
    }

    [Fact]
    public async Task Stop_WaitsForSynchronousInvocationBoundary_TimeoutStillReturnsAndLateCleanupCompletes()
    {
        var started = Signal();
        using var release = new ManualResetEventSlim();
        CancellationToken handlerToken = default;
        var events = new ConcurrentQueue<QueueProcessingEvent>();
        var queue = new AmrReceiveQueue((_, token) =>
        {
            handlerToken = token;
            started.SetResult();
            Assert.True(release.Wait(Limit));
            return Task.Delay(Timeout.Infinite, token);
        }, events.Enqueue);
        try
        {
            queue.TryEnqueue(Message("AMR_001", 1));
            await started.Task.WaitAsync(Limit);
            queue.TryEnqueue(Message("AMR_001", 2));
            await Assert.ThrowsAsync<TimeoutException>(() => queue.StopAsync(TimeSpan.FromMilliseconds(50)));
            Assert.False(handlerToken.IsCancellationRequested);
            Assert.Equal(QueueAdmission.NotAccepting, queue.TryEnqueue(Message("AMR_002", 3)));
        }
        finally
        {
            release.Set();
            await queue.RequestStop().WaitAsync(Limit);
        }
        Assert.True(handlerToken.IsCancellationRequested);
        Assert.Single(events, entry => entry.Code == "StopTimeout");
        Assert.Single(events, entry => entry.Code == "HandlerCancelled");
        Assert.Equal(1, events.Where(entry => entry.Code == "ShutdownDiscarded").Sum(entry => entry.Count));
        Assert.Single(events, entry => entry.Code == "Stopped");
    }

    [Fact]
    public async Task ConcurrentAdmissionAndStop_NeverInvokeHandlerWithAnAlreadyCancelledToken()
    {
        int calledWithCancellation = 0;
        for (int iteration = 0; iteration < 5000; iteration++)
        {
            var queue = new AmrReceiveQueue((_, token) =>
            {
                if (token.IsCancellationRequested) Interlocked.Increment(ref calledWithCancellation);
                return Task.CompletedTask;
            }, _ => { });
            Task[] writers = Enumerable.Range(0, 4).Select(index => Task.Run(() =>
                queue.TryEnqueue(Message($"AMR_{index}", iteration)))).ToArray();
            Task stop = Task.Run(async () => { await Task.Yield(); await queue.RequestStop(); });
            await Task.WhenAll(writers.Append(stop)).WaitAsync(Limit);
        }
        Assert.Equal(0, calledWithCancellation);
    }

    [Fact]
    public async Task Subscription_QueueAndAmrLimitsRejectNewInput_WithoutBlockingOrMisclassifyingValidation()
    {
        var started = Signal();
        var release = Signal();
        var secondProcessed = Signal();
        var processed = new ConcurrentQueue<ReceivedMessage>();
        var admissions = new List<QueueAdmission>();
        var rejected = new List<ValidationError>();
        var callbackErrors = new List<CallbackFailure>();
        var queue = new AmrReceiveQueue(async (message, token) =>
        {
            if (message.AmrId == "AMR_001") { started.TrySetResult(); await release.Task.WaitAsync(token); }
            processed.Enqueue(message);
            if (message.AmrId == "AMR_002") secondProcessed.TrySetResult();
        }, capacityPerAmr: 2, amrLimit: 2);
        var client = new FakeMqttClient { IsConnected = true };
        using var subscription = new MessageSubscription(client, message => admissions.Add(queue.TryEnqueue(message)),
            rejected.Add, callbackErrors.Add);
        client.SubscribeResponse.SetResult(new MqttClientSubscribeResult(1,
            MessageSubscription.TopicFilters.Select(topic => new MqttClientSubscribeResultItem(new() { Topic = topic },
                MqttClientSubscribeResultCode.GrantedQoS0)).ToList(), "", []));
        await subscription.SubscribeAsync();
        try
        {
            await client.ReceiveAsync(Packet("AMR_001", "{}"));
            await client.ReceiveAsync(Packet("AMR_001", Payload("AMR_001", 1))).WaitAsync(Limit);
            await started.Task.WaitAsync(Limit);
            foreach (int number in new[] { 2, 3, 4 })
                await client.ReceiveAsync(Packet("AMR_001", Payload("AMR_001", number))).WaitAsync(Limit);
            await client.ReceiveAsync(Packet("AMR_002", Payload("AMR_002", 5))).WaitAsync(Limit);
            await secondProcessed.Task.WaitAsync(Limit);
            await client.ReceiveAsync(Packet("AMR_003", Payload("AMR_003", 6))).WaitAsync(Limit);
            Assert.False(release.Task.IsCompleted);
            Assert.Equal("AMR_002", Assert.Single(processed).AmrId);
        }
        finally
        {
            subscription.Dispose();
            release.TrySetResult();
            await queue.CompleteAsync().WaitAsync(Limit);
            await queue.StopAsync(Limit);
        }
        Assert.Equal(new[] { QueueAdmission.Accepted, QueueAdmission.Accepted, QueueAdmission.Accepted,
            QueueAdmission.QueueFull, QueueAdmission.Accepted, QueueAdmission.AmrLimitReached }, admissions);
        Assert.Single(rejected);
        Assert.Empty(callbackErrors);
        Assert.Equal(new long[] { 1, 2, 3 }, processed.Where(message => message.AmrId == "AMR_001")
            .Select(message => Assert.IsType<HeartbeatMessage>(message.Model).Timestamp));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ReceivedMessage Message(string id, long timestamp) => new(id, "heartbeat",
        new HeartbeatMessage(id, timestamp, "OK", DateTimeOffset.UtcNow), DateTimeOffset.UtcNow);
    private static string Payload(string id, int timestamp) => $$"""{"id":"{{id}}","ts":{{timestamp}},"status":"OK"}""";
    private static MqttApplicationMessage Packet(string id, string payload) => new MqttApplicationMessageBuilder()
        .WithTopic($"amr/{id}/heartbeat").WithPayload(payload).Build();
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > Limit) throw new TimeoutException("Expected dispatch boundary was not reached.");
            await Task.Delay(1);
        }
    }
}
