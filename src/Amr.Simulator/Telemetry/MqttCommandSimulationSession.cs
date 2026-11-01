using System.Buffers;
using System.Text.Json;
using System.Threading.Channels;
using Amr.Communication.Command;
using Amr.Communication.Mqtt;
using Amr.Simulator.Configuration;
using Amr.Simulator.Simulation;
using MQTTnet;
using MQTTnet.Protocol;

namespace Amr.Simulator.Telemetry;

// MQTT 콜백은 검사/큐 접수만 한다. 시간 갱신·명령 수행·최종 응답은 아래 단일 루프가 소유한다.
public static class MqttCommandSimulationSession
{
    public static async Task<int> RunAsync(SimulatorSettings settings, SimulatorMqttOptions options,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var motion = new CommandDrivenAmr(settings);
        var queue = Channel.CreateBounded<CommandMessage>(new BoundedChannelOptions(128)
            { SingleReader = true, AllowSynchronousContinuations = false, FullMode = BoundedChannelFullMode.Wait });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var client = new MqttClientFactory().CreateMqttClient();
        using var connection = new MqttConnection(client);
        string topic = $"amr/{settings.AmrId}/command";
        int accepting = 0, failed = 0, closing = 0;
        output = TextWriter.Synchronized(output); error = TextWriter.Synchronized(error);
        Task Receive(MqttApplicationMessageReceivedEventArgs args)
        {
            if (Volatile.Read(ref accepting) == 0 || args.ApplicationMessage.Topic != topic) return Task.CompletedTask;
            var result = new CommandValidator().Validate(args.ApplicationMessage.Topic, args.ApplicationMessage.Payload);
            if (!result.IsValid || args.ApplicationMessage.Retain)
                output.WriteLine($"CommandRejected Reason={result.Error?.Code.ToString() ?? "RETAINED_COMMAND"}");
            else if (!queue.Writer.TryWrite(result.Message!))
            {
                if (!stop.IsCancellationRequested) { Interlocked.Exchange(ref failed, 1); error.WriteLine("CommandQueueFull"); stop.Cancel(); }
            }
            return Task.CompletedTask;
        }
        Task Disconnected(MqttClientDisconnectedEventArgs args)
        {
            if (args.ClientWasConnected && Volatile.Read(ref closing) == 0)
            { Interlocked.Exchange(ref failed, 1); error.WriteLine("CommandSessionDisconnected"); stop.Cancel(); }
            return Task.CompletedTask;
        }
        client.ApplicationMessageReceivedAsync += Receive;
        client.DisconnectedAsync += Disconnected;
        try
        {
            await connection.ConnectAsync(options.Connection, stop.Token);
            Volatile.Write(ref accepting, 1);
            using var subscriptionTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            subscriptionTimeout.CancelAfter(options.Connection.ConnectTimeout);
            var result = await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter(topic, MqttQualityOfServiceLevel.AtMostOnce).Build(), subscriptionTimeout.Token);
            if (result.Items.Count != 1 || result.Items.Single().ResultCode != MqttClientSubscribeResultCode.GrantedQoS0)
                throw new IOException("CommandSubscriptionRejected");
            await output.WriteLineAsync($"CommandSessionReady AmrId={settings.AmrId} Topic={topic}");
            await Telemetry(); await Heartbeat();
            var clock = TimeProvider.System;
            long last = clock.GetTimestamp(), lastTelemetry = last, lastHeartbeat = last;
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Min(settings.UpdateIntervalMs, Math.Min(options.TelemetryMs, options.HeartbeatMs))));
            while (await timer.WaitForNextTickAsync(stop.Token))
            {
                long now = clock.GetTimestamp();
                var replies = new List<CommandFinalReply>(motion.Advance(clock.GetElapsedTime(last, now).TotalSeconds));
                last = now;
                bool commandReceived = false;
                while (queue.Reader.TryRead(out var command))
                {
                    commandReceived = true;
                    var execution = motion.Execute(command);
                    replies.AddRange(execution.Replies);
                    await output.WriteLineAsync($"CommandProcessed AmrId={settings.AmrId} Type={command.Type} CmdId={command.CommandId} State={motion.State} Diagnostic={execution.Diagnostic ?? "NONE"}");
                }
                if (commandReceived || replies.Count != 0 || clock.GetElapsedTime(lastTelemetry, now).TotalMilliseconds >= options.TelemetryMs)
                { await Telemetry(); lastTelemetry = now; }
                foreach (var reply in replies)
                {
                    await Publish("ack", new { id = settings.AmrId, cmdId = reply.CommandId, result = reply.Result, ts = reply.Timestamp, reason = reply.Reason });
                    await output.WriteLineAsync($"FinalAckPublished AmrId={settings.AmrId} CmdId={reply.CommandId} Result={reply.Result} Reason={reply.Reason} Delivery=Unconfirmed");
                }
                if (clock.GetElapsedTime(lastHeartbeat, now).TotalMilliseconds >= options.HeartbeatMs)
                { await Heartbeat(); lastHeartbeat = now; }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception exception)
        { Interlocked.Exchange(ref failed, 1); await error.WriteLineAsync($"CommandSessionFailed Reason={exception.GetType().Name}"); }
        finally
        {
            Volatile.Write(ref closing, 1); Volatile.Write(ref accepting, 0);
            queue.Writer.TryComplete();
            client.ApplicationMessageReceivedAsync -= Receive;
            client.DisconnectedAsync -= Disconnected;
            try
            {
                if (client.IsConnected)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await client.UnsubscribeAsync(new MqttClientUnsubscribeOptionsBuilder().WithTopicFilter(topic).Build(), timeout.Token);
                }
            }
            catch (Exception exception) { failed = 1; await error.WriteLineAsync($"CommandUnsubscribeFailed Reason={exception.GetType().Name}"); }
            try { await connection.DisconnectAsync(); }
            catch (Exception exception) { failed = 1; await error.WriteLineAsync($"CommandDisconnectFailed Reason={exception.GetType().Name}"); }
        }
        await output.WriteLineAsync($"CommandSessionEnded ExitCode={failed}");
        return failed;

        Task Telemetry() => Publish("telemetry", new { id = settings.AmrId, ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            pos = new { x = motion.Position.X, y = motion.Position.Y }, sp = motion.SpeedMps, state = motion.State });
        Task Heartbeat() => Publish("heartbeat", new { id = settings.AmrId, ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), status = "OK" });
        async Task Publish(string kind, object value)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            var result = await client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic($"amr/{settings.AmrId}/{kind}")
                .WithPayload(JsonSerializer.SerializeToUtf8Bytes(value)).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce).WithRetainFlag(false).Build(), deadline.Token);
            if (!result.IsSuccess) throw new IOException("PublicationRejected");
        }
    }
}
