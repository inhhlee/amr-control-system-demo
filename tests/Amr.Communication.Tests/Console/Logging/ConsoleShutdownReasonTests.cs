using System.Collections.Concurrent;
using Amr.Communication.ConsoleApp;
using Amr.Communication.ConsoleApp.Logging;
using Amr.Communication.Mqtt;
using MQTTnet;
using MQTTnet.Packets;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class ConsoleShutdownReasonTests
{
    [Theory]
    [InlineData(false, "user")]
    [InlineData(true, "user")]
    [InlineData(false, "lost")]
    [InlineData(true, "lost")]
    [InlineData(false, "user-then-lost")]
    [InlineData(true, "user-then-lost")]
    [InlineData(false, "lost-then-user")]
    [InlineData(true, "lost-then-user")]
    public async Task SubscriptionAndReceiveWait_PreserveFirstShutdownCause(bool subscribed, string order)
    {
        await using var run = new Run();
        await run.ConnectAsync(subscribed);
        foreach (string cause in order.Split("-then-"))
        {
            if (cause == "user") run.User.Cancel();
            else await run.LoseConnectionAsync();
        }
        bool lostFirst = order.StartsWith("lost", StringComparison.Ordinal);
        await run.AssertResultAsync(lostFirst ? "ConnectionLost" : "UserCanceled", lostFirst ? 1 : 0);
    }

    [Fact]
    public async Task InitialConnectCancellation_RemainsUserCanceledAndExitZero()
    {
        await using var run = new Run();
        await run.Client.ConnectEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        run.User.Cancel();
        await run.AssertResultAsync("UserCanceled", 0, "ConnectCanceled");
        Assert.DoesNotContain(" Connected", run.Output.ToString());
    }

    [Fact]
    public async Task UserCancellationDuringDisconnectCallback_DoesNotOverwriteCauseOrReleaseWaitEarly()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var run = new Run(e =>
        {
            if (EventType(e) != "ConnectionLost") return;
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Test gate timed out");
        });
        await run.ConnectAsync(false);
        var lost = Task.Run(run.LoseConnectionAsync);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            run.User.Cancel();
            Assert.False(run.Completion.IsCompleted);
        }
        finally { release.Set(); }
        await lost.WaitAsync(TimeSpan.FromSeconds(3));
        await run.AssertResultAsync("ConnectionLost", 1);
    }

    [Fact]
    public async Task ConcurrentSignals_KeepReasonExitCodeAndConnectionLostEventConsistent()
    {
        for (int iteration = 0; iteration < 32; iteration++)
        {
            await using var run = new Run();
            await run.ConnectAsync(true);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var user = Task.Run(async () => { await start.Task; run.User.Cancel(); });
            var lost = Task.Run(async () => { await start.Task; await run.LoseConnectionAsync(); });
            start.SetResult();
            await Task.WhenAll(user, lost).WaitAsync(TimeSpan.FromSeconds(3));
            await run.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            string reason = Property(Assert.Single(run.Sink.Events, e => EventType(e) == "ShutdownRequested"), "Reason");
            Assert.Contains(reason, new[] { "UserCanceled", "ConnectionLost" });
            await run.AssertResultAsync(reason, reason == "ConnectionLost" ? 1 : 0);
        }
    }

    private static string EventType(LogEvent e) => Property(e, "EventType");
    private static string Property(LogEvent e, string name) => ((ScalarValue)e.Properties[name]).Value!.ToString()!;

    private sealed class Run : IAsyncDisposable
    {
        internal FakeMqttClient Client { get; } = new();
        internal CancellationTokenSource User { get; } = new();
        internal StringWriter Output { get; } = new();
        private StringWriter Error { get; } = new();
        internal Capture Sink { get; }
        private readonly Logger _logger;
        internal Task<int> Completion { get; }

        internal Run(Action<LogEvent>? onEmit = null)
        {
            Sink = new Capture(onEmit);
            _logger = new LoggerConfiguration().WriteTo.Sink(Sink).CreateLogger();
            Client.DisconnectCompletion.SetResult();
            Client.UnsubscribeResponse.SetResult(new MqttClientUnsubscribeResult(2,
                MessageSubscription.TopicFilters.Select(t => new MqttClientUnsubscribeResultItem(t,
                    MqttClientUnsubscribeResultCode.Success)).ToList(), "", []));
            Completion = ConsoleRunner.RunAsync([], () => Client, User.Token,
                TextWriter.Synchronized(Output), TextWriter.Synchronized(Error), new StructuredLog(_logger));
        }

        internal async Task ConnectAsync(bool subscribed)
        {
            Client.ConnectResponse.SetResult(new() { ResultCode = MqttClientConnectResultCode.Success });
            await Client.SubscribeEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (!subscribed) return;
            Client.SubscribeResponse.SetResult(new MqttClientSubscribeResult(1,
                MessageSubscription.TopicFilters.Select(t => new MqttClientSubscribeResultItem(
                    new MqttTopicFilter { Topic = t }, MqttClientSubscribeResultCode.GrantedQoS0)).ToList(), "", []));
            await Sink.Subscribed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }

        internal Task LoseConnectionAsync()
        {
            Client.IsConnected = false;
            return Client.NotifyDisconnectedAsync(new(true, null, MqttClientDisconnectReason.UnspecifiedError, null, null, null));
        }

        internal async Task AssertResultAsync(string reason, int expectedExit, string eventType = "ShutdownRequested")
        {
            Assert.Equal(expectedExit, await Completion.WaitAsync(TimeSpan.FromSeconds(3)));
            var termination = Assert.Single(Sink.Events, e => EventType(e) is "ConnectCanceled" or "ShutdownRequested");
            Assert.Equal(eventType, EventType(termination));
            Assert.Equal(reason, Property(termination, "Reason"));
            Assert.Equal(reason == "ConnectionLost" ? 1 : 0, Sink.Events.Count(e => EventType(e) == "ConnectionLost"));
            Assert.Equal(expectedExit.ToString(), Property(Assert.Single(Sink.Events, e => EventType(e) == "ExitCode"), "ExitCode"));
            Assert.Contains(eventType == "ConnectCanceled" ? "ConnectCanceled Reason=UserCanceled" : "ShutdownRequested", Output.ToString());
            Assert.Contains($"ExitCode={expectedExit}", Output.ToString());
            Assert.Equal("", Error.ToString());
            Assert.Equal(1, Client.DisposeCalls);
            Assert.Equal(0, Client.MessageHandlerCount);
        }

        public async ValueTask DisposeAsync()
        {
            User.Cancel();
            try { await Completion.WaitAsync(TimeSpan.FromSeconds(4)); }
            finally { User.Dispose(); _logger.Dispose(); Output.Dispose(); Error.Dispose(); }
        }
    }

    private sealed class Capture(Action<LogEvent>? onEmit) : ILogEventSink
    {
        internal ConcurrentQueue<LogEvent> Events { get; } = new();
        internal TaskCompletionSource Subscribed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Emit(LogEvent e)
        {
            Events.Enqueue(e);
            if (EventType(e) == "Subscribed") Subscribed.TrySetResult();
            onEmit?.Invoke(e);
        }
    }
}
