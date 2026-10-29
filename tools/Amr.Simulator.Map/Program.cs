using System.Net;
using Amr.Simulator.Map;
using Amr.Simulator.Map.Control;
using Amr.Simulator.Telemetry;
using Amr.Communication.Mqtt;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

// 경유점 순서·장애물 우회를 한 AMR의 단일 이동·MQTT 세션에서 처리한다.
string repository = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(repository, "config", "simulator.single.json")))
    repository = Directory.GetParent(repository)?.FullName ?? throw new DirectoryNotFoundException("Repository not found.");
string config = Path.Combine(repository, "config", "map-demo", "settings.json");
if (args.Contains("--fleet-config", StringComparer.Ordinal))
{
    await FleetMapHost.RunAsync(repository, args);
    return;
}
int? port = null;
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
GridMissionSettings settings;
try { settings = await GridMissionSettings.LoadAsync(config); }
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"설정 파일을 확인해 주세요: {config}\n{exception.Message}");
    Environment.ExitCode = 1;
    return;
}
port ??= settings.WebPort;
SimulatorMqttOptions? mqtt = hasMqtt ? new(new MqttConnectionSettings(mqttHost!, mqttPort!.Value), telemetryMs!.Value, heartbeatMs!.Value) : settings.Mqtt;

await using var gridSession = await GridMapSession.CreateAsync(config, Path.Combine(repository, "evidence", "AMR"), mqtt, settings);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = [],
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port.Value));
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
app.MapGet("/api/state", () => Results.Json(gridSession.Read()));
app.MapPost("/api/start", () => gridSession.Start() ? Results.Accepted() : Results.Conflict(new { message = "이미 실행 중입니다." }));
app.MapPost("/api/cancel", () => { gridSession.Cancel(); return Results.Accepted(); });
app.MapGet("/api/grid/state", () => Results.Json(gridSession.Read()));
app.MapPost("/api/grid/start", () => gridSession.Start() ? Results.Accepted() : Results.Conflict(new { message = "이미 이동 중입니다." }));
app.MapPost("/api/grid/cancel", () => { gridSession.Cancel(); return Results.Accepted(); });
app.MapPost("/api/grid/reset", () => { gridSession.Reset(); return Results.Accepted(); });
app.MapPost("/api/grid/obstacle", (GridObstacleRequest request) =>
{
    if (request.Row is null || request.Col is null || request.Blocked is null)
        return Results.BadRequest(new { message = "row·col·blocked는 필수입니다." });
    try
    {
        gridSession.SetObstacle(new(request.Row.Value, request.Col.Value), request.Blocked.Value);
        return Results.Ok(gridSession.Read());
    }
    catch (ArgumentException exception) { return Results.BadRequest(new { message = exception.Message }); }
});
app.Lifetime.ApplicationStopping.Register(gridSession.Cancel);
await app.StartAsync();
var server = app.Services.GetRequiredService<IServer>();
string url = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
Console.WriteLine($"MapReady Url={url} Evidence={gridSession.EvidenceDirectory}");
await app.WaitForShutdownAsync();

internal sealed record GridObstacleRequest(int? Row, int? Col, bool? Blocked);
