// Compiled with the actual proxy by Verify-ToolRecovery.ps1. No MQTT or Docker.
public static class ToolRecoveryProbe
{
    static string ReadShared(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(file);
        return reader.ReadToEnd();
    }

    static void Command(string directory, string value)
    {
        string path = Path.Combine(directory, "proxy.control");
        File.WriteAllText(path + ".tmp", value);
        File.Move(path + ".tmp", path, true);
    }

    public static async Task<string> RunAsync(string directory, string action)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient { ReceiveBufferSize = 1024 };
        TcpClient upstream = null;
        using var floodStop = new CancellationTokenSource();
        Task proxy = null, flood = null;
        bool pressure = false, pendingAck = false, stopAck = false, completed = false;
        bool succeededBeforePeerClosure = false, stoppedEvent = false, sessionClosed = false;
        long sent = 0;
        double elapsed = 0;
        string probeFailure = null, cleanupFailure = null;
        try
        {
            int brokerPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            proxy = ResponseGateProxy.RunAsync(brokerPort, directory);
            using var ready = JsonDocument.Parse(ReadShared(Path.Combine(directory, "proxy.ready.json")));
            int port = ready.RootElement.GetProperty("port").GetInt32();
            string logPath = Path.Combine(directory, "proxy.jsonl");
            if (action != "before-accept")
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                upstream = await listener.AcceptTcpClientAsync();
                async Task Flood()
                {
                    byte[] bytes = new byte[65536];
                    for (int n = 0; n < 1024; n++)
                    {
                        await upstream.GetStream().WriteAsync(bytes, floodStop.Token);
                        Interlocked.Add(ref sent, bytes.Length);
                    }
                }
                flood = Flood(); // The client deliberately never reads responses.
                string previous = "";
                for (int n = 0; n < 30; n++)
                {
                    await Task.Delay(200);
                    string current = ReadShared(logPath);
                    if (current.Contains("Forwarded") && current == previous && !flood.IsCompleted)
                    { pressure = true; break; }
                    previous = current;
                }
                if (!pressure) throw new InvalidOperationException("Backpressure was not established.");
                Command(directory, "1 " + action);
                await Task.Delay(500);
                pendingAck = ReadShared(logPath).Contains("ControlApplied");
            }
            var watch = Stopwatch.StartNew();
            Command(directory, "2 stop");
            completed = await Task.WhenAny(proxy, Task.Delay(2000)) == proxy;
            elapsed = watch.Elapsed.TotalMilliseconds;
            succeededBeforePeerClosure = proxy.IsCompletedSuccessfully;
            string finalLog = ReadShared(logPath);
            stopAck = finalLog.Contains("\"action\":\"stop\"");
            stoppedEvent = finalLog.Contains("\"eventType\":\"Stopped\"");
            sessionClosed = finalLog.Contains("\"eventType\":\"SessionClosed\"");
        }
        catch (Exception e) { probeFailure = e.ToString(); }
        finally
        {
            floodStop.Cancel();
            client.Dispose();
            upstream?.Dispose();
            listener.Stop();
            if (flood != null) try { await flood.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
            if (proxy != null) try { await proxy.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (Exception e) { cleanupFailure = e.GetType().Name; }
        }
        return JsonSerializer.Serialize(new {
            action, backpressureEstablished = pressure, bytesSentByFakeBroker = sent,
            pendingAcknowledgedBeforeStop = pendingAck, stopAcknowledged = stopAck,
            proxyCompletedWithin2Seconds = completed, stopToCompletionMilliseconds = elapsed,
            succeededBeforePeerClosure, stoppedEvent, sessionClosed, probeFailure, cleanupFailure,
            peerSocketsAndListenerDisposed = true,
            passed = probeFailure == null && completed && elapsed <= 2000 && stopAck &&
                succeededBeforePeerClosure && stoppedEvent &&
                (action == "before-accept" || (pressure && !pendingAck && sessionClosed))
        });
    }
}
