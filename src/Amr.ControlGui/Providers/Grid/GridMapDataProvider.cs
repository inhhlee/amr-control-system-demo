using System.Collections.Immutable;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Amr.Core;

namespace Amr.ControlGui;

// 기존 지도 호스트가 소유한 단일 MQTT 세션을 읽는다. GUI는 이동/MQTT를 재구현하지 않는다.
public sealed class GridMapDataProvider : IAmrDataProvider
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly Uri _endpoint;
    private readonly TimeSpan _pollInterval;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly CancellationToken _stopToken;
    private readonly object _gate = new();
    private GridDashboardFrame _latest = new(new(0, AmrChangeKind.InitialSnapshot, [], [], []), null, null);
    private long _revision;
    private bool _disposed, _watching;

    public GridMapDataProvider(Uri endpoint, HttpClient? http = null, TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != "http" || endpoint.Host != "127.0.0.1" ||
            endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.UserInfo.Length != 0)
            throw new ArgumentException("지도 주소는 http://127.0.0.1:<port> 형식이어야 합니다.", nameof(endpoint));
        _endpoint = endpoint;
        _stopToken = _shutdown.Token;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(200);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_pollInterval, TimeSpan.Zero);
        _ownsClient = http is null;
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(2) };
    }

    public ProviderMode Mode => ProviderMode.Actual;
    public string Endpoint => _endpoint.ToString();

    internal async IAsyncEnumerable<GridDashboardFrame> WatchGridAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            CheckAvailable(cancellationToken);
            if (_watching) throw new InvalidOperationException("지도 공급자에는 하나의 화면 구독만 연결합니다.");
            _watching = true;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopToken);
        using var timer = new PeriodicTimer(_pollInterval);
        try
        {
            do
            {
                GridDashboardFrame frame;
                try
                {
                    var state = await _http.GetFromJsonAsync<GridMapState>(new Uri(_endpoint, "api/state"), linked.Token).ConfigureAwait(false)
                        ?? throw new JsonException("지도 상태가 비어 있습니다.");
                    frame = Capture(state);
                }
                catch (Exception error) when (!linked.IsCancellationRequested && error is HttpRequestException or JsonException or OperationCanceledException)
                {
                    lock (_gate)
                    {
                        var devices = _latest.Change.Devices.Select(x => x with {
                            BrokerConnection = ConnectionObservation.Unknown, Liveness = LivenessObservation.Unknown, Revision = ++_revision
                        }).ToImmutableArray();
                        frame = new(new(++_revision, AmrChangeKind.DeviceChanged, devices, [], []), _latest.State,
                            error is JsonException ? "지도 응답 형식 오류 · 마지막 수신값 유지" : "지도 서버 응답 없음 · 마지막 수신값 유지");
                        _latest = frame;
                    }
                }
                yield return frame;
            } while (await timer.WaitForNextTickAsync(linked.Token).ConfigureAwait(false));
        }
        finally { lock (_gate) _watching = false; }
    }

    internal GridDashboardFrame Capture(GridMapState state)
    {
        Validate(state);
        lock (_gate)
        {
            CheckAvailable(default);
            long revision = ++_revision;
            // 수신 전/초기화에는 설정·계산 좌표로 장비 위치를 만들지 않는다.
            ImmutableArray<AmrSnapshot> devices = [];
            if (state.Domain is { } domain)
                return _latest = new(new(revision, AmrChangeKind.DeviceChanged,
                    domain.Devices.Select(x => x with { Revision = revision }).ToImmutableArray(), domain.Alarms, []), state, null);
            if (state.Position is not null && state.ReceivedTelemetry is { } telemetry)
                devices = [new(state.AmrId, telemetry.Position.X, telemetry.Position.Y, telemetry.Speed,
                    Enum.Parse<AmrState>(telemetry.State),
                    state.BrokerState == "Subscribed" ? ConnectionObservation.Connected :
                        state.BrokerState == "Failed" ? ConnectionObservation.Disconnected : ConnectionObservation.Unknown,
                    state.BrokerState != "Subscribed" || state.HeartbeatMonitor?.Monitoring != true ? LivenessObservation.Unknown :
                        state.HeartbeatMonitor.State switch { "Alive" => LivenessObservation.Alive, "Unresponsive" => LivenessObservation.Unresponsive, _ => LivenessObservation.Unknown },
                    state.ReceivedHeartbeat?.ReceivedAtUtc, telemetry.ReceivedAtUtc, revision)];
            return _latest = new(new(revision, AmrChangeKind.DeviceChanged, devices, [], []), state, null);
        }
    }

    private static void Validate(GridMapState state)
    {
        if (state.Fleet is { } fleet)
        {
            if (fleet.Length != 2 || state.Domain is not { } domain || domain.Statuses.IsDefault || domain.Devices.IsDefault || domain.Alarms.IsDefault ||
                fleet.Select(x => x.AmrId).Distinct(StringComparer.Ordinal).Count() != 2 ||
                domain.Statuses.Length != 2 || !domain.Statuses.Select(x => x.AmrId).Order().SequenceEqual(fleet.Select(x => x.AmrId).Order()))
                throw new JsonException("두 AMR의 Core 조회 계약이 필요합니다.");
            foreach (var member in fleet)
            {
                if (member.Fleet is not null || member.Domain is not null) throw new JsonException("중첩 Fleet는 허용하지 않습니다.");
                Validate(member);
            }
            if (domain.Devices.Select(x => x.AmrId).Distinct().Count() != domain.Devices.Length ||
                domain.Devices.Any(x => !fleet.Any(y => y.AmrId == x.AmrId) || !double.IsFinite(x.X) || !double.IsFinite(x.Y) ||
                    !double.IsFinite(x.SpeedMps) || x.SpeedMps < 0 || !Enum.IsDefined(x.State) || !Enum.IsDefined(x.Liveness) || !Enum.IsDefined(x.BrokerConnection)) ||
                domain.Statuses.Any(x => !Enum.IsDefined(x.Communication) || !Enum.IsDefined(x.Mission.State)) ||
                domain.Alarms.Any(x => !fleet.Any(y => y.AmrId == x.AmrId)))
                throw new JsonException("Core 장비/업무 상태가 올바르지 않습니다.");
            foreach (var device in domain.Devices)
            {
                var view = fleet.Single(x => x.AmrId == device.AmrId);
                if (device.X < 0 || device.X > view.Navigation.Map[0].Length - 1 || device.Y > 0 || device.Y < 1 - view.Navigation.Map.Length)
                    throw new JsonException("Core 위치가 지도 범위 밖입니다.");
            }
        }
        else if (state.Domain is not null) throw new JsonException("Core 조회에는 두 AMR 지도 상태가 필요합니다.");
        if (state.Source != "mqtt-grid-control-receiver" || string.IsNullOrWhiteSpace(state.AmrId) ||
            state.Navigation is not { Map: { Length: > 0 } } navigation || navigation.Map[0] is not { Length: > 0 } ||
            navigation.Path is null || navigation.Traveled is null || navigation.Waypoints is null || navigation.ReachedWaypoints is null ||
            state.ReceivedTrail is null || state.Logs is null)
            throw new JsonException("MQTT 격자 지도 상태가 필요합니다.");
        var map = navigation.Map;
        if (map.Any(row => row is null || row.Length != map[0].Length || row.Any(x => x is not (0 or 1))) ||
            navigation.FixedObstacles is null || navigation.FixedObstacles.Length != map.Length ||
            navigation.FixedObstacles.Any(row => row is null || row.Length != map[0].Length))
            throw new JsonException("격자 크기가 일치하지 않습니다.");
        bool InBounds(GridCell? cell) => cell is not null && cell.Row >= 0 && cell.Row < map.Length && cell.Col >= 0 && cell.Col < map[0].Length;
        if (!InBounds(navigation.Start) || !InBounds(navigation.Goal) || !InBounds(navigation.Current) ||
            navigation.Path.Concat(navigation.Traveled).Concat(navigation.Waypoints).Concat(state.ReceivedTrail).Any(cell => !InBounds(cell)) ||
            (navigation.CurrentTarget is not null && !InBounds(navigation.CurrentTarget)))
            throw new JsonException("격자 밖의 좌표입니다.");
        if (state.Position is { } cell)
        {
            var telemetry = state.ReceivedTelemetry;
            if (cell.Row < 0 || cell.Row >= map.Length || cell.Col < 0 || cell.Col >= map[0].Length ||
                telemetry is null || telemetry.Id != state.AmrId || !double.IsFinite(telemetry.Position.X) ||
                !double.IsFinite(telemetry.Position.Y) || !double.IsFinite(telemetry.Speed) || telemetry.Speed < 0 ||
                telemetry.Position.X != cell.Col || telemetry.Position.Y != -cell.Row ||
                !Enum.TryParse<AmrState>(telemetry.State, out var parsed) || !Enum.IsDefined(parsed))
                throw new JsonException("수신 위치와 격자 좌표가 일치하지 않습니다.");
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default) => await PostAsync("start", null, cancellationToken);
    public async Task CancelAsync(CancellationToken cancellationToken = default) => await PostAsync("cancel", null, cancellationToken);
    public async Task ResetAsync(CancellationToken cancellationToken = default) => await PostAsync("grid/reset", null, cancellationToken);
    internal Task FleetActionAsync(string action, string amrId, CancellationToken token)
    {
        ValidateAmrId(amrId);
        return PostAsync(action, new { amrId }, token);
    }
    internal Task FleetObstacleAsync(string amrId, GridCellEdit edit, CancellationToken token) =>
        PostAsync("grid/obstacle", new { amrId, edit.Row, edit.Col, edit.Blocked }, token);
    internal async Task SetObstacleAsync(GridCellEdit edit, CancellationToken cancellationToken) =>
        await PostAsync("grid/obstacle", new { edit.Row, edit.Col, edit.Blocked }, cancellationToken);

    private async Task PostAsync(string action, object? body, CancellationToken cancellationToken)
    {
        lock (_gate) CheckAvailable(cancellationToken);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_endpoint, "api/" + action));
        request.Headers.Add("X-Simulator-Map", "1");
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await _http.SendAsync(request, linked.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode(); // HTTP 접수는 MQTT Command/ACK 완료가 아니다.
    }

    public async IAsyncEnumerable<AmrChange> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var frame in WatchGridAsync(cancellationToken).ConfigureAwait(false)) yield return frame.Change;
    }

    public Task<IReadOnlyList<AmrSnapshot>> GetDevicesAsync(CancellationToken cancellationToken = default)
    { lock (_gate) { CheckAvailable(cancellationToken); return Task.FromResult<IReadOnlyList<AmrSnapshot>>(_latest.Change.Devices); } }
    public Task<AmrSnapshot?> GetDeviceAsync(string amrId, CancellationToken cancellationToken = default)
    { ValidateAmrId(amrId); lock (_gate) { CheckAvailable(cancellationToken); return Task.FromResult(_latest.Change.Devices.FirstOrDefault(x => x.AmrId == amrId)); } }
    public Task<IReadOnlyList<AlarmSnapshot>> GetAlarmsAsync(AmrQuery query, CancellationToken cancellationToken = default)
    { ValidateQuery(query); lock (_gate) { CheckAvailable(cancellationToken); return Task.FromResult<IReadOnlyList<AlarmSnapshot>>([]); } }
    public Task<HistoryResult> GetHistoryAsync(AmrQuery query, CancellationToken cancellationToken = default)
    { ValidateQuery(query); lock (_gate) { CheckAvailable(cancellationToken); return Task.FromResult(new HistoryResult([])); } }
    public Task<CommandSnapshot?> GetCommandAsync(string amrId, string commandId, CancellationToken cancellationToken = default)
    { ValidateAmrId(amrId); ArgumentException.ThrowIfNullOrWhiteSpace(commandId); lock (_gate) { CheckAvailable(cancellationToken); return Task.FromResult<CommandSnapshot?>(null); } }
    private void CheckAvailable(CancellationToken token) { ObjectDisposedException.ThrowIf(_disposed, this); token.ThrowIfCancellationRequested(); }
    private static void ValidateAmrId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '/' or '+' or '#'))
            throw new ArgumentException("AMR ID에 공백·제어 문자·Topic 구분자를 사용할 수 없습니다.", nameof(id));
    }
    private static void ValidateQuery(AmrQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.AmrId is not null) ValidateAmrId(query.AmrId);
        if (query.From > query.To) throw new ArgumentException("조회 시작은 종료 시각보다 늦을 수 없습니다.", nameof(query));
        if (query.Type is not null) ArgumentException.ThrowIfNullOrWhiteSpace(query.Type);
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate) { if (_disposed) return ValueTask.CompletedTask; _disposed = true; }
        _shutdown.Cancel();
        if (_ownsClient) _http.Dispose();
        _shutdown.Dispose();
        // 화면이 구독/요청 Task를 먼저 취소·대기한 뒤 앱 시작 지점이 이 공급자를 정리한다.
        return ValueTask.CompletedTask;
    }
}

