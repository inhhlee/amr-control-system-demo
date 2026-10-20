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
using Xunit.Abstractions;

namespace Amr.Communication.Tests;

public sealed class ConsoleShutdownReasonTests(ITestOutputHelper testOutput)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateDisconnect_WhileFirstCallbackIsInFlight_ShutsDownOnce(bool subscribed)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var run = new Run(e =>
        {
            if (EventType(e) != "ConnectionLost") return;
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Test callback gate timed out");
        });
        await run.ConnectAsync(subscribed);
        var callback = run.Client.CaptureDisconnectedCallback();
        var notification = new MqttClientDisconnectedEventArgs(true, null,
            MqttClientDisconnectReason.UnspecifiedError, null, null, null);
        run.Client.IsConnected = false;
        Task first = Task.Run(() => callback(notification));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            // The winner is inside logging, before setting exitCode and canceling the wait.
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => callback(notification))))
                .WaitAsync(TimeSpan.FromSeconds(3));
            run.User.Cancel();
            Assert.False(run.Completion.IsCompleted);
            Assert.Single(run.Sink.Events, e => EventType(e) == "ConnectionLost");
            testOutput.WriteLine($"Subscribed={subscribed} Order=first-lost-held -> 8-duplicates-completed -> user-cancel -> first-released; CompletionBeforeRelease=False");
        }
        finally { release.Set(); }
        await first.WaitAsync(TimeSpan.FromSeconds(3));
        await run.AssertResultAsync("ConnectionLost", 1);
        Assert.Equal(0, run.Client.DisconnectCalls);
        WriteEvidence(run);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CapturedDisconnect_AfterExit_CannotChangeResultOrReconnect(bool subscribed, bool lostFirst)
    {
        await using var run = new Run();
        await run.ConnectAsync(subscribed);
        var staleCallback = run.Client.CaptureDisconnectedCallback();
        if (lostFirst) await run.LoseConnectionAsync();
        else run.User.Cancel();
        await run.AssertResultAsync(lostFirst ? "ConnectionLost" : "UserCanceled", lostFirst ? 1 : 0);
        string finalOutput = run.Output.ToString();
        var finalEvents = run.Sink.Events.ToArray();
        int disconnectCalls = run.Client.DisconnectCalls;
        var late = new MqttClientDisconnectedEventArgs(true, null,
            MqttClientDisconnectReason.UnspecifiedError, null, null, null);
        // Calling the captured delegate is stronger than publishing to an empty event list.
        await staleCallback(late).WaitAsync(TimeSpan.FromSeconds(3));
        await staleCallback(late).WaitAsync(TimeSpan.FromSeconds(3));
        await run.Client.NotifyDisconnectedAsync(late);
        run.User.Cancel();
        Assert.Equal(finalOutput, run.Output.ToString());
        Assert.Equal(finalEvents, run.Sink.Events.ToArray());
        Assert.Equal(disconnectCalls, run.Client.DisconnectCalls);
        Assert.Equal(1, run.Client.ConnectCalls);
        await run.AssertResultAsync(lostFirst ? "ConnectionLost" : "UserCanceled", lostFirst ? 1 : 0);
        testOutput.WriteLine($"Subscribed={subscribed} LostFirst={lostFirst} Order=terminal-exit -> captured-late-callback-twice -> event-notify -> user-cancel; OutputAndEventsUnchanged=True");
        WriteEvidence(run);
    }

    [Fact]
    public async Task UserCancellation_ThenDisconnectDuringExplicitCleanup_RemainsExitZero()
    {
        await using var run = new Run(holdDisconnect: true);
        await run.ConnectAsync(true);
        var callback = run.Client.CaptureDisconnectedCallback();
        run.User.Cancel();
        try
        {
            await run.Client.DisconnectEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(run.Completion.IsCompleted);
            Assert.Equal(0, run.Client.DisposeCalls);
            Assert.Equal(MqttClientDisconnectOptionsReason.NormalDisconnection, run.Client.DisconnectOptions!.Reason);
            run.Client.IsConnected = false;
            await callback(new(true, null, MqttClientDisconnectReason.NormalDisconnection, null, null, null));
            await callback(new(true, null, MqttClientDisconnectReason.UnspecifiedError, null, null, null));
            Assert.False(run.Completion.IsCompleted);
            Assert.DoesNotContain(run.Sink.Events, e => EventType(e) == "ConnectionLost");
            testOutput.WriteLine("Order=user-cancel -> explicit-disconnect-held -> normal-and-unexpected-callbacks -> disconnect-released; CompletionBeforeRelease=False");
        }
        finally { run.Client.DisconnectCompletion.TrySetResult(); }
        await run.AssertResultAsync("UserCanceled", 0);
        Assert.Equal(1, run.Client.DisconnectCalls);
        WriteEvidence(run);
    }

    private void WriteEvidence(Run run)
    {
        testOutput.WriteLine($"ClientId={run.Client.Options.ClientId} Connected={run.Client.IsConnected} DisposeCalls={run.Client.DisposeCalls} DisconnectCalls={run.Client.DisconnectCalls} MessageHandlers={run.Client.MessageHandlerCount} DisconnectHandlers={run.Client.DisconnectedHandlerCount}");
        testOutput.WriteLine(run.Output.ToString());
    }

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

    [Theory]
    [InlineData(MqttClientDisconnectReason.ServerShuttingDown, false)]
    [InlineData(MqttClientDisconnectReason.NormalDisconnection, false)]
    [InlineData(MqttClientDisconnectReason.UnspecifiedError, true)]
    public async Task UnexpectedDisconnect_RecordsSafeCauseAndTime_AndNeverTreatsProtocolCodeAsUserCancel(
        MqttClientDisconnectReason mqttReason, bool transportError)
    {
        await using var run = new Run();
        await run.ConnectAsync(true);
        const string secret = "must-not-log-secret\r\nforged-event";
        Exception? exception = transportError
            ? new IOException(secret, new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset))
            : null;
        var before = DateTimeOffset.UtcNow;
        run.Client.IsConnected = false;
        await run.Client.NotifyDisconnectedAsync(new(true, null, mqttReason, secret,
            [new MqttUserProperty("private", new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(secret)))], exception));
        await run.AssertResultAsync("ConnectionLost", 1);
        var after = DateTimeOffset.UtcNow;
        var lost = Assert.Single(run.Sink.Events, e => EventType(e) == "ConnectionLost");
        Assert.Equal(LogEventLevel.Warning, lost.Level);
        Assert.Equal(mqttReason.ToString(), Property(lost, "Reason"));
        Assert.Equal("True", Property(lost, "ClientWasConnected"));
        Assert.Equal(transportError ? "IOException" : "None", Property(lost, "ExceptionType"));
        Assert.Equal(transportError ? "ConnectionReset" : "None", Property(lost, "SocketError"));
        Assert.Equal(run.Client.Options.ClientId, Property(lost, "ClientId"));
        var detected = Assert.IsType<DateTimeOffset>(((ScalarValue)lost.Properties["DetectedAtUtc"]).Value);
        Assert.Equal(TimeSpan.Zero, detected.Offset);
        Assert.InRange(detected, before, after);
        Assert.Contains($"ConnectionLost Reason={mqttReason} ClientWasConnected=True", run.Output.ToString());
        Assert.Contains("ShutdownRequested Reason=ConnectionLost", run.Output.ToString());
        Assert.DoesNotContain(secret, run.Output.ToString());
        Assert.All(run.Sink.Events, e => Assert.DoesNotContain(secret, string.Join(";", e.Properties)));
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

        internal Run(Action<LogEvent>? onEmit = null, bool holdDisconnect = false)
        {
            Sink = new Capture(onEmit);
            _logger = new LoggerConfiguration().WriteTo.Sink(Sink).CreateLogger();
            if (!holdDisconnect) Client.DisconnectCompletion.SetResult();
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
            Assert.Equal(0, Client.DisconnectedHandlerCount);
            Assert.False(Client.IsConnected);
            foreach (string lifecycle in new[] { "QueueStopped", "SubscriptionStopped", "Disposed" })
                Assert.Single(Sink.Events, e => EventType(e) == lifecycle);
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
