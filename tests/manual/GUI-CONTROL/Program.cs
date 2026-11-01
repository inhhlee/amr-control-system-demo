using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using Amr.Communication.Command;
using Amr.Communication.Models;
using Amr.Communication.Mqtt;
using Amr.ControlGui;
using Amr.Core;
using Amr.Infrastructure;
using MQTTnet;

internal static class Program
{
    private static AmrChange? _latest;
    private static readonly List<object> Results = [];
    private static AmrChange Frame => Volatile.Read(ref _latest) ?? throw new InvalidOperationException("No snapshot yet.");
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 3 || !int.TryParse(args[0], out int port) || !args[1].StartsWith("amr-gui-control-", StringComparison.Ordinal))
            throw new ArgumentException("Specify isolated port, owned amr-gui-control-* container and evidence path.");
        string evidence = Path.GetFullPath(args[2]); Directory.CreateDirectory(evidence);
        string config = Path.Combine(evidence, "simulator-control.config.json");
        File.WriteAllText(config, """{"amrId":"AMR_001","initialPosition":{"x":-1,"y":0},"speedMps":1,"updateIntervalMs":50,"waypoints":[{"x":5,"y":2}]}""");
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using var log = TextWriter.Synchronized(new StreamWriter(Path.Combine(evidence, "communication.jsonl")) { AutoFlush = true });
        var receivedAcks = new ConcurrentQueue<AckMessage>();
        var provider = new MqttAmrDataProvider(new("127.0.0.1", port), reconnect: new() { Delay = TimeSpan.FromSeconds(1), MaxAttempts = 20 },
            destination: new("설정 목적지", 5, 2), report: entry =>
            {
                log.WriteLine(JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, entry.Name, entry.Value }));
                if (entry.Value is ReceivedMessage { Model: AckMessage ack }) receivedAcks.Enqueue(ack);
            });
        using var form = new MainForm(provider, provider);
        using var watchStop = new CancellationTokenSource();
        Task watch = Task.Run(async () =>
        {
            try { await foreach (var frame in provider.WatchAsync(watchStop.Token)) Volatile.Write(ref _latest, frame); }
            catch (OperationCanceledException) when (watchStop.IsCancellationRequested) { }
        });
        Child? simulator = null;
        try
        {
            form.Show(); provider.Start();
            Wait(() => Volatile.Read(ref _latest)?.Connection?.State == DataConnectionState.Connected);
            Check("CTRL-01", Frame.Devices.IsEmpty && Frame.Connection!.RegisteredAmrIds.SequenceEqual(["AMR_001"]), "Only configured actual target registered; no invented position.");
            simulator = StartSimulator(port, config, evidence, "simulator-before");
            Wait(() => Frame.Devices.FirstOrDefault() is { Liveness: LivenessObservation.Alive, State: AmrState.IDLE } && Find<Button>(form,"openSimulation").Enabled);
            Check("CTRL-02", Frame.Devices[0].X == -1 && Frame.Devices[0].Y == 0, "Actual AMR initial coordinates received from configuration.");
            Find<Button>(form, "openSimulation").PerformClick();
            var child = form.OwnedForms.OfType<SimulationForm>().Single();
            Check("CTRL-03", Find<Button>(child,"sourceSettings").Text == "설정 안내" && Find<Label>(child,"destinationCoordinates").Text.Contains("5.0"), "Actual source is configuration-based; assigned destination shown in selected AMR window.");
            bool destinationApplied = false;
            Exception? dialogError = null;
            using (var timer = new System.Windows.Forms.Timer { Interval = 20 })
            {
                timer.Tick += (_,_) =>
                {
                    var dialog = child.OwnedForms.SingleOrDefault(x=>x.Name=="destinationSelectionWindow");
                    if(dialog is null) return;
                    timer.Stop();
                    try
                    {
                        Find<TextBox>(dialog,"destinationX").Text="NaN"; Find<Button>(dialog,"applyDestination").PerformClick();
                        if(string.IsNullOrEmpty(Find<Label>(dialog,"destinationError").Text))throw new InvalidOperationException("Invalid destination accepted.");
                        Find<TextBox>(dialog,"destinationX").Text="5"; Find<TextBox>(dialog,"destinationY").Text="2";
                        Find<Button>(dialog,"applyDestination").PerformClick(); destinationApplied=true;
                    }
                    catch(Exception error){dialogError=error;dialog.Close();}
                };
                timer.Start(); Find<Button>(child,"destinationSettings").PerformClick();
            }
            if(dialogError is not null)throw dialogError;
            Wait(()=>Frame.Devices[0].Journey?.Destination.Name=="목적 좌표");
            Check("CTRL-03A",destinationApplied && Frame.Devices[0] is {X:-1,Y:0,State:AmrState.IDLE},"Visible destination dialog rejects non-finite input and applies valid actual mission coordinates without moving AMR.");
            Wait(()=>Find<Button>(child,"simulationStart").Enabled);
            Find<Button>(child,"simulationStart").PerformClick();
            Wait(() => Frame.Devices[0].State == AmrState.BUSY && Frame.Commands.Any(x=>x.Kind==CommandKind.MOVE) && Find<Button>(child,"simulationStop").Enabled);
            string firstMove = Frame.Commands.Last(x=>x.Kind==CommandKind.MOVE).CommandId;
            Check("CTRL-04", Frame.Commands.Single(x=>x.CommandId==firstMove).Result == CommandResult.Unconfirmed && !Find<Button>(child,"simulationStart").Enabled,
                "GUI MOVE published; actual BUSY seen; publication is not reported as completed.");
            Capture(child,evidence,"01-moving-window.png");
            Find<Button>(child,"simulationStop").PerformClick();
            Wait(() => Frame.Devices[0].State==AmrState.STOPPED && Frame.Commands.Any(x=>x.Kind==CommandKind.STOP && x.Result==CommandResult.Success)
                && Frame.Commands.Single(x=>x.CommandId==firstMove).Result==CommandResult.Failure && Find<Button>(child,"simulationStart").Enabled);
            var stopped = Frame.Devices[0];
            Check("CTRL-05", stopped.SpeedMps==0 && Frame.Commands.Single(x=>x.CommandId==firstMove).Reason=="STOP_INTERRUPTED", "STOP actually stopped movement; original MOVE failed, STOP has independent SUCCESS.");
            PumpFor(600);
            Check("CTRL-06", Frame.Devices[0].X==stopped.X && Frame.Devices[0].Y==stopped.Y && Frame.Devices[0].Liveness==LivenessObservation.Alive,
                "Position stays stopped while Heartbeat and Telemetry continue.");
            Capture(child,evidence,"02-stopped-window.png");
            Find<Button>(child,"simulationStart").PerformClick();
            Wait(() => Frame.Commands.Count(x=>x.Kind==CommandKind.MOVE)==2 && Frame.Devices[0].State==AmrState.BUSY);
            string resumed = Frame.Commands.Last(x=>x.Kind==CommandKind.MOVE).CommandId;
            Check("CTRL-07", resumed!=firstMove && Frame.Devices[0].Journey!.Source.X==stopped.X && Frame.Devices[0].Journey!.Source.Y==stopped.Y,
                "Manual restart creates new request ID and uses stopped current position as source.");
            child.Close(); PumpFor(350);
            Check("CTRL-08", !form.OwnedForms.OfType<SimulationForm>().Any() && (Frame.Devices[0].X!=stopped.X || Frame.Devices[0].Y!=stopped.Y),
                "Closing separate window does not stop actual AMR movement.");
            Find<Button>(form,"openSimulation").PerformClick(); child=form.OwnedForms.OfType<SimulationForm>().Single();
            Check("CTRL-09", Find<Label>(child,"simulationAmrId").Text=="AMR_001", "Reopened window remains bound to actual target.");
            Wait(() => Frame.Commands.Single(x=>x.CommandId==resumed).Result==CommandResult.Success && Frame.Devices[0] is { X:5,Y:2,SpeedMps:0,State:AmrState.IDLE }
                && Find<Label>(child,"simulationResponse").Text.Contains("실행 완료"));
            Check("CTRL-10", Frame.Devices[0].Journey!.Phase==SimulationPhase.Arrived && Math.Abs(Frame.Devices[0].Journey!.RemainingDistance ?? double.PositiveInfinity)<1e-9,
                "Actual arrival, matching final ACK and same-basis remaining distance zero confirmed.");
            Capture(form,evidence,"03-arrived-dashboard.png"); Capture(child,evidence,"04-arrived-window.png");
            using var fixture = new MqttClientFactory().CreateMqttClient();
            Await(fixture.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1",port).WithClientId("control-fixture-"+Guid.NewGuid().ToString("N")).Build()));
            Publish(fixture,"alarm",new {id="AMR_001",ts=1,code="PROBE_WARN",level="WARN",alarmState="ACTIVE",message="MQTT 시험 경고"});
            Wait(()=>Frame.Alarms.Any(x=>x.IsActive) && Find<DataGridView>(form,"alarmGrid").Rows.Count==1);
            Check("CTRL-11", Frame.Alarms.Single().Severity==AlarmSeverity.Warning, "Alarm test fixture traversed real MQTT into active GUI alarm; not Simulator fault-scenario implementation.");
            Publish(fixture,"alarm",new {id="AMR_001",ts=2,code="PROBE_WARN",level="WARN",alarmState="CLEARED",message="MQTT 시험 경고"});
            Wait(()=>!Frame.Alarms.Single().IsActive && Find<DataGridView>(form,"alarmGrid").Rows.Count==0);
            int history=Frame.History.Length;
            Find<TextBox>(form,"historyAmr").Text="NO_SUCH_AMR"; Find<Button>(form,"historySearch").PerformClick();
            Check("CTRL-12", Find<DataGridView>(form,"historyGrid").Rows.Count==0, "Actual session-history filter applied.");
            Find<Button>(form,"historyReset").PerformClick();
            Check("CTRL-13", Frame.History.Length==history && Find<DataGridView>(form,"historyGrid").Rows.Count==history,
                "History reset changes filter/selection only, preserving actual records.");
            var success=Frame.Commands.Single(x=>x.CommandId==resumed);
            Publish(fixture,"ack",new {id="AMR_001",cmdId=resumed,result="SUCCESS",ts=success.AckTimestampUnixMs,reason=success.Reason}); PumpFor(150);
            Check("CTRL-14", Frame.History.Length==history, "Duplicate final ACK does not repeat business/history transition.");
            Publish(fixture,"ack",new {id="AMR_001",cmdId=resumed,result="FAILURE",ts=3,reason="PROBE_CONFLICT"});
            Wait(()=>Frame.Commands.Single(x=>x.CommandId==resumed).HasConflictingResult);
            Check("CTRL-15", Frame.Commands.Single(x=>x.CommandId==resumed).Result==CommandResult.Success, "Conflicting ACK flagged while original SUCCESS preserved.");
            child.Close();
            Docker("stop","-t","1",args[1]);
            Wait(()=>Frame.Connection!.State==DataConnectionState.ReconnectWaiting);
            Check("CTRL-16", Frame.Connection!.RegisteredAmrIds.Length==1 && Frame.Devices.Length==1 && !Frame.Connection.TelemetryIsCurrent,
                "Disconnected real AMR remains registered with last position; current state is unknown.");
            Capture(form,evidence,"05-disconnected-dashboard.png");
            Wait(()=>simulator.HasExited); simulator.Dispose(); simulator=null;
            Docker("start",args[1]); Wait(()=>Frame.Connection!.State==DataConnectionState.Connected);
            Check("CTRL-17", !Frame.Connection!.TelemetryIsCurrent, "Reconnect alone does not mark old coordinates fresh.");
            simulator=StartSimulator(port,config,evidence,"simulator-after");
            Wait(()=>Frame.Connection!.TelemetryIsCurrent && Frame.Devices[0] is {X:-1,Y:0,Liveness:LivenessObservation.Alive}); PumpFor(200);
            Check("CTRL-18", !simulator.LogText.Contains("CommandProcessed") && Frame.Devices[0].Journey!.Phase!=SimulationPhase.Arrived && Frame.Devices[0].Journey!.Source.X==-1,
                "No automatic command replay after reconnect; old completion is not current arrival at restarted coordinates.");
            Await(fixture.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1",port).WithClientId("control-observer-"+Guid.NewGuid().ToString("N")).Build()));
            var acksAfterClose = new ConcurrentQueue<string>();
            fixture.ApplicationMessageReceivedAsync += message =>
            {
                using var json=JsonDocument.Parse(message.ApplicationMessage.Payload);
                if(json.RootElement.GetProperty("result").GetString()=="SUCCESS") acksAfterClose.Enqueue(json.RootElement.GetProperty("cmdId").GetString()!);
                return Task.CompletedTask;
            };
            Await(fixture.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter("amr/AMR_001/ack").Build()));
            Find<Button>(form,"openSimulation").PerformClick(); child=form.OwnedForms.OfType<SimulationForm>().Single();
            Find<Button>(child,"simulationStart").PerformClick();
            Wait(()=>Frame.Devices[0].State==AmrState.BUSY);
            string detached=Frame.Commands.Last(x=>x.Kind==CommandKind.MOVE).CommandId;
            form.Close(); Wait(()=>form.IsDisposed); watchStop.Cancel(); Await(watch); Await(provider.DisposeAsync().AsTask());
            Wait(()=>acksAfterClose.Contains(detached));
            Check("CTRL-19", !simulator.HasExited, "After GUI closes and its communication owner stops, independent AMR completes MOVE and sends final ACK to separate observer.");
            Await(fixture.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build()));
            return 0;
        }
        catch(Exception error) { Results.Add(new{id="ERROR",passed=false,detail=error.ToString()}); Console.Error.WriteLine(error); return 1; }
        finally
        {
            if(!form.IsDisposed){ form.Close(); Wait(()=>form.IsDisposed); }
            watchStop.Cancel(); Await(watch); Await(provider.DisposeAsync().AsTask()); simulator?.Dispose();
            File.WriteAllText(Path.Combine(evidence,"control-results.json"),JsonSerializer.Serialize(new{executedAt=DateTimeOffset.Now,
                os=Environment.OSVersion.ToString(),runtime=Environment.Version.ToString(),broker=$"127.0.0.1:{port}",results=Results},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
    private static T Find<T>(Control root,string name) where T:Control => (T)root.Controls.Find(name,true).Single();
    private static void Wait(Func<bool> test)
    {
        var watch=Stopwatch.StartNew();
        while(!test()){ if(watch.Elapsed>TimeSpan.FromSeconds(18))throw new TimeoutException(JsonSerializer.Serialize(Volatile.Read(ref _latest))); Application.DoEvents(); Thread.Sleep(10); }
        Application.DoEvents();
    }
    private static void PumpFor(int ms){var watch=Stopwatch.StartNew(); while(watch.ElapsedMilliseconds<ms){Application.DoEvents();Thread.Sleep(10);}}
    private static void Await(Task task){Wait(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
    private static void Check(string id,bool passed,string detail){Results.Add(new{id,passed,detail});Console.WriteLine($"{id} {(passed?"PASS":"FAIL")} {detail}");if(!passed)throw new InvalidOperationException(id+": "+detail);}
    private static void Capture(Form form,string evidence,string name){Application.DoEvents();using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(evidence,name),ImageFormat.Png);}
    private static void Publish(IMqttClient client,string kind,object data)=>Await(client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic("amr/AMR_001/"+kind).WithPayload(JsonSerializer.SerializeToUtf8Bytes(data)).WithRetainFlag(false).Build()));
    private static Child StartSimulator(int port,string config,string evidence,string name)
    {
        var info=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"src/Amr.Simulator/bin/Debug/net10.0/Amr.Simulator.dll","--config",config,"--command-mode","--mqtt","--host","127.0.0.1","--port",port.ToString(),"--telemetry-ms","100","--heartbeat-ms","500"})info.ArgumentList.Add(arg);
        return new(Process.Start(info)!,Path.Combine(evidence,name+".log"));
    }
    private sealed class Child:IDisposable
    {
        private readonly Process _process;private readonly TextWriter _log;private readonly Task _stdout,_stderr;private readonly ConcurrentQueue<string> _lines=[];
        internal Child(Process process,string file){_process=process;_log=TextWriter.Synchronized(new StreamWriter(file){AutoFlush=true});_stdout=Read(process.StandardOutput);_stderr=Read(process.StandardError);}
        internal bool HasExited=>_process.HasExited;internal string LogText=>string.Join("\n",_lines);
        private async Task Read(StreamReader reader){while(await reader.ReadLineAsync() is{}line){_lines.Enqueue(line);await _log.WriteLineAsync(line);}}
        public void Dispose(){if(!_process.HasExited)_process.Kill(entireProcessTree:true);Await(_process.WaitForExitAsync());Await(Task.WhenAll(_stdout,_stderr));_log.Dispose();_process.Dispose();}
    }
    private static void Docker(params string[]args)
    {
        var info=new ProcessStartInfo("docker"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in args)info.ArgumentList.Add(arg);
        using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();Await(process.WaitForExitAsync());if(process.ExitCode!=0)throw new InvalidOperationException(output.Result+error.Result);
    }
}