internal sealed record GridDashboardFrame(AmrChange Change, GridMapState? State, string? Error);
internal sealed record GridCell(int Row, int Col);
internal sealed record GridCellEdit(int Row, int Col, bool Blocked);
internal sealed record GridWaypoint(int Index, GridCell Cell, int Moves);
internal sealed record GridNavigation(string Phase, GridCell Start, GridCell Goal, GridCell Current, int[][] Map,
    bool[][] FixedObstacles, GridCell[] Path, GridCell[] Traveled, int? RemainingDistance, int Moves, int Replans,
    GridCell[] Waypoints, int NextWaypointIndex, GridCell? CurrentTarget, GridWaypoint[] ReachedWaypoints);
internal sealed record GridTelemetryPosition(double X, double Y);
internal sealed record GridReceivedTelemetry(string Id, long Timestamp, GridTelemetryPosition Position, double Speed, string State, DateTimeOffset ReceivedAtUtc);
internal sealed record GridReceivedHeartbeat(string Id, long Timestamp, DateTimeOffset ReceivedAtUtc);
internal sealed record GridHeartbeatObservation(string AmrId, string State, bool Monitoring, DateTimeOffset? LastReceivedAtUtc,
    double? ElapsedMilliseconds, double TimeoutMilliseconds, double CheckIntervalMilliseconds, int TimeoutCount, int RecoveryCount);
internal sealed record GridMapState(GridNavigation Navigation, string Source, GridCell? Position, string AmrId,
    GridReceivedTelemetry? ReceivedTelemetry, GridReceivedHeartbeat? ReceivedHeartbeat, GridCell[] ReceivedTrail,
    int TelemetryCount, int HeartbeatCount, int RejectedCount, long? LastPublishedTimestamp, string BrokerState,
    string? Failure, string? MqttEndpoint, string[] Logs, GridHeartbeatObservation? HeartbeatMonitor = null,
    AmrDomainSnapshot? Domain = null, GridMapState[]? Fleet = null);
