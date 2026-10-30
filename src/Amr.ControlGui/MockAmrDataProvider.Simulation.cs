using System.Text.Json;
using Amr.Core;
using Amr.Simulator.Configuration;
using Amr.Simulator.Simulation;

namespace Amr.ControlGui;

// Mock의 실행·알람·이력 상태를 담당한다. 조회·구독·잠금/Revision은 기본 .cs 파일과 공유한다.
// MainForm은 이 데이터를 표시하고, 이 클래스가 Start/Stop에 따른 상태 전이를 결정한다.
public sealed partial class MockAmrDataProvider
{
    private readonly List<AlarmSnapshot> _demoAlarms = [];
    private readonly List<HistoryEntry> _history = [];
    private readonly Dictionary<string, SimulatedAmr> _simulations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _movementCommands = new(StringComparer.Ordinal);
    public bool FeedbackDemo { get; }
    public static IReadOnlyList<MapStation> SimulationSources { get; } = Array.AsReadOnly(new[]
    {
        new MapStation("S-01", 2, 2), new MapStation("S-02", 4, 2)
    });

    // 화면 검토용 등록 장비5대와 오류/경고 예시를 만든다. 실제 MQTT 수신 자료가 아니다.
    private void InitializeFeedbackDemo()
    {
        _devices.Clear();
        var positions = new[] { (2d, 2d), (4d, 9d), (16d, 3d), (15d, 7d), (4d, 4d) };
        for (int i = 0; i < positions.Length; i++)
        {
            string id = $"AMR-{i + 1:00}";
            var (x, y) = positions[i];
            var source = i == 0 ? SimulationSources[0] : new MapStation("현재 위치", x, y);
            _devices.Add(id, InitialDevice(id, x, y) with
            {
                BrokerConnection = ConnectionObservation.Connected,
                Liveness = i == 4 ? LivenessObservation.Unresponsive : LivenessObservation.Alive,
                State = i == 3 ? AmrState.FAULT : AmrState.IDLE,
                Journey = NewJourney(source)
            });
        }
        _demoAlarms.Add(new("AMR-04", "", "작업 진행 불가", _clock.GetUtcNow(), 0)
        { Severity = AlarmSeverity.Error, Note = "원인 확인과 조치가 필요한 상태" });
        _demoAlarms.Add(new("AMR-05", "", "주의 필요", _clock.GetUtcNow(), 0)
        { Severity = AlarmSeverity.Warning, Note = "작업은 가능하나 확인이 필요한 상태" });
        foreach (var alarm in _demoAlarms)
            Record(alarm.AmrId, "알람", $"{SeverityText(alarm.Severity)} · {alarm.Message} · 발생 중 · 비고: {alarm.Note}");
    }

    private static AmrJourney NewJourney(MapStation source) => new(source, new("A-03", 17, 10),
        SimulationPhase.Ready, Math.Abs(17 - source.X) + Math.Abs(10 - source.Y), 0);

    // 출발지 변경은 Mock 실행 준비이며 실제 장비 위치를 변경하는 명령이 아니다.
    public void SetSimulationSource(string amrId, string sourceName)
    {
        lock (_gate)
        {
            CheckAvailable(default);
            if (!FeedbackDemo || !_devices.TryGetValue(amrId, out var device)) throw new ArgumentException("등록된 Mock 장비가 아닙니다.", nameof(amrId));
            if (device.Journey?.Phase != SimulationPhase.Ready) throw new InvalidOperationException("출발지는 시작 전에만 설정할 수 있습니다.");
            var source = SimulationSources.FirstOrDefault(x => x.Name == sourceName) ?? throw new ArgumentException("출발지를 선택하세요.", nameof(sourceName));
            _devices[amrId] = device with { X = source.X, Y = source.Y, Journey = NewJourney(source), Revision = ++_revision };
            Record(amrId, "상태", $"출발지 {source.Name} ({source.X}, {source.Y}) 설정 · 대기");
            Publish(Capture(AmrChangeKind.DeviceChanged, amrId));
        }
    }

