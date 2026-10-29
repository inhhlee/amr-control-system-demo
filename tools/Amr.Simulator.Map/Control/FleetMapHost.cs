using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Amr.Simulator.Map.Control;

internal static class FleetMapHost
{
    public static async Task RunAsync(string repository, string[] args)
    {
        string? config = null;
        int? port = null;
        var seen = new HashSet<string>();
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !seen.Add(args[i])) throw new ArgumentException("Unique key/value options required.");
            if (args[i] == "--fleet-config") config = Path.GetFullPath(args[i + 1], repository);
            else if (args[i] == "--port" && int.TryParse(args[i + 1], out int value) && value is >= 0 and <= 65535) port = value;
            else throw new ArgumentException("Use --fleet-config <path> [--port <0..65535>].");
        }
        if (config is null) throw new ArgumentException("Fleet config is required.");
        FleetMissionSettings settings = await FleetMissionSettings.LoadAsync(config);
        string evidence = Path.Combine(repository, "evidence", "AMR", $"fleet-live-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        await using var session = new FleetMapSession(settings, evidence);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot") });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port ?? settings.WebPort));
        var app = builder.Build();
        app.Use(async (context, next) => {
            if (context.Request.Host.Host != "127.0.0.1" || context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
            { context.Response.StatusCode = 403; return; }
            if (context.Request.Method == "POST" && context.Request.Headers["X-Simulator-Map"] != "1")
            { context.Response.StatusCode = 403; return; }
            context.Response.Headers.CacheControl = "no-store";
            await next(context);
        });
        // 기존 웹 지도는 단일 AMR용이다. Fleet의 정식 화면은 같은 API를 사용하는 Windows Forms다.
        app.MapGet("/", () => Results.Text("AMR Fleet: Windows Forms GUI의 --grid-url로 이 주소에 연결하세요."));
        app.MapGet("/api/state", () => Results.Json(session.Read()));
        app.MapGet("/api/grid/state", () => Results.Json(session.Read()));
        IResult Action(FleetAction request, Func<string, bool> action) => request.AmrId is null ? Results.BadRequest() :
            action(request.AmrId) ? Results.Accepted() : Results.Conflict(new { message = "AMR 상태와 미션 재개 조건을 확인하세요." });
        app.MapPost("/api/start", (FleetAction request) => Action(request, session.Start));
        app.MapPost("/api/cancel", (FleetAction request) => Action(request, session.Cancel));
        app.MapPost("/api/grid/reset", (FleetAction request) => Action(request, session.Reset));
        app.MapPost("/api/grid/obstacle", (FleetObstacle request) => {
            try { return request.AmrId is not null && session.SetObstacle(request.AmrId, new(request.Row, request.Col), request.Blocked)
                ? Results.Ok() : Results.BadRequest(); }
            catch (ArgumentException) { return Results.BadRequest(); }
        });
        session.Connect();
        await app.StartAsync();
        string url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        Console.WriteLine($"FleetReady Url={url} Evidence={session.EvidenceDirectory}");
        await app.WaitForShutdownAsync();
    }
    private sealed record FleetAction(string? AmrId);
    private sealed record FleetObstacle(string? AmrId, int Row, int Col, bool Blocked);
}
