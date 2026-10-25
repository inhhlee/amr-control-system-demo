using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Amr.Core;

namespace Amr.ControlGui;

public sealed class MockAmrDataProvider : IAmrDataProvider
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AmrSnapshot> _devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CommandSnapshot> _commands = new(StringComparer.Ordinal);
    private readonly HashSet<Subscription> _subscriptions = [];
    private readonly int _subscriptionCapacity;
    private readonly int _commandCapacity;
    private readonly TimeProvider _clock;
    private long _revision;
    private bool _disposed;

    public MockAmrDataProvider(int subscriptionCapacity, int commandCapacity, TimeProvider? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(subscriptionCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(commandCapacity, 1);
        _subscriptionCapacity = subscriptionCapacity;
        _commandCapacity = commandCapacity;
        _clock = clock ?? TimeProvider.System;
        _devices.Add("AMR_001", InitialDevice("AMR_001", 0, 0));
        _devices.Add("AMR_002", InitialDevice("AMR_002", 5, 2));
    }

    public ProviderMode Mode => ProviderMode.Mock;
    internal int SubscriptionCount { get { lock (_gate) return _subscriptions.Count; } }

    public Task<IReadOnlyList<AmrSnapshot>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            CheckAvailable(cancellationToken);
            return Task.FromResult<IReadOnlyList<AmrSnapshot>>(DeviceValues());
        }
    }

    public Task<AmrSnapshot?> GetDeviceAsync(string amrId, CancellationToken cancellationToken = default)
    {
        ValidateAmrId(amrId);
        lock (_gate)
        {
            CheckAvailable(cancellationToken);
            return Task.FromResult(_devices.GetValueOrDefault(amrId));
        }
    }

    public Task<IReadOnlyList<AlarmSnapshot>> GetAlarmsAsync(AmrQuery query, CancellationToken cancellationToken = default)
    {
        ValidateQuery(query);
        lock (_gate)
        {
            CheckAvailable(cancellationToken);
            return Task.FromResult<IReadOnlyList<AlarmSnapshot>>(ImmutableArray<AlarmSnapshot>.Empty);
        }
    }

    public Task<HistoryResult> GetHistoryAsync(AmrQuery query, CancellationToken cancellationToken = default)
    {
        ValidateQuery(query);
        lock (_gate)
        {
            CheckAvailable(cancellationToken);
            return Task.FromResult(new HistoryResult([]));
        }
    }

    public Task<CommandSnapshot?> GetCommandAsync(string amrId, string commandId, CancellationToken cancellationToken = default)
    {
        ValidateAmrId(amrId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        lock (_gate)
        {
            CheckAvailable(cancellationToken);
            var command = _commands.GetValueOrDefault(commandId);
            return Task.FromResult(command?.AmrId == amrId ? command : null);
        }
    }

    public async IAsyncEnumerable<AmrChange> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Subscription subscription;
        AmrChange initial;
        lock (_gate)
        {
            CheckAvailable(cancellationToken);
            subscription = new Subscription(_subscriptionCapacity);
            _subscriptions.Add(subscription);
            // 등록과 초기 캡처를 같은 잠금에서 수행하여 중간 변경을 놓치지 않는다.
            initial = Capture(AmrChangeKind.InitialSnapshot);
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return initial;
            while (await subscription.Channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (subscription.Channel.Reader.TryRead(out var change))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lock (_gate)
                    {
                        if (_disposed) yield break;
                        if (subscription.Overflowed) throw new AmrResyncRequiredException();
                    }
                    yield return change;
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _subscriptions.Remove(subscription);
                subscription.Channel.Writer.TryComplete();
                while (subscription.Channel.Reader.TryRead(out _)) { }
            }
        }
    }

    public Task AdvanceDeviceAsync(string amrId, CancellationToken cancellationToken = default)
    {
        ValidateAmrId(amrId);
        lock (_gate)
        {
            CheckAvailable(cancellationToken);
            if (!_devices.TryGetValue(amrId, out var device)) throw new ArgumentException("시험 장비가 없습니다.", nameof(amrId));
            bool moving = device.State != AmrState.BUSY;
            _devices[amrId] = device with
            {
                X = device.X + 1,
                SpeedMps = moving ? 1 : 0,
                State = moving ? AmrState.BUSY : AmrState.IDLE,
                Revision = ++_revision
            };
            Publish(Capture(AmrChangeKind.DeviceChanged, amrId));
            return Task.CompletedTask;
        }
    }

    internal Task<CommandSubmission> SubmitAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAmrId(request.AmrId);
        lock (_gate)
        {
            CheckAvailable(cancellationToken);
            if (!_devices.ContainsKey(request.AmrId)) return Reject(CommandRejection.UnknownDevice, "등록된 시험 장비가 아닙니다.");
            if (request.Kind != CommandKind.MOVE) return Reject(CommandRejection.UnsupportedInMock, "첫 Mock은 MOVE 접수·결과 확인만 지원합니다.");
            if (request.X is not double x || request.Y is not double y || !double.IsFinite(x) || !double.IsFinite(y))
                return Reject(CommandRejection.InvalidParameters, "MOVE에는 유한한 X·Y가 필요합니다.");
            if (_commands.Count >= _commandCapacity) return Reject(CommandRejection.MockCapacityReached, "시험용 요청 보관 한도입니다. 앱을 다시 실행하세요.");

            // 이 잠금 안의 취소 확인이 접수 경계다. 등록 뒤에는 취소된 토큰 때문에 ID를 숨기지 않는다.
            var id = $"MOCK-{Guid.NewGuid():N}";
            var command = new CommandSnapshot(request.AmrId, id, request.Kind, request.X, request.Y, _clock.GetUtcNow(),
                PublishObservation.MockOnly, CommandResult.Unconfirmed, false, null, null, null, null, false, ++_revision);
            _commands.Add(id, command);
            Publish(Capture(AmrChangeKind.CommandChanged, request.AmrId, id));
            return Task.FromResult(new CommandSubmission(true, id, null, null));
        }
    }

    public Task CompleteCommandAsync(string amrId, string commandId, bool success, CancellationToken cancellationToken = default)
    {
        ValidateAmrId(amrId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        lock (_gate)
        {
            CheckAvailable(cancellationToken);
            if (!_commands.TryGetValue(commandId, out var command) || command.AmrId != amrId)
                throw new ArgumentException("대상 장비와 요청 ID에 맞는 시험 요청이 없습니다.", nameof(commandId));
            if (command.Result != CommandResult.Unconfirmed) throw new InvalidOperationException("이미 결과를 지정한 시험 요청입니다.");
            // 실제 ACK를 수신한 것이 아니므로 ACK 원본·시각과 실제 장비 좌표는 만들어 내지 않는다.
            _commands[commandId] = command with
            {
                Result = success ? CommandResult.Success : CommandResult.Failure,
                Reason = success ? "Mock 성공 시나리오" : "Mock 실패 시나리오",
                Revision = ++_revision
            };
            Publish(Capture(AmrChangeKind.CommandChanged, amrId, commandId));
            return Task.CompletedTask;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            foreach (var subscription in _subscriptions)
            {
                while (subscription.Channel.Reader.TryRead(out _)) { }
                subscription.Channel.Writer.TryComplete();
            }
            _subscriptions.Clear();
            // 주기 작업·전송 작업을 생성하지 않으므로 기다릴 내부 작업이 없다.
            return ValueTask.CompletedTask;
        }
    }

    private AmrChange Capture(AmrChangeKind kind, string? amrId = null, string? commandId = null) =>
        new(_revision, kind, DeviceValues(), [], _commands.Values.OrderBy(x => x.Revision).ToImmutableArray(), amrId, commandId);

    private ImmutableArray<AmrSnapshot> DeviceValues() => _devices.Values.OrderBy(x => x.AmrId, StringComparer.Ordinal).ToImmutableArray();

    private void Publish(AmrChange change)
    {
        foreach (var subscription in _subscriptions.ToArray())
        {
            if (subscription.Channel.Writer.TryWrite(change)) continue;
            subscription.Overflowed = true;
            while (subscription.Channel.Reader.TryRead(out _)) { }
            subscription.Channel.Writer.TryComplete(new AmrResyncRequiredException());
            _subscriptions.Remove(subscription);
        }
    }

    private void CheckAvailable(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        token.ThrowIfCancellationRequested();
    }

    private static AmrSnapshot InitialDevice(string id, double x, double y) =>
        new(id, x, y, 0, AmrState.IDLE, ConnectionObservation.Unknown, LivenessObservation.Unknown, null, null, 0);

    private static Task<CommandSubmission> Reject(CommandRejection code, string reason) => Task.FromResult(new CommandSubmission(false, null, code, reason));

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

    private sealed class Subscription(int capacity)
    {
        public Channel<AmrChange> Channel { get; } = System.Threading.Channels.Channel.CreateBounded<AmrChange>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false, // 공급자의 초과/종료 처리도 버퍼를 비운다.
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        public bool Overflowed { get; set; }
    }
}
