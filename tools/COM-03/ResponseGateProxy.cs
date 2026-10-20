using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// Single-session test tool: opaque TCP forwarding, response discard and explicit stop.
// No MQTT parsing, reconnect, response buffering or packet logging.
public static class ResponseGateProxy
{
    public static async Task RunAsync(int brokerPort, string directory)
    {
        using var trace = new StreamWriter(new FileStream(Path.Combine(directory, "proxy.jsonl"),
            FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        var clock = Stopwatch.StartNew();
        var logLock = new object();
        void Log(string kind, object data)
        {
            lock (logLock)
                trace.WriteLine(JsonSerializer.Serialize(new {
                    atUtc = DateTimeOffset.UtcNow, elapsedMs = clock.Elapsed.TotalMilliseconds,
                    eventType = kind, data
                }));
        }

        using var stop = new CancellationTokenSource();
        using var responseGate = new SemaphoreSlim(1, 1);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        bool blocked = false;
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        async Task Pump(NetworkStream source, NetworkStream destination, bool response)
        {
            var bytes = new byte[4096];
            string direction = response ? "broker-to-client" : "client-to-broker";
            try
            {
                while (true)
                {
                    int count = await source.ReadAsync(bytes, 0, bytes.Length, stop.Token);
                    if (count == 0) { Log("Eof", new { direction }); return; }
                    if (response) await responseGate.WaitAsync(stop.Token);
                    try
                    {
                        if (response && blocked) Log("Dropped", new { direction, bytes = count });
                        else
                        {
                            await destination.WriteAsync(bytes, 0, count, stop.Token);
                            Log("Forwarded", new { direction, bytes = count });
                        }
                    }
                    finally { if (response) responseGate.Release(); }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (IOException) when (stop.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
        }

        async Task Session()
        {
            using var client = await listener.AcceptTcpClientAsync(stop.Token);
            listener.Stop(); // Exactly one session per proxy process.
            using var broker = new TcpClient(AddressFamily.InterNetwork);
            using var connectLimit = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            connectLimit.CancelAfter(TimeSpan.FromSeconds(5));
            await broker.ConnectAsync(IPAddress.Loopback, brokerPort, connectLimit.Token);
            Log("SessionOpened", new {
                clientPeer = client.Client.RemoteEndPoint.ToString(),
                proxyLocal = client.Client.LocalEndPoint.ToString(),
                upstreamLocal = broker.Client.LocalEndPoint.ToString(),
                brokerPeer = broker.Client.RemoteEndPoint.ToString()
            });
            var outgoing = Pump(client.GetStream(), broker.GetStream(), false);
            var incoming = Pump(broker.GetStream(), client.GetStream(), true);
            try { await await Task.WhenAny(outgoing, incoming); }
            finally
            {
                stop.Cancel();
                client.Close();
                broker.Close();
                await Task.WhenAll(outgoing, incoming);
                Log("SessionClosed", new { });
            }
        }

        async Task Commands()
        {
            long last = 0;
            string path = Path.Combine(directory, "proxy.control");
            while (!stop.IsCancellationRequested)
            {
                if (File.Exists(path))
                {
                    // The caller atomically replaces a tiny '<sequence> <action>' file.
                    string[] fields = File.ReadAllText(path).Trim().Split(' ');
                    if (fields.Length == 2 && long.TryParse(fields[0], out long sequence) && sequence > last)
                    {
                        string action = fields[1];
                        if (action != "block" && action != "resume" && action != "stop")
                            throw new InvalidDataException("Unknown proxy action.");
                        // Stop must cancel even a socket write waiting for backpressure.
                        // Only block/resume need to serialize with response writes.
                        if (action == "stop")
                        {
                            Log("ControlApplied", new { sequence, action });
                            stop.Cancel();
                            return;
                        }
                        // Re-read the latest command if a response write is stalled.
                        // An unacknowledged block/resume must not hide a later stop.
                        if (!await responseGate.WaitAsync(25, stop.Token)) continue;
                        try
                        {
                            blocked = action == "block";
                            last = sequence;
                            Log("ControlApplied", new { sequence, action, blocked });
                        }
                        finally { responseGate.Release(); }
                    }
                }
                await Task.Delay(25, stop.Token);
            }
        }

        Log("Listening", new { port, brokerPort, processId = Environment.ProcessId });
        File.WriteAllText(Path.Combine(directory, "proxy.ready.json"),
            JsonSerializer.Serialize(new { port, brokerPort, processId = Environment.ProcessId }));
        var session = Session();
        var commands = Commands();
        try { await await Task.WhenAny(session, commands); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally
        {
            stop.Cancel();
            listener.Stop();
            try { await Task.WhenAll(session, commands); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            Log("Stopped", new { });
        }
    }
}
