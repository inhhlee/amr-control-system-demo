using System.Diagnostics;
using Amr.Simulator.Configuration;

namespace Amr.Simulator.Simulation;

/// <summary>구독 준비 후 시작하는 단조 시계. 별도 스레드 없이 실행 루프에서 Poll한다.</summary>
public sealed class FaultScenarioRunner
{
    private readonly Func<TimeSpan> _elapsed;
    private readonly (long Milliseconds, int Id, bool Activate, FaultScenario Fault)[] _events;
    private int _next;
    private double _processedSeconds;
    public bool HasPendingEvents => _next < _events.Length;

    public FaultScenarioRunner(IReadOnlyList<FaultScenario> scenarios, Func<TimeSpan>? elapsed = null)
    {
        var stopwatch = Stopwatch.StartNew();
        _elapsed = elapsed ?? (() => stopwatch.Elapsed);
        _events = scenarios.SelectMany((fault, id) => new[]
        {
            (Milliseconds: fault.ActivateAfterMs, Id: id, Activate: true, Fault: fault),
            (Milliseconds: fault.ResolveAfterMs, Id: id, Activate: false, Fault: fault)
        }).OrderBy(x => x.Milliseconds).ThenBy(x => x.Id).ToArray();
    }

    public CommandExecutionResult Poll(CommandDrivenAmr amr)
    {
        double now = _elapsed().TotalSeconds;
        if (now < _processedSeconds) throw new InvalidOperationException("Monotonic clock moved backwards.");
        var replies = new List<CommandFinalReply>();
        var alarms = new List<AlarmTransition>();
        bool causeResolved = false;
        while (_next < _events.Length && _events[_next].Milliseconds / 1000d <= now)
        {
            var change = _events[_next++];
            double due = change.Milliseconds / 1000d;
            // Tick 지연 시에도 장애 시점을 지나서 이동/도착시키지 않는다.
            replies.AddRange(amr.Advance(due - _processedSeconds));
            _processedSeconds = due;
            if (change.Activate)
            {
                var result = amr.ApplyFault(change.Id, change.Fault);
                replies.AddRange(result.Replies); alarms.AddRange(result.Alarms);
            }
            else { amr.ResolveFaultCause(change.Id); causeResolved = true; }
        }
        replies.AddRange(amr.Advance(now - _processedSeconds));
        _processedSeconds = now;
        return new(replies, causeResolved ? "FAULT_CAUSE_RESOLVED" : null) { Alarms = alarms };
    }
}
