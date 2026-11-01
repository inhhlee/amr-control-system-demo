using System.Collections.Concurrent;
using Amr.Communication.Command;
using Amr.Communication.ConsoleApp;
using Amr.Communication.ConsoleApp.Logging;
using Amr.Communication.Models;
using Amr.Communication.Mqtt;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class TrialEventLoggerTests
{
    [Fact]
    public void PeriodicDataGoesToDebugWhileConsoleKeepsOnlyFirstAndStateChanges()
    {
        var capture = new Capture(); using var logger = Logger(capture); var output = new StringWriter();
        using var events = new TrialEventLogger(new(output, false), TrialConsoleOptions.Parse(["--trial"]), new(logger, "trial-split"));
        var session = Guid.NewGuid();
        for (int i=0;i<20;i++) events.Observe(Telemetry(i, "IDLE"),session);
        events.Observe(Telemetry(20,"BUSY"),session);
        events.Observe(new("Received",new ReceivedMessage("AMR_001","heartbeat",new HeartbeatMessage("AMR_001",100,"OK",DateTimeOffset.UtcNow),DateTimeOffset.UtcNow)),session);
        Assert.Equal(2,output.ToString().Split("종류=telemetry").Length-1);
        Assert.DoesNotContain("종류=heartbeat",output.ToString());
        Assert.Equal(21,capture.OfType("TelemetryReceived").Count());
        Assert.All(capture.OfType("TelemetryReceived"), e=>Assert.Equal(LogEventLevel.Debug,e.Level));
        var detail=capture.OfType("TelemetryReceived").Last();
        Assert.Equal(20d,Value(detail,"X")); Assert.Equal("BUSY",Value(detail,"State"));
        Assert.Equal("trial-split",Value(detail,"RunId"));Assert.Equal(session.ToString("N"),Value(detail,"SessionId"));
        Assert.Equal("amr/AMR_001/telemetry",Value(detail,"Topic"));Assert.Equal(100L,Value(detail,"DeviceTimestamp"));
        Assert.All(capture.Events,e=>Assert.DoesNotContain(e.Properties.Keys,k=>k is "ConsoleKind" or "ConsolePreview" or "ConsolePreviewTruncated"));
    }

    [Fact]
    public void FirstReceiptAndStateAreTrackedSeparatelyForEachTopicDevice()
    {
        var capture=new Capture();using var logger=Logger(capture);var output=new StringWriter();
        using var events=new TrialEventLogger(new(output,false),TrialConsoleOptions.Parse(["--trial"]),new(logger));
        events.Observe(Telemetry(1,"IDLE"),null);
        events.Observe(new("Received",new ReceivedMessage("AMR_002","telemetry",new TelemetryMessage("AMR_002",100,new(2,0),0,"IDLE",DateTimeOffset.UtcNow),DateTimeOffset.UtcNow)),null);
        events.Observe(Telemetry(3,"IDLE"),null);
        Assert.Equal(new[]{"AMR_001","AMR_002"},capture.OfType("TelemetryResumed").Select(e=>Value(e,"AmrId")).ToArray());
        Assert.Equal(2,output.ToString().Split("종류=telemetry").Length-1);
    }

    [Fact]
    public void DetailSwitchChangesOnlyConsoleAndDoesNotStopSeqData()
    {
        var capture=new Capture();using var logger=Logger(capture);var output=new StringWriter();
        using var events=new TrialEventLogger(new(output,false),TrialConsoleOptions.Parse(["--trial"]),new(logger));
        events.Observe(Telemetry(1,"IDLE"),null);events.Observe(Telemetry(2,"IDLE"),null);
        events.SetConsoleDetail(true);events.Observe(Telemetry(3,"IDLE"),null);
        events.SetConsoleDetail(false);events.Observe(Telemetry(4,"IDLE"),null);
        Assert.Equal(4,capture.OfType("TelemetryReceived").Count());
        Assert.Equal(2,output.ToString().Split("종류=telemetry").Length-1);
        Assert.Contains("위치=(3,0)",output.ToString());Assert.DoesNotContain("위치=(4,0)",output.ToString());
    }

    [Fact]
    public void ReconnectReportsFreshReceiptEvenWhenStateDidNotChange()
    {
        var capture=new Capture();using var logger=Logger(capture);var output=new StringWriter();
        using var events=new TrialEventLogger(new(output,false),TrialConsoleOptions.Parse(["--trial"]),new(logger));
        var first=Guid.NewGuid();var second=Guid.NewGuid();
        events.Observe(Telemetry(1,"IDLE"),first);events.Observe(new("Failed","BrokerDisconnected"),null);
        events.Observe(new("Connecting"),null);events.Observe(new("Reconnected"),second);
        events.Observe(Telemetry(2,"IDLE"),second);
        Assert.Equal(2,capture.OfType("TelemetryResumed").Count());
        Assert.Equal(second.ToString("N"),Value(capture.OfType("TelemetryResumed").Last(),"SessionId"));
        Assert.Contains("[재접속완료]",output.ToString());Assert.Equal(2,output.ToString().Split("종류=telemetry").Length-1);
    }

    [Fact]
    public void ConsoleOnlyPreviewIsNeverForwardedToSeq()
    {
        var capture=new Capture();using var logger=Logger(capture);var output=new StringWriter();
        using var events=new TrialEventLogger(new(output,false),TrialConsoleOptions.Parse(["--trial","--console-detail","--show-payload"]),new(logger));
        events.Observe(new("MessageDiagnostic",new MessageDiagnostic(DateTimeOffset.UtcNow,"amr/AMR_001/telemetry",50,"{\"TEST_SENTINEL\":1}",false,true)),null);
        Assert.Contains("TEST_SENTINEL",output.ToString());
        var entry=Assert.Single(capture.OfType("MessageDiagnostic"));Assert.Equal(LogEventLevel.Debug,entry.Level);
        Assert.DoesNotContain("TEST_SENTINEL",entry.RenderMessage()+string.Join(",",entry.Properties));
        Assert.All(new[]{"Payload","PayloadPreview","Model","Exception","ConsolePreview"},key=>Assert.False(entry.Properties.ContainsKey(key)));
    }

    [Fact]
    public void CommandPublicationAndAckPreserveRequestIdentityWithoutClaimingBusinessCompletion()
    {
        var capture=new Capture();using var logger=Logger(capture);var output=new StringWriter();
        using var events=new TrialEventLogger(new(output,false),TrialConsoleOptions.Parse(["--trial"]),new(logger));
        var command=new CommandMessage("CMD-TEST","MOVE",100,new(3,2));
        events.Command("CommandRequested","발행요청",command,"요청 작성");
        events.Command("CommandPublished","명령발행",command,"MQTT 발행 반환, AMR 수행 미확인");
        var ack=new AckMessage("CMD-TEST","FAILURE",200,null,"BUSY_MOVE_REJECTED",DateTimeOffset.UtcNow);
        events.Observe(new("Received",new ReceivedMessage("AMR_001","ack",ack,DateTimeOffset.UtcNow)),null);
        var received=Assert.Single(capture.OfType("AckReceived"));Assert.Equal(LogEventLevel.Warning,received.Level);
        Assert.Equal("CMD-TEST",Value(received,"CommandId"));Assert.Equal("AMR_001",Value(received,"AmrId"));
        Assert.Null(Value(received,"AckAmrId"));Assert.Equal("FAILURE",Value(received,"Result"));
        Assert.Contains("AMR 수행 미확인",output.ToString());Assert.Contains("result=FAILURE",output.ToString());
    }

    private static Logger Logger(Capture sink)=>new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
    private static object? Value(LogEvent entry,string key)=>((ScalarValue)entry.Properties[key]).Value;
    private static CommunicationSessionEvent Telemetry(double x,string state)=>new("Received",new ReceivedMessage("AMR_001","telemetry",
        new TelemetryMessage("AMR_001",100,new(x,0),state=="BUSY"?1:0,state,DateTimeOffset.UtcNow),DateTimeOffset.UtcNow));
    private sealed class Capture:ILogEventSink
    {
        internal ConcurrentQueue<LogEvent> Events {get;}=new();
        public void Emit(LogEvent entry)=>Events.Enqueue(entry);
        internal IEnumerable<LogEvent> OfType(string type)=>Events.Where(e=>Equals(Value(e,"EventType"),type));
    }
}
