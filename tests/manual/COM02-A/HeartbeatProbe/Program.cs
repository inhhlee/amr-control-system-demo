using System.Net;
using System.Text.Json;
using Amr.Communication.Mqtt;
using Amr.Simulator.Map.Control;
using Amr.Simulator.Navigation;
using Amr.Simulator.Telemetry;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

// 시험 전용 송신 장애 주입. 제품 수신·검사·감시·GUI 코드는 그대로 사용한다.
if (args.Length != 2) throw new ArgumentException("HeartbeatProbe <broker-port> <evidence-directory>");
int port = int.Parse(args[0]);
string root = Path.GetFullPath(args[1]);
Directory.CreateDirectory(root);
var mqtt = new SimulatorMqttOptions(new MqttConnectionSettings("127.0.0.1", port), 100, 200);
var gate = new SignalGate();
await using var session = new GridMapSession(root, mqtt,
    navigator: new GridNavigator(new(0, 0), [new(2, 3), new(4, 3), new(4, 5)]),
    amrId: "HB_ONE", stepIntervalMs: 100,
    transportFactory: () => new GatedTransport(new MqttSimulationTransport(mqtt.Connection), gate));
session.Connect();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
var app = builder.Build();
app.Use(async (context, next) => {
    if(context.Request.Host.Host != "127.0.0.1" || context.Connection.RemoteIpAddress is not {} ip || !IPAddress.IsLoopback(ip))
    { context.Response.StatusCode=403; return; }
    if(context.Request.Method == "POST" && context.Request.Headers["X-Simulator-Map"] != "1")
    { context.Response.StatusCode=403; return; }
    context.Response.Headers.CacheControl="no-store";
    await next(context);
});
app.MapGet("/api/grid/state", () => Results.Json(session.Read()));
app.MapGet("/api/state", () => Results.Json(session.Read()));
app.MapPost("/api/grid/start", () => session.Start() ? Results.Accepted() : Results.Conflict());
app.MapPost("/api/grid/cancel", () => { session.Cancel(); return Results.Accepted(); });
app.MapPost("/api/grid/reset", () => { session.Reset(); return Results.Accepted(); });
app.MapPost("/api/grid/obstacle", (Obstacle request) => {
    session.SetObstacle(new(request.Row, request.Col), request.Blocked); return Results.Ok();
});
app.MapPost("/verify/signal", (Signal request) => {
    if(request.Mode is not ("all" or "heartbeat" or "none")) return Results.BadRequest();
    gate.Mode=request.Mode; return Results.Ok(new { gate.Mode, gate.Suppressed });
});
app.MapGet("/verify/signal", () => new { gate.Mode, gate.Suppressed });
app.MapPost("/verify/shutdown", () => { app.Lifetime.StopApplication(); return Results.Accepted(); });
await app.StartAsync();
string url=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
await File.WriteAllTextAsync(Path.Combine(root,"probe-session.json"), JsonSerializer.Serialize(new {
    url,processId=Environment.ProcessId,amrId=session.AmrId,brokerPort=port,telemetryMs=100,heartbeatMs=200,
    timeoutMs=5000,checkIntervalMs=500,evidenceDirectory=root
},new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));
Console.WriteLine($"ProbeReady Url={url}");
await app.WaitForShutdownAsync();

sealed record Obstacle(int Row,int Col,bool Blocked);
sealed record Signal(string Mode);
sealed class SignalGate
{
    private string _mode="all";
    private long _suppressed;
    public string Mode { get=>Volatile.Read(ref _mode); set=>Volatile.Write(ref _mode,value); }
    public long Suppressed=>Interlocked.Read(ref _suppressed);
    public bool Allow(string kind)
    {
        string mode=Mode;
        if(mode=="none" || mode=="heartbeat" && kind=="Heartbeat") { Interlocked.Increment(ref _suppressed); return false; }
        return true;
    }
}
sealed class GatedTransport(ISimulationTransport inner,SignalGate gate) : ISimulationTransport
{
    public event Action? ConnectionLost { add=>inner.ConnectionLost+=value; remove=>inner.ConnectionLost-=value; }
    public Task ConnectAsync(CancellationToken token)=>inner.ConnectAsync(token);
    public Task PublishAsync(SimulationPublication message,CancellationToken token)=>gate.Allow(message.Kind)?inner.PublishAsync(message,token):Task.CompletedTask;
    public Task DisconnectAsync()=>inner.DisconnectAsync();
    public void Dispose()=>inner.Dispose();
}
