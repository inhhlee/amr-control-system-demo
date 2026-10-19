using System.Collections.Concurrent;
using Amr.Communication.Models;
using Amr.Communication.Mqtt;
using Amr.Communication.Validation;
using MQTTnet;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class AmrReceiveQueueTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Subscription_AllKindsShareOneQueue_AnotherAmrFinishesWhileFirstWaits()
    {
        var firstStarted = Signal();
        var releaseFirst = Signal();
        var secondFinished = Signal();
        var started = new ConcurrentQueue<ReceivedMessage>();
        var finished = new ConcurrentQueue<ReceivedMessage>();
        var accepted = new List<ReceivedMessage>();
        var admission = new List<QueueAdmission>();
        var rejections = new List<ValidationError>();
        var callbackFailures = new List<CallbackFailure>();
        var queue = new AmrReceiveQueue(async message =>
        {
            started.Enqueue(message);
            if (message.AmrId == "AMR_001" && message.Kind == "heartbeat")
            {
                firstStarted.SetResult();
                await releaseFirst.Task;
            }
            finished.Enqueue(message);
            if (message.AmrId == "AMR_002") secondFinished.SetResult();
        });
        var client = new FakeMqttClient { IsConnected = true };
        using var subscription = new MessageSubscription(client, message =>
        {
            accepted.Add(message);
            admission.Add(queue.TryEnqueue(message));
        }, rejections.Add, callbackFailures.Add);
        Approve(client);
        await subscription.SubscribeAsync();

        try
        {
            // 실제 수신부·검사기를 사용한다. 불량 입력은 큐에 오지 않는다.
            await client.ReceiveAsync(Packet("AMR_001", "heartbeat", "{}"));
            await client.ReceiveAsync(Packet("AMR_001", "heartbeat", """{"id":"AMR_001","ts":40,"status":"OK"}"""));
            await firstStarted.Task.WaitAsync(TestTimeout);
            await client.ReceiveAsync(Packet("AMR_001", "telemetry", """{"id":"AMR_001","ts":30,"pos":{"x":1.2,"y":-3.4},"sp":0.5,"state":"BUSY"}"""));
            await client.ReceiveAsync(Packet("AMR_001", "alarm", """{"id":"AMR_001","ts":20,"code":" A01 ","level":"WARN","alarmState":"CLEARED","message":" 경고 "}"""));
            await client.ReceiveAsync(Packet("AMR_001", "ack", """{"cmdId":" CMD_7 ","result":"FAILURE","ts":10,"reason":" 이동 불가 "}"""));
            await client.ReceiveAsync(Packet("AMR_002", "ack", """{"cmdId":"CMD_8","result":"SUCCESS","ts":1}"""));

            await secondFinished.Task.WaitAsync(TestTimeout);
            Assert.Equal("AMR_002", Assert.Single(finished).AmrId);
            Assert.Single(started, message => message.AmrId == "AMR_001");
            Assert.False(releaseFirst.Task.IsCompleted);
        }
        finally
        {
            subscription.Dispose();
            releaseFirst.TrySetResult();
            await queue.CompleteAsync().WaitAsync(TestTimeout);
        }

        Assert.Single(rejections);
        Assert.Empty(callbackFailures);
        Assert.Equal(5, admission.Count);
        Assert.All(admission, result => Assert.Equal(QueueAdmission.Accepted, result));
        var firstMessages = finished.Where(message => message.AmrId == "AMR_001").ToArray();
        Assert.Equal(new[] { "heartbeat", "telemetry", "alarm", "ack" }, firstMessages.Select(message => message.Kind));
        // 원래 객체를 그대로 전달하므로 모델·대상·최초 수신 시각을 다시 만들지 않는다.
        for (int index = 0; index < firstMessages.Length; index++) Assert.Same(accepted[index], firstMessages[index]);
        var ack = Assert.IsType<AckMessage>(firstMessages[3].Model);
        Assert.Equal(new AckMessage(" CMD_7 ", "FAILURE", 10, null, " 이동 불가 ", firstMessages[3].ReceivedAtUtc), ack);
    }

    [Fact]
    public async Task HandlerSynchronousPrefix_DoesNotRunOnEnqueuingThreadOrHoldQueueLock()
    {
        var started = Signal();
        var secondFinished = Signal();
        using var release = new ManualResetEventSlim();
        var queue = new AmrReceiveQueue(message =>
        {
            if (message.AmrId == "AMR_001")
            {
                started.TrySetResult();
                // first await가 없는 느린 처리자를 시험에서만 흉내 낸다. 실제 처리자는 비동기로 작성한다.
                release.Wait();
            }
            else secondFinished.TrySetResult();
            return Task.CompletedTask;
        });
        try
        {
            // 처리자를 접수 함수 안에서 잘못 실행해도 시험은 제한 시간 뒤 실패하고 finally에서 대기를 푼다.
            Assert.Equal(QueueAdmission.Accepted,
                await Task.Run(() => queue.TryEnqueue(Message("AMR_001", 1))).WaitAsync(TestTimeout));
            await started.Task.WaitAsync(TestTimeout);
            Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message("AMR_001", 2)));
            Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message("AMR_002", 3)));
            await secondFinished.Task.WaitAsync(TestTimeout);
            Assert.False(release.IsSet);
        }
        finally
        {
            release.Set();
            await queue.CompleteAsync().WaitAsync(TestTimeout);
        }
    }

    [Fact]
    public async Task ConcurrentProducers_CreateOnlyOneConsumerForSameAmr()
    {
        var start = Signal();
        var processed = new ConcurrentQueue<ReceivedMessage>();
        int active = 0, overlap = 0;
        var queue = new AmrReceiveQueue(async message =>
        {
            if (Interlocked.Increment(ref active) != 1) Interlocked.Increment(ref overlap);
            await Task.Yield();
            processed.Enqueue(message);
            Interlocked.Decrement(ref active);
        });
        var producers = Enumerable.Range(0, 8).Select(producer => Task.Run(async () =>
        {
            await start.Task;
            for (int index = 0; index < 10; index++)
                Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message("AMR_001", producer * 100 + index)));
        })).ToArray();
        try
        {
            start.SetResult();
            await Task.WhenAll(producers).WaitAsync(TestTimeout);
        }
        finally { await queue.CompleteAsync().WaitAsync(TestTimeout); }
        Assert.Equal(0, overlap);
        Assert.Equal(80, processed.Count);
        var timestamps = processed.Select(message => Assert.IsType<HeartbeatMessage>(message.Model).Timestamp).ToArray();
        Assert.Equal(80, timestamps.Distinct().Count());
        for (int producer = 0; producer < 8; producer++)
            Assert.Equal(Enumerable.Range(0, 10).Select(index => (long)(producer * 100 + index)),
                timestamps.Where(timestamp => timestamp / 100 == producer));
    }

    [Fact]
    public async Task AmrKeys_PreserveCaseAndUnicodeWithoutNormalization()
    {
        string[] ids = ["AMR_001", "amr_001", "장비-é", "장비-e\u0301"];
        var started = ids.ToDictionary(id => id, _ => Signal(), StringComparer.Ordinal);
        var release = Signal();
        var queue = new AmrReceiveQueue(async message =>
        {
            started[message.AmrId].SetResult();
            await release.Task;
        });
        try
        {
            foreach (string id in ids) Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message(id, 1)));
            await Task.WhenAll(started.Values.Select(signal => signal.Task)).WaitAsync(TestTimeout);
        }
        finally
        {
            release.SetResult();
            await queue.CompleteAsync().WaitAsync(TestTimeout);
        }
    }

    [Fact]
    public async Task Capacity_CountsOnlyWaitingMessages_RejectsNewInputWithoutDroppingOldOnes()
    {
        var started = Signal();
        var release = Signal();
        var processed = new List<long>();
        var queue = new AmrReceiveQueue(async message =>
        {
            started.TrySetResult();
            await release.Task;
            processed.Add(Assert.IsType<HeartbeatMessage>(message.Model).Timestamp);
        }, capacityPerAmr: 2);
        try
        {
            Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message("AMR_001", 1)));
            await started.Task.WaitAsync(TestTimeout);
            Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message("AMR_001", 2)));
            Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message("AMR_001", 3)));
            Assert.Equal(QueueAdmission.QueueFull, queue.TryEnqueue(Message("AMR_001", 4)));
        }
        finally
        {
            release.SetResult();
            await queue.CompleteAsync().WaitAsync(TestTimeout);
        }
        Assert.Equal(new long[] { 1, 2, 3 }, processed);
    }

    [Fact]
    public async Task EmptyQueue_KeepsAmrRegistrationUntilCompletion()
    {
        var firstProcessed = Signal();
        int count = 0;
        var queue = new AmrReceiveQueue(_ =>
        {
            Interlocked.Increment(ref count);
            firstProcessed.TrySetResult();
            return Task.CompletedTask;
        }, amrLimit: 1);
        Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message("AMR_001", 1)));
        await firstProcessed.Task.WaitAsync(TestTimeout);
        Assert.Equal(QueueAdmission.AmrLimitReached, queue.TryEnqueue(Message("AMR_002", 2)));
        Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message("AMR_001", 3)));
        await queue.CompleteAsync().WaitAsync(TestTimeout);
        Assert.Equal(2, count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandlerFailure_ContinuesWithNextMessage(bool asynchronous)
    {
        var processed = new List<long>();
        var queue = new AmrReceiveQueue(message =>
        {
            long ts = Assert.IsType<HeartbeatMessage>(message.Model).Timestamp;
            if (ts == 1)
            {
                if (asynchronous) return FailAsync();
                throw new InvalidOperationException("private-payload");
            }
            processed.Add(ts);
            return Task.CompletedTask;
        });
        Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message("AMR_001", 1)));
        Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message("AMR_001", 2)));
        await queue.CompleteAsync().WaitAsync(TestTimeout);
        Assert.Equal(new long[] { 2 }, processed);

        static async Task FailAsync()
        {
            await Task.Yield();
            throw new InvalidOperationException("private-payload");
        }
    }

    [Fact]
    public async Task Complete_ClosesAdmissionAndWaitsForPreviouslyAcceptedWork()
    {
        var release = Signal();
        int count = 0;
        var queue = new AmrReceiveQueue(async _ => { await release.Task; Interlocked.Increment(ref count); });
        Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message("AMR_001", 1)));
        Assert.Equal(QueueAdmission.Accepted, queue.TryEnqueue(Message("AMR_001", 2)));
        Task completion = queue.CompleteAsync();
        Assert.Same(completion, queue.CompleteAsync());
        Assert.False(completion.IsCompleted);
        Assert.Equal(QueueAdmission.NotAccepting, queue.TryEnqueue(Message("AMR_002", 3)));
        release.SetResult();
        await completion.WaitAsync(TestTimeout);
        Assert.Equal(2, count);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static ReceivedMessage Message(string id, long timestamp)
    {
        var receivedAt = DateTimeOffset.Parse("2026-10-19T00:00:00Z");
        return new(id, "heartbeat", new HeartbeatMessage(id, timestamp, "OK", receivedAt), receivedAt);
    }

    private static MqttApplicationMessage Packet(string id, string kind, string json) =>
        new MqttApplicationMessageBuilder().WithTopic($"amr/{id}/{kind}").WithPayload(json).Build();

    private static void Approve(FakeMqttClient client) => client.SubscribeResponse.SetResult(new MqttClientSubscribeResult(1,
        MessageSubscription.TopicFilters.Select(topic => new MqttClientSubscribeResultItem(new() { Topic = topic },
            MqttClientSubscribeResultCode.GrantedQoS0)).ToList(), "", []));
}
