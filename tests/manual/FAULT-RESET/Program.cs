using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using Amr.ControlGui;
using Amr.Core;
using Amr.Infrastructure;
using Amr.Simulator.Configuration;
using Amr.Simulator.Telemetry;
using MQTTnet;

internal static class Program
{
    private static AmrChange? _latest;
    private static readonly List<object> Results = [];
    private static readonly ConcurrentQueue<(string Kind, string Payload, bool Retain)> Wire = [];
    private static AmrChange Frame => Volatile.Read(ref _latest) ?? throw new InvalidOperationException("No frame.");

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[0], out int port) || port == 1884)
            throw new ArgumentException("Specify an isolated broker port (not 1884) and a new evidence directory.");
        string evidence = Path.GetFullPath(args[1]); Directory.CreateDirectory(evidence);
        const string config = """{"amrId":"AMR_001","initialPosition":{"x":4,"y":2},"speedMps":1,"updateIntervalMs":50,"waypoints":[{"x":100,"y":2}],"faults":[{"code":"DRIVE_FAULT","level":"ERR","activateAfterMs":4000,"resolveAfterMs":8000,"message":"구동 장애 시험"}]}""";
        File.WriteAllText(Path.Combine(evidence, "simulator.config.json"), config);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Control.CheckForIllegalCrossThreadCalls = true;
        using var messages = TextWriter.Synchronized(new StreamWriter(Path.Combine(evidence, "mqtt.jsonl")) { AutoFlush = true });
        using var simulationLog = TextWriter.Synchronized(new StreamWriter(Path.Combine(evidence, "simulator.log")) { AutoFlush = true });
        using var observer = new MqttClientFactory().CreateMqttClient();
        observer.ApplicationMessageReceivedAsync += e =>
        {
            var message = e.ApplicationMessage;
            string payload = Encoding.UTF8.GetString(message.Payload.ToArray());
            Wire.Enqueue((message.Topic.Split('/')[^1], payload, message.Retain));
            messages.WriteLine(JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, topic = message.Topic, payload, retain = message.Retain }));
            return Task.CompletedTask;
        };
        using var simulatorStop = new CancellationTokenSource();
        using var watchStop = new CancellationTokenSource();
        var provider = new MqttAmrDataProvider(new("127.0.0.1", port), destination: new("시험 목적지", 100, 2));
        using var form = new MainForm(provider, provider);
        Task<int>? simulator = null;
        var watch = Task.Run(async () =>
        {
            try { await foreach (var value in provider.WatchAsync(watchStop.Token)) Volatile.Write(ref _latest, value); }
            catch (OperationCanceledException) when (watchStop.IsCancellationRequested) { }
        });
        try
        {
            Await(observer.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", port)
                .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500).WithClientId("fault-observer-" + Guid.NewGuid().ToString("N")).Build()));
            Await(observer.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter("amr/AMR_001/#").Build()));
            form.Show(); form.ShowInTaskbar = false; form.Location = new(-20000, -20000); provider.Start();
            Wait(() => Volatile.Read(ref _latest)?.Connection?.State == DataConnectionState.Connected);
            simulator = Task.Run(() => MqttCommandSimulationSession.RunAsync(SimulatorSettingsLoader.Parse(config),
                new(new("127.0.0.1", port), 100, 250), simulationLog, simulationLog, simulatorStop.Token));
            Wait(() => Frame.Devices.FirstOrDefault() is { State: AmrState.IDLE, Liveness: LivenessObservation.Alive }
                && Find<Button>(form, "openSimulation").Enabled);
            Find<Button>(form, "openSimulation").PerformClick();
            var child = form.OwnedForms.OfType<SimulationForm>().Single();
            child.ShowInTaskbar = false; child.Location = new(-20000, -20000); // 자동 시험 중 사용자의 기존 화면 조작과 격리
            Find<Button>(child, "simulationStart").PerformClick();
            Wait(() => Frame.Devices[0].State == AmrState.BUSY && Frame.Commands.Any(x => x.Kind == CommandKind.MOVE));
            string moveId = Frame.Commands.Last(x => x.Kind == CommandKind.MOVE).CommandId;
            Check("FR-01", Frame.Commands.Single(x => x.CommandId == moveId).Result == CommandResult.Unconfirmed,
                "GUI Start -> real MQTT MOVE -> BUSY; no premature success.");
            Publish(observer, new { cmdId = "BUSY-RESET", type = "RESET", ts = 1, param = new { } });
            Wait(() => Ack("BUSY-RESET", "FAILURE", "BUSY_RESET_REJECTED"));
            Check("FR-02", Frame.Devices[0].State == AmrState.BUSY, "Direct BUSY RESET fails without interrupting MOVE.");
            Wait(() => Frame.Devices[0].State == AmrState.FAULT && Frame.Alarms.Any(x => x.IsActive)
                && Frame.Commands.Single(x => x.CommandId == moveId).Result == CommandResult.Failure
                && Find<DataGridView>(form, "alarmGrid").Rows.Count == 1);
            var stopped = Frame.Devices[0];
            Check("FR-03", stopped.SpeedMps == 0 && stopped.X > 4 && stopped.X < 100 && stopped.Y == 2
                && Frame.Commands.Single(x => x.CommandId == moveId).Reason == "FAULT_INTERRUPTED"
                && Find<Label>(form, "detailsLabel").Text.Contains("FAULT · 장애")
                && !Find<Button>(child, "simulationStart").Enabled && Find<Label>(child, "simulationState").ForeColor == Color.Firebrick,
                "Scheduled fault stops actual movement; MOVE fails; GUI is red, alarm visible, Start disabled.");
            Capture(form, evidence, "01-fault-dashboard.png");
            Find<Button>(form, "faultReset").PerformClick();
            Wait(() => Frame.Commands.Any(x => x.Kind == CommandKind.RESET && x.Result == CommandResult.Failure)
                && Find<Label>(child, "simulationReason").Text.Contains("아직 해소"));
            var early = Frame.Commands.Last(x => x.Kind == CommandKind.RESET);
            Check("FR-04", Frame.Commands.Count(x => x.Kind == CommandKind.RESET) == 1 && early.Reason == "FAULT_CAUSE_ACTIVE" && Frame.Devices[0].State == AmrState.FAULT && Frame.Alarms.Single().IsActive,
                "MainForm RESET reaches simulator; unresolved cause returns matching FAILURE and keeps FAULT/ACTIVE.");
            Capture(child, evidence, "02-reset-rejected.png");
            Wait(() => ReadLiveLog(Path.Combine(evidence, "simulator.log")).Contains("FaultCauseResolved"));
            Pump(350);
            Check("FR-05", Frame.Devices[0].State == AmrState.FAULT && Frame.Alarms.Single().IsActive
                && Frame.Devices[0].X == stopped.X && Frame.Devices[0].Y == stopped.Y && Frame.Devices[0].Liveness == LivenessObservation.Alive,
                "Cause resolution alone keeps latch, coordinates and Heartbeat; no automatic resume.");
            Find<Button>(child, "simulationReset").PerformClick();
            Wait(() => Frame.Commands.Any(x => x.Kind == CommandKind.RESET && x.Result == CommandResult.Success)
                && Frame.Devices[0].State == AmrState.IDLE && !Frame.Alarms.Single().IsActive
                && Find<DataGridView>(form, "alarmGrid").Rows.Count == 0 && Find<Button>(child, "simulationStart").Enabled);
            var recovery = Frame.Commands.Last(x => x.Kind == CommandKind.RESET);
            Check("FR-06", Frame.Commands.Count(x => x.Kind == CommandKind.RESET) == 2 && recovery.CommandId != early.CommandId && recovery.Reason == "RESET_COMPLETED"
                && Frame.Devices[0].X == stopped.X && Frame.Devices[0].Y == stopped.Y && Frame.Devices[0].SpeedMps == 0
                && !Find<Label>(form, "detailsLabel").Text.Contains("FAULT · 장애"),
                "New RESET -> CLEARED, unchanged coordinates, IDLE, matching SUCCESS; GUI Start restored.");
            Capture(child, evidence, "03-reset-recovered.png");
            Publish(observer, new { cmdId = recovery.CommandId, type = "RESET", ts = 2, param = new { } });
            Wait(() => Wire.Count(x => x.Kind == "ack" && Property(x.Payload, "cmdId") == recovery.CommandId) == 2);
            Check("FR-07", Wire.Count(x => x.Kind == "alarm" && Property(x.Payload, "alarmState") == "CLEARED") == 1
                && !Frame.Commands.Single(x => x.CommandId == recovery.CommandId).HasConflictingResult,
                "Duplicate RESET replays cached final ACK without repeating CLEARED or changing state.");
            Await(provider.SetDestinationAsync("AMR_001", new("복구 후 목적지", stopped.X + 1, stopped.Y)));
            Pump(100); Find<Button>(child, "simulationStart").PerformClick();
            Wait(() => Frame.Commands.Count(x => x.Kind == CommandKind.MOVE) == 2 && Frame.Commands.Last(x => x.Kind == CommandKind.MOVE).Result == CommandResult.Success);
            Check("FR-08", Frame.Devices[0].X == stopped.X + 1 && Frame.Devices[0].Y == stopped.Y,
                "Explicit new GUI MOVE starts from preserved fault position and arrives normally.");
            Check("FR-09", Frame.Connection!.RejectedMessages == 0 && Wire.All(x => !x.Retain)
                && Wire.Any(x => x.Kind == "heartbeat") && Wire.Any(x => x.Kind == "alarm"),
                "Real Telemetry/Heartbeat/Alarm/ACK pass existing receiver validators; no retained messages.");
            simulatorStop.Cancel(); Await(simulator);
            Check("FR-10", simulator.Result == 0, "Cancellation awaits simulator unsubscribe/disconnect and exits successfully.");
            File.WriteAllText(Path.Combine(evidence, "final-snapshot.json"), JsonSerializer.Serialize(Frame, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error) { Results.Add(new { id = "ERROR", passed = false, detail = error.ToString() }); Console.Error.WriteLine(error); return 1; }
        finally
        {
            simulatorStop.Cancel(); if (simulator is not null) Await(simulator);
            if (!form.IsDisposed) { form.Close(); Wait(() => form.IsDisposed); }
            watchStop.Cancel(); Await(watch); Await(provider.DisposeAsync().AsTask());
            if (observer.IsConnected) Await(observer.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build()));
            File.WriteAllText(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new { executedAt = DateTimeOffset.Now,
                runtime = Environment.Version.ToString(), os = Environment.OSVersion.ToString(), port, results = Results }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static string? Property(string json, string name) { using var doc = JsonDocument.Parse(json); return doc.RootElement.GetProperty(name).GetString(); }
    private static string ReadLiveLog(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(file);
        return reader.ReadToEnd();
    }
    private static bool Ack(string id, string result, string reason) => Wire.Any(x => x.Kind == "ack" && Property(x.Payload, "cmdId") == id && Property(x.Payload, "result") == result && Property(x.Payload, "reason") == reason);
    private static void Publish(IMqttClient client, object data) => Await(client.PublishAsync(new MqttApplicationMessageBuilder()
        .WithTopic("amr/AMR_001/command").WithPayload(JsonSerializer.SerializeToUtf8Bytes(data)).WithRetainFlag(false).Build()));
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static void Wait(Func<bool> predicate)
    {
        var clock = Stopwatch.StartNew();
        while (!predicate()) { if (clock.Elapsed.TotalSeconds > 15) throw new TimeoutException(JsonSerializer.Serialize(Volatile.Read(ref _latest))); Application.DoEvents(); Thread.Sleep(10); }
        Application.DoEvents();
    }
    private static void Await(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Pump(int ms) { var clock = Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); } }
    private static void Check(string id, bool passed, string detail) { Results.Add(new { id, passed, detail }); Console.WriteLine($"{id} {(passed ? "PASS" : "FAIL")} {detail}"); if (!passed) throw new InvalidOperationException(id); }
    private static void Capture(Form form, string evidence, string name) { Application.DoEvents(); using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(evidence, name), ImageFormat.Png); }
}
