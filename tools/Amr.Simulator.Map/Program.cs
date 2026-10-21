using System.Net;
using Amr.Simulator.Map;
using Amr.Simulator.Telemetry;
using Amr.Communication.Mqtt;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

// 시연용 호스트. 실제 시뮬레이터와 같은 RunAsync를 호출하며 지도에서 이동을 계산하지 않는다.
string repository = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(repository, "config", "simulator.single.json")))
    repository = Directory.GetParent(repository)?.FullName ?? throw new DirectoryNotFoundException("Repository not found.");
string config = Path.Combine(repository, "config", "simulator.single.json");
int port = 5088;
string? mqttHost = null;
int? mqttPort = null, telemetryMs = null, heartbeatMs = null;
var seen = new HashSet<string>(StringComparer.Ordinal);
for (int i = 0; i < args.Length; i += 2)
{
    if (i + 1 >= args.Length || !seen.Add(args[i])) throw new ArgumentException("Options must be unique key/value pairs.");
    switch (args[i])
    {
        case "--config": config = Path.GetFullPath(args[i + 1], repository); break;
        case "--port" when int.TryParse(args[i + 1], out int parsed) && parsed is >= 0 and <= 65535: port = parsed; break;
        case "--mqtt-host" when !string.IsNullOrWhiteSpace(args[i + 1]): mqttHost = args[i + 1]; break;
        case "--mqtt-port" when int.TryParse(args[i + 1], out int mp) && mp is >= 1 and <= 65535: mqttPort = mp; break;
        case "--telemetry-ms" when int.TryParse(args[i + 1], out int tm) && tm > 0: telemetryMs = tm; break;
        case "--heartbeat-ms" when int.TryParse(args[i + 1], out int hm) && hm > 0: heartbeatMs = hm; break;
        default: throw new ArgumentException("Usage: --config <path> --port <0..65535>");
    }
}
bool hasMqtt = mqttHost is not null || mqttPort is not null || telemetryMs is not null || heartbeatMs is not null;
if (hasMqtt && (mqttHost is null || mqttPort is null || telemetryMs is null || heartbeatMs is null))
    throw new ArgumentException("Specify --mqtt-host, --mqtt-port, --telemetry-ms and --heartbeat-ms together.");
SimulatorMqttOptions? mqtt = hasMqtt ? new(new MqttConnectionSettings(mqttHost!, mqttPort!.Value), telemetryMs!.Value, heartbeatMs!.Value) : null;

await using var session = await MapSession.CreateAsync(config, Path.Combine(repository, "evidence", "AMR"), mqtt);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = [],
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));
var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Request.Host.Host != "127.0.0.1" ||
        context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address))
    {
        context.Response.StatusCode = 403;
        return;
    }
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self'; frame-ancestors 'none'";
    if (context.Request.Method == "POST" && context.Request.Headers["X-Simulator-Map"] != "1")
    {
        context.Response.StatusCode = 403;
        return;
    }
    await next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/state", () => Results.Json(session.Read()));
app.MapPost("/api/start", () => session.Start() ? Results.Accepted() : Results.Conflict(new { message = "이미 실행 중입니다." }));
app.MapPost("/api/cancel", () => { session.Cancel(); return Results.Accepted(); });
app.Lifetime.ApplicationStopping.Register(session.Cancel);
await app.StartAsync();
var server = app.Services.GetRequiredService<IServer>();
string url = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
Console.WriteLine($"MapReady Url={url} Evidence={session.EvidenceDirectory}");
await app.WaitForShutdownAsync();