    // MockCommandDispatcher → SubmitAsync가 _gate 잠금 안에서 호출한다.
    // 시작은 대기·오류 없음·생존 확인·목적지 일치를, Stop은 이동 중인지 검사한 후 접수한다.
    private CommandSubmission SubmitSimulation(CommandRequest request)
    {
        CommandSubmission RejectSimulation(string reason)
        {
            Record(request.AmrId, "명령", $"{(request.Kind == CommandKind.MOVE ? "시작" : request.Kind.ToString())} 거부 · {reason}");
            ++_revision;
            Publish(Capture(AmrChangeKind.CommandChanged, request.AmrId));
            return new(false, null, CommandRejection.InvalidParameters, reason);
        }
        if (!_devices.TryGetValue(request.AmrId, out var device))
            return new(false, null, CommandRejection.UnknownDevice, "등록된 Mock 장비가 아닙니다.");
        var journey = device.Journey!;
        if (request.Kind is not (CommandKind.MOVE or CommandKind.STOP)) return RejectSimulation("시작·Stop만 지원합니다.");
        if (request.Kind == CommandKind.MOVE)
        {
            if (journey.Phase != SimulationPhase.Ready) return RejectSimulation("시작은 대기 상태에서만 가능합니다. Stop 후 재개는 지원하지 않습니다.");
            if (device.State == AmrState.FAULT || _demoAlarms.Any(x => x.AmrId == request.AmrId && x.IsActive && x.Severity == AlarmSeverity.Error))
                return RejectSimulation("작업 진행 불가 · 오류 확인이 필요합니다.");
            if (device.Liveness != LivenessObservation.Alive) return RejectSimulation("대상 AMR의 연결 상태를 확인하세요.");
            if (request.X != journey.Destination.X || request.Y != journey.Destination.Y) return RejectSimulation("할당된 목적지 좌표와 일치하지 않습니다.");
            if (_commands.Count >= _commandCapacity) return RejectSimulation("Mock 요청 보관 한도입니다. 앱을 다시 실행하세요.");
            // 검증된 Simulator의 순차 이동 계산을 재사용한다. 이 Mock은 X 이동 후 Y 이동한다.
            var settings = SimulatorSettingsLoader.Parse(JsonSerializer.Serialize(new
            {
                amrId = request.AmrId,
                initialPosition = new { x = journey.Source.X, y = journey.Source.Y },
                speedMps = 1.5, updateIntervalMs = 100,
                waypoints = new[] { new { x = journey.Destination.X, y = journey.Source.Y }, new { x = journey.Destination.X, y = journey.Destination.Y } }
            }));
            _simulations.Add(request.AmrId, new SimulatedAmr(settings));
        }
        else if (journey.Phase != SimulationPhase.Moving) return RejectSimulation("이동 중에만 Stop을 요청할 수 있습니다.");

        string id = $"MOCK-{Guid.NewGuid():N}";
        // MOVE 수락은 도착 완료가 아니므로 Unconfirmed로 기록한다. Stop은 아래에서 실제 Mock 이동을 끝낸다.
        var command = new CommandSnapshot(request.AmrId, id, request.Kind, request.X, request.Y, _clock.GetUtcNow(),
            PublishObservation.MockOnly, request.Kind == CommandKind.STOP ? CommandResult.Success : CommandResult.Unconfirmed,
            false, null, request.Kind == CommandKind.STOP ? "Stop 수락 · 정지 완료" : "시작 수락 · 이동 중", null, null, false, ++_revision);
        _commands.Add(id, command);
        Record(request.AmrId, "명령", command.Reason!);
        if (request.Kind == CommandKind.MOVE)
        {
            _movementCommands.Add(request.AmrId, id);
            _devices[request.AmrId] = device with { State = AmrState.BUSY, SpeedMps = 1.5, Revision = _revision,
                Journey = journey with { Phase = SimulationPhase.Moving, StartedAt = _clock.GetUtcNow() } };
        }
        else FinishMovement(request.AmrId, false, "Stop 요청으로 이동이 정지되었습니다.");
        Publish(Capture(AmrChangeKind.CommandChanged, request.AmrId, id));
        return new(true, id, null, command.Reason);
    }

    // 호출자는 단조 경과 시간을 전달한다. 같은 잠금이 Start/Stop/창 닫힘과 Tick의 순서를 직렬화한다.
    public void AdvanceSimulations(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        lock (_gate)
        {
            CheckAvailable(default);
            if (elapsedSeconds == 0 || _simulations.Count == 0) return;
            foreach (var (id, simulation) in _simulations.ToArray())
            {
                simulation.Advance(elapsedSeconds);
                var device = _devices[id];
                var journey = device.Journey!;
                double moved = Math.Abs(simulation.Position.X - journey.Source.X) + Math.Abs(simulation.Position.Y - journey.Source.Y);
                _devices[id] = device with { X = simulation.Position.X, Y = simulation.Position.Y,
                    Revision = ++_revision, Journey = journey with { TraveledDistance = Math.Min(journey.TotalDistance, moved) } };
                if (simulation.IsCompleted) FinishMovement(id, true, "목적지 도착 · 실행 완료");
            }
            Publish(Capture(AmrChangeKind.DeviceChanged));
        }
    }

    // 실행창 닫힘은 해당 ID의 가상 이동만 종료한다. 다른 AMR이나 공유 공급자는 종료하지 않는다.
    public void StopSimulationOnWindowClose(string amrId)
    {
        lock (_gate)
        {
            CheckAvailable(default);
            if (!_simulations.ContainsKey(amrId)) return;
            FinishMovement(amrId, false, "실행창 닫힘 · Mock 이동 정지");
            Publish(Capture(AmrChangeKind.DeviceChanged, amrId));
        }
    }

    // 이동 목록에서 제거하면 다음 Tick부터 좌표가 갱신되지 않는다. 마지막 위치는 보존한다.
    // 도착은 MOVE 성공, Stop/창 닫힘은 MOVE 미완료(Failure)로 기록하며 별도 Stop 요청의 성공과 구분한다.
    private void FinishMovement(string amrId, bool arrived, string reason)
    {
        _simulations.Remove(amrId);
        var device = _devices[amrId];
        _devices[amrId] = device with { State = arrived ? AmrState.IDLE : AmrState.STOPPED, SpeedMps = 0, Revision = ++_revision,
            Journey = device.Journey! with { Phase = arrived ? SimulationPhase.Arrived : SimulationPhase.Stopped } };
        if (_movementCommands.Remove(amrId, out string? commandId))
            _commands[commandId] = _commands[commandId] with { Result = arrived ? CommandResult.Success : CommandResult.Failure, Reason = reason, Revision = _revision };
        Record(amrId, "상태", reason);
    }

    // 원본 이력은 공급자 세션에 누적된다. 화면의 조회 조건 초기화가 이 목록을 지우지 않는다.
    private void Record(string id, string type, string text) => _history.Add(new(id, type, text, _clock.GetUtcNow()));
    private static bool Matches(AmrQuery query, string id, DateTimeOffset time, string type) =>
        (query.AmrId is null || query.AmrId == id) && (query.Type is null || query.Type == type) &&
        (query.From is null || time >= query.From) && (query.To is null || time <= query.To);
    internal static string SeverityText(AlarmSeverity severity) => severity switch { AlarmSeverity.Error => "오류", AlarmSeverity.Warning => "경고", _ => "미분류" };
}
