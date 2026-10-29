using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Amr.Communication.Models;
using Amr.Simulator.Map.Control;
using Amr.Simulator.Telemetry;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

// 시험 전용 송신 억제/수신 처리 지연. 제품 통신 세션·Core·호스트·GUI를 그대로 사용한다.
if (args.Length != 2) throw new ArgumentException("FleetProbe <fleet-config> <evidence-directory>");
var settings = await FleetMissionSettings.LoadAsync(Path.GetFullPath(args[0]));
string root = Path.GetFullPath(args[1]);
var gates = settings.Missions.ToDictionary(x => x.AmrId, _ => new Gate());
var delay = new DeliveryGate();
await using var session = new FleetMapSession(settings, root,
    id => new GatedTransport(new MqttSimulationTransport(settings.Missions.Single(x => x.AmrId == id).Mqtt!.Connection), gates[id]), delay.BeforeAsync);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
var app = builder.Build();
var requestLogGate = new object();
app.Use(async (context, next) => {
    if (context.Request.Host.Host != "127.0.0.1" || context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
    { context.Response.StatusCode = 403; return; }
    if (context.Request.Method == "POST" && context.Request.Headers["X-Simulator-Map"] != "1")
    { context.Response.StatusCode = 403; return; }
    context.Response.Headers.CacheControl = "no-store";
    if (context.Request.Method == "POST") lock (requestLogGate)
        File.AppendAllText(Path.Combine(root, "control-requests.jsonl"), JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, path = context.Request.Path.Value }) + Environment.NewLine);
    await next(context);
});
app.MapGet("/api/state", () => Results.Json(session.Read()));
app.MapGet("/api/grid/state", () => Results.Json(session.Read()));
app.MapPost("/api/start", (Target request) => session.Start(request.AmrId) ? Results.Accepted() : (IResult)Results.Conflict());
app.MapPost("/api/cancel", (Target request) => session.Cancel(request.AmrId) ? Results.Accepted() : (IResult)Results.BadRequest());
app.MapPost("/api/grid/reset", (Target request) => session.Reset(request.AmrId) ? Results.Accepted() : (IResult)Results.BadRequest());
app.MapPost("/verify/signal", (Signal request) => {
    if (!gates.TryGetValue(request.AmrId, out var gate) || request.Mode is not ("all" or "heartbeat" or "none")) return Results.BadRequest();
    gate.Mode = request.Mode; return (IResult)Results.Ok();
});
app.MapPost("/verify/delay", (Delay request) => {
    if (!gates.ContainsKey(request.AmrId)) return Results.BadRequest();
    delay.Set(request.AmrId, request.Blocked); return (IResult)Results.Ok();
});
app.MapGet("/verify/delay", () => delay.Counts);
app.MapPost("/verify/shutdown", () => { app.Lifetime.StopApplication(); return Results.Accepted(); });
session.Connect();
await app.StartAsync();
string url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
await File.WriteAllTextAsync(Path.Combine(root, "probe-session.json"), JsonSerializer.Serialize(new {
    url, processId = Environment.ProcessId, sessionId = session.SessionId, amrIds = settings.Missions.Select(x => x.AmrId),
    brokerPort = settings.Missions[0].Mqtt!.Connection.Port, evidenceDirectory = root }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
Console.WriteLine($"FleetProbeReady Url={url}");
await app.WaitForShutdownAsync();

sealed record Target(string AmrId);
sealed record Signal(string AmrId, string Mode);
sealed record Delay(string AmrId, bool Blocked);
sealed class Gate { public volatile string Mode = "all"; }
sealed class GatedTransport(ISimulationTransport inner, Gate gate) : ISimulationTransport
{
    public event Action? ConnectionLost { add => inner.ConnectionLost += value; remove => inner.ConnectionLost -= value; }
    public Task ConnectAsync(CancellationToken token) => inner.ConnectAsync(token);
    public Task PublishAsync(SimulationPublication message, CancellationToken token) =>
        gate.Mode == "none" || gate.Mode == "heartbeat" && message.Kind == "Heartbeat" ? Task.CompletedTask : inner.PublishAsync(message, token);
    public Task DisconnectAsync() => inner.DisconnectAsync();
    public void Dispose() => inner.Dispose();
}
sealed class DeliveryGate
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _held = new();
    public ConcurrentDictionary<string, int> Counts { get; } = new();
    public void Set(string id, bool blocked)
    {
        if (blocked) _held.TryAdd(id, new(TaskCreationOptions.RunContinuationsAsynchronously));
        else if (_held.TryRemove(id, out var source)) source.TrySetResult();
    }
    public async Task BeforeAsync(ReceivedMessage message, CancellationToken token)
    {
        if (_held.TryGetValue(message.AmrId, out var source)) await source.Task.WaitAsync(token);
        Counts.AddOrUpdate(message.AmrId, 1, (_, count) => count + 1);
    }
}
