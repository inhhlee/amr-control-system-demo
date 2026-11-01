using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text.Json;
using Amr.Communication.Mqtt;
using Amr.ControlGui;
using Amr.Core;
using Amr.Infrastructure;
using MQTTnet;

internal static class Program
{
    private static AmrChange? _latest;
    private static readonly List<object> Results = [];

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 3 || !int.TryParse(args[0], out int port) || !args[1].StartsWith("amr-gui-live-", StringComparison.Ordinal))
            throw new ArgumentException("Usage: LiveProbe <isolated port> <owned amr-gui-live-* container> <evidence directory>");
        string evidence = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(evidence);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var log = TextWriter.Synchronized(new StreamWriter(Path.Combine(evidence, "mqtt-events.jsonl")) { AutoFlush = true });
        var provider = new MqttAmrDataProvider(new("127.0.0.1", port), reconnect: new() { Delay = TimeSpan.FromSeconds(1), MaxAttempts = 20 },
            report: entry => log.WriteLine(JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, entry.Name, entry.Value })));
        using var form = new MainForm(provider, null);
        using var watchStop = new CancellationTokenSource();
        Task watch = Task.Run(async () =>
        {
            try { await foreach (var frame in provider.WatchAsync(watchStop.Token)) Volatile.Write(ref _latest, frame); }
            catch (OperationCanceledException) when (watchStop.IsCancellationRequested) { }
        });
        try
        {
            form.Show(); provider.Start();
            Wait(() => Frame?.Connection?.State == DataConnectionState.Connected && Label(form, "communicationValue").Contains("서버 연결됨"));
            Check("LIVE-01", Frame!.Devices.IsEmpty, "Broker connected before Telemetry; no invented AMR coordinates.");
            var tabs = (TabControl)form.Controls.Find("feedbackTabs", true).Single();
            tabs.SelectedIndex = 1;
            using (var simulator = StartSimulator(port, evidence, "simulator-before"))
            {
                Wait(() => Frame?.Devices.FirstOrDefault() is { State: AmrState.BUSY } && Label(form, "detailsLabel").Contains("이동"));
                Check("LIVE-02", Frame!.Connection!.TelemetryIsCurrent, "Actual Simulator process sent BUSY coordinates through Mosquitto into GUI.");
                Capture(form, evidence, "01-moving.png");
                Wait(() => IsFinal() && Label(form, "detailsLabel").Contains("X 1 m / Y 2 m"));
                Wait(() => simulator.HasExited);
                Check("LIVE-03", simulator.ExitCode == 0, "Simulator final=(1,2), speed=0, IDLE; actual GUI received final position.");
            }
            Wait(() => Frame!.Devices[0].Liveness == LivenessObservation.Unresponsive && Label(form, "detailsLabel").Contains("응답 없음"));
            Check("LIVE-04", Frame!.Connection!.State == DataConnectionState.Connected,
                "After Simulator exit, Heartbeat timeout marks AMR unresponsive while Broker remains connected.");
            using (var publisher = new MqttClientFactory().CreateMqttClient())
            {
                Await(publisher.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", port).Build()));
                Await(publisher.PublishAsync(new MqttApplicationMessageBuilder().WithTopic("amr/AMR_001/telemetry")
                    .WithPayload("""{"id":"AMR_001","ts":1,"pos":{"x":999,"y":999},"sp":-1,"state":"BUSY"}""").Build()));
                Await(publisher.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build()));
            }
            Wait(() => Frame!.Connection!.RejectedMessages == 1);
            Check("LIVE-05", IsFinal(), "Invalid negative-speed Telemetry rejected; last valid coordinates unchanged.");
            Docker("stop", "-t", "1", args[1]);
            Wait(() => Frame!.Connection!.State == DataConnectionState.ReconnectWaiting && Label(form, "communicationValue").Contains("재접속 대기"));
            Check("LIVE-06", IsFinal() && !Frame!.Connection!.TelemetryIsCurrent && Frame.Devices[0].Liveness == LivenessObservation.Unknown,
                "Broker loss displayed; final coordinates/state preserved as last received, liveness unknown.");
            Capture(form, evidence, "02-disconnected.png");
            Docker("start", args[1]);
            Wait(() => Frame!.Connection!.State == DataConnectionState.Connected && Label(form, "communicationValue").Contains("서버 연결됨"));
            Check("LIVE-07", !Frame!.Connection!.TelemetryIsCurrent && Label(form, "mapSource").Contains("마지막 수신값"),
                "Automatic reconnection/subscription succeeded; old coordinates remain stale until new Telemetry.");
            Capture(form, evidence, "03-reconnected-awaiting-data.png");
            using (var simulator = StartSimulator(port, evidence, "simulator-after"))
            {
                Wait(() => Frame!.Connection!.TelemetryIsCurrent && Frame.Devices[0].State == AmrState.BUSY &&
                    Label(form, "mapSource").Contains("현재 연결에서 수신"));
                Check("LIVE-08", Frame!.Devices[0].LastTelemetryReceivedAt is not null,
                    "Restarted actual Simulator publishes a new run; same GUI resumes receiving without restart.");
                Wait(() => IsFinal() && Label(form, "detailsLabel").Contains("X 1 m / Y 2 m"));
                Wait(() => simulator.HasExited);
                Check("LIVE-09", simulator.ExitCode == 0 && Frame!.Devices[0].Liveness == LivenessObservation.Alive,
                    "After reconnection, final position/state and Heartbeat received in same GUI.");
                Capture(form, evidence, "04-received-after-reconnect.png");
            }
            Docker("stop", "-t", "1", args[1]);
            Wait(() => Frame!.Connection!.State == DataConnectionState.ReconnectWaiting);
            form.Close(); Wait(() => form.IsDisposed);
            watchStop.Cancel(); Await(watch); Await(provider.DisposeAsync().AsTask());
            Check("LIVE-10", watch.IsCompleted, "GUI closed during reconnect waiting; subscriptions and owned session joined.");
            return 0;
        }
        catch (Exception error)
        {
            Results.Add(new { id = "ERROR", passed = false, detail = error.ToString() });
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            if (!form.IsDisposed) { form.Close(); Wait(() => form.IsDisposed); }
            watchStop.Cancel(); Await(watch); Await(provider.DisposeAsync().AsTask());
            File.WriteAllText(Path.Combine(evidence, "live-results.json"), JsonSerializer.Serialize(new
            {
                executedAt = DateTimeOffset.Now, environment = Environment.OSVersion.ToString(), runtime = Environment.Version.ToString(),
                broker = $"127.0.0.1:{port}", container = args[1], results = Results
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static AmrChange? Frame => Volatile.Read(ref _latest);
    private static bool IsFinal() => Frame?.Devices.FirstOrDefault() is { X: 1, Y: 2, SpeedMps: 0, State: AmrState.IDLE };
    private static string Label(Form form, string name) => form.Controls.Find(name, true).Single().Text;
    private static void Wait(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException($"GUI wait exceeded 15s; frame={JsonSerializer.Serialize(Frame)}");
            Application.DoEvents(); Thread.Sleep(10);
        }
        Application.DoEvents();
    }
    private static void Await(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Check(string id, bool success, string detail)
    {
        Results.Add(new { id, passed = success, detail });
        Console.WriteLine($"{id} {(success ? "PASS" : "FAIL")} {detail}");
        if (!success) throw new InvalidOperationException(id + ": " + detail);
    }
    private static void Capture(Form form, string evidence, string name)
    {
        Application.DoEvents();
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
        image.Save(Path.Combine(evidence, name), ImageFormat.Png);
    }
    private static SimulatorProcess StartSimulator(int port, string evidence, string name)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "src/Amr.Simulator/bin/Debug/net10.0/Amr.Simulator.dll", "--config", "config/simulator.single.json",
            "--mqtt", "--host", "127.0.0.1", "--port", port.ToString(CultureInfo.InvariantCulture), "--telemetry-ms", "200", "--heartbeat-ms", "1000" }) info.ArgumentList.Add(argument);
        return new(Process.Start(info)!, Path.Combine(evidence, name + ".log"));
    }
    private sealed class SimulatorProcess : IDisposable
    {
        private readonly Process _process;
        private readonly string _log;
        private readonly Task<string> _stdout, _stderr;
        internal SimulatorProcess(Process process, string log)
        {
            _process = process; _log = log;
            // 즉시 파이프를 비우고, 종료 때 로그 저장까지 기다린다.
            _stdout = process.StandardOutput.ReadToEndAsync(); _stderr = process.StandardError.ReadToEndAsync();
        }
        internal bool HasExited => _process.HasExited;
        internal int ExitCode => _process.ExitCode;
        public void Dispose()
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true); // 이 시험에서 시작한 자식만 정리한다.
            Await(_process.WaitForExitAsync());
            File.WriteAllText(_log, _stdout.GetAwaiter().GetResult() + _stderr.GetAwaiter().GetResult());
            _process.Dispose();
        }
    }
    private static void Docker(params string[] args)
    {
        var info = new ProcessStartInfo("docker") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in args) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        Await(process.WaitForExitAsync());
        if (process.ExitCode != 0) throw new InvalidOperationException(output.Result + error.Result);
    }
}
