$ErrorActionPreference = 'Stop'
$reviewDirectory = $PSScriptRoot
$proxySource = Get-Content -Raw (Join-Path $PSScriptRoot '../../../tools/COM-03/ResponseGateProxy.cs')
$probe = @'
public static class ProxyStopReview
{
    static string ReadShared(string path)
    {
        using var file = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read,
            System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
        using var reader = new System.IO.StreamReader(file);
        return reader.ReadToEnd();
    }
    static void Command(string directory, string value)
    {
        string path = System.IO.Path.Combine(directory, "proxy.control");
        System.IO.File.WriteAllText(path + ".tmp", value);
        System.IO.File.Move(path + ".tmp", path, true);
    }
    public static async System.Threading.Tasks.Task<string> RunAsync(string directory)
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var client = new System.Net.Sockets.TcpClient { ReceiveBufferSize = 1024 };
        System.Net.Sockets.TcpClient upstream = null;
        using var floodStop = new System.Threading.CancellationTokenSource();
        System.Threading.Tasks.Task proxy = null, flood = null;
        bool backpressure = false, blockAck = false, stopAck = false, stopped = false;
        long sent = 0;
        string cleanupFailure = null;
        try
        {
            int brokerPort = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            proxy = ResponseGateProxy.RunAsync(brokerPort, directory);
            string readyPath = System.IO.Path.Combine(directory, "proxy.ready.json");
            using var ready = System.Text.Json.JsonDocument.Parse(ReadShared(readyPath));
            int port = ready.RootElement.GetProperty("port").GetInt32();
            await client.ConnectAsync(System.Net.IPAddress.Loopback, port);
            upstream = await listener.AcceptTcpClientAsync();
            async System.Threading.Tasks.Task Flood()
            {
                byte[] bytes = new byte[65536];
                for (int n = 0; n < 1024; n++)
                {
                    await upstream.GetStream().WriteAsync(bytes, floodStop.Token);
                    System.Threading.Interlocked.Add(ref sent, bytes.Length);
                }
            }
            flood = Flood(); // The connected client deliberately never reads responses.
            string logPath = System.IO.Path.Combine(directory, "proxy.jsonl");
            string previous = "";
            for (int n = 0; n < 30; n++)
            {
                await System.Threading.Tasks.Task.Delay(200);
                string current = ReadShared(logPath);
                if (current.Contains("Forwarded") && current == previous && !flood.IsCompleted)
                { backpressure = true; break; }
                previous = current;
            }
            if (!backpressure) throw new System.InvalidOperationException("Did not establish blocked response write");
            Command(directory, "1 block");
            await System.Threading.Tasks.Task.Delay(500);
            blockAck = ReadShared(logPath).Contains("ControlApplied");
            Command(directory, "2 stop");
            stopped = await System.Threading.Tasks.Task.WhenAny(proxy,
                System.Threading.Tasks.Task.Delay(2000)) == proxy;
            string finalLog = ReadShared(logPath);
            stopAck = finalLog.Contains("\"action\":\"stop\"");
        }
        finally
        {
            floodStop.Cancel();
            client.Dispose();
            upstream?.Dispose();
            listener.Stop();
            if (flood != null) try { await flood.WaitAsync(System.TimeSpan.FromSeconds(3)); } catch { }
            if (proxy != null) try { await proxy.WaitAsync(System.TimeSpan.FromSeconds(3)); }
                catch (System.Exception e) { cleanupFailure = e.GetType().Name; }
        }
        return System.Text.Json.JsonSerializer.Serialize(new {
            head = "da1e64083f73e12fc5ec992ba3dab03bddcf6679",
            backpressureEstablished = backpressure,
            bytesSentByFakeBroker = sent,
            blockAcknowledgedBeforeStop = blockAck,
            stopAcknowledgedWithin2Seconds = stopAck,
            proxyCompletedWithin2SecondsOfStop = stopped,
            peerSocketsAndListenerDisposed = true,
            forcedPeerClosureException = cleanupFailure,
            verdict = stopped && stopAck ? "expected-stop-observed" : "stop-stuck-behind-block"
        });
    }
}
'@
Add-Type -TypeDefinition ($proxySource + "`n" + $probe)
[ProxyStopReview]::RunAsync($reviewDirectory).GetAwaiter().GetResult() |
    Set-Content -LiteralPath (Join-Path $reviewDirectory 'proxy-stop-reproduction.json') -Encoding utf8
Get-Content -Raw (Join-Path $reviewDirectory 'proxy-stop-reproduction.json')
