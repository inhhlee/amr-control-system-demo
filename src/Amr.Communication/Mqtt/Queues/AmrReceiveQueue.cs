using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Amr.Communication.Models;

namespace Amr.Communication.Mqtt;

/// <summary>검사된 메시지를 AMR별 한 줄에 넣고, 각 줄에서 한 건씩 처리한다.</summary>
/// <remarks>
/// 연결 세션마다 새 객체를 만든다. 소유자가 StopAsync를 호출해 취소·잔여 폐기·정리를 기다린다.
/// 처리자는 서로 다른 AMR에서 동시에 호출되며 취소에 협조해야 한다. 자기 큐의 종료를 기다리지 않는다.
/// 처리자의 동기 구간은 짧게 반환해야 한다. 취소는 진행 중인 동기 호출이 Task를 반환한 뒤 전달된다.
/// 동기 구간이 멈추면 StopAsync의 제한 시간이 적용되며, 늦은 반환 후 취소·정리를 계속한다.
/// 처리 기록 콜백은 짧게 반환하고 자체 오류를 처리한다. 오류 시 Trace에 예외 형식만 기록한다.
/// </remarks>
public sealed class AmrReceiveQueue
{
    private readonly object _gate = new();
    // 접수 잠금과 분리한다. 여러 AMR의 동기 호출은 함께 진입하고, 취소는 그 호출들의 반환 뒤에 전달한다.
    private readonly ReaderWriterLockSlim _dispatchGate = new();
    private readonly Dictionary<string, AmrQueue> _queues = new(StringComparer.Ordinal);
    private readonly Func<ReceivedMessage, CancellationToken, Task> _handler;
    private readonly Action<QueueProcessingEvent> _report;
    private readonly CancellationTokenSource _stop = new();
    private readonly int _capacityPerAmr;
    private readonly int _amrLimit;
    private Task? _completion;
    private Task? _stopTask;
    private volatile bool _stopping;

    internal AmrReceiveQueue(Func<ReceivedMessage, Task> handler, int capacityPerAmr = 256, int amrLimit = 64)
        : this((message, _) => handler(message), null, capacityPerAmr, amrLimit)
    {
        ArgumentNullException.ThrowIfNull(handler);
    }

    public AmrReceiveQueue(Func<ReceivedMessage, CancellationToken, Task> handler,
        Action<QueueProcessingEvent>? report = null, int capacityPerAmr = 256, int amrLimit = 64)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityPerAmr);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amrLimit);
        _handler = handler;
        _report = report ?? (entry => Trace.TraceError("{0}", JsonSerializer.Serialize(entry)));
        _capacityPerAmr = capacityPerAmr;
        _amrLimit = amrLimit;
    }

    /// <summary>빈자리를 기다리지 않고 접수 결과를 반환한다. Accepted는 업무 처리 성공이 아니다.</summary>
    public QueueAdmission TryEnqueue(ReceivedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_gate)
        {
            if (_completion is not null || _stopping) return QueueAdmission.NotAccepting;

            if (!_queues.TryGetValue(message.AmrId, out var queue))
            {
                if (_queues.Count >= _amrLimit) return QueueAdmission.AmrLimitReached;
                var channel = Channel.CreateBounded<ReceivedMessage>(new BoundedChannelOptions(_capacityPerAmr)
                {
                    SingleReader = false, // 소비자는 하나지만 종료 시 소유자도 잔여 항목을 꺼내 폐기한다.
                    SingleWriter = true, // 모든 쓰기는 위 잠금 안에서 순서대로 실행한다.
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false
                });

                // 전용 스레드가 아니다. 공유 스레드 풀에서 시작하고, await 중에는 스레드를 점유하지 않는다.
                // 처리자의 첫 await 이전 코드도 MQTT 수신 콜백에서 실행하지 않는다.
                queue = new(channel, Task.Run(() => ConsumeAsync(message.AmrId, channel.Reader)));
                _queues.Add(message.AmrId, queue);
            }

            // Wait 모드에서도 TryWrite는 기다리지 않고 false를 반환한다. 오래된 입력을 버리는 모드는 쓰지 않는다.
            return queue.Channel.Writer.TryWrite(message) ? QueueAdmission.Accepted : QueueAdmission.QueueFull;
        }
    }

    /// <summary>접수를 닫고 이미 접수한 작업을 모두 처리한다. 첫 단계 시험의 소비자 정리에 사용한다.</summary>
    /// <remarks>
    /// 취소·폐기하는 연결 종료와 다르다. 처리자가 끝나지 않으면 이 Task도 끝나지 않는다.
    /// 여러 번 호출해도 같은 완료 Task를 반환한다. 모든 생산자가 접수를 마친 뒤 소유자가 호출한다.
    /// </remarks>
    internal Task CompleteAsync()
    {
        lock (_gate)
        {
            if (_completion is not null) return _completion;
            foreach (var queue in _queues.Values) queue.Channel.Writer.TryComplete();
            _completion = Task.WhenAll(_queues.Values.Select(queue => queue.Consumer));
            return _completion;
        }
    }

    /// <summary>새 접수를 즉시 닫는다. 취소 통지와 소비자 종료는 반환하는 Task로 관찰한다.</summary>
    public Task RequestStop()
    {
        lock (_gate)
        {
            if (_stopTask is not null) return _stopTask;
            _stopping = true;
            var discards = new List<QueueProcessingEvent>();
            foreach (var pair in _queues)
            {
                pair.Value.Channel.Writer.TryComplete();
                int count = 0;
                while (pair.Value.Channel.Reader.TryRead(out _)) count++;
                if (count > 0) discards.Add(new("ShutdownDiscarded", pair.Key, Count: count));
            }
            Task[] consumers = _queues.Values.Select(queue => queue.Consumer).ToArray();
            // 취소 콜백이나 처리자를 수신 잠금 안에서 실행하지 않는다.
            _stopTask = Task.Run(async () =>
            {
                try
                {
                    Task cancellation;
                    _dispatchGate.EnterWriteLock();
                    try { cancellation = _stop.CancelAsync(); }
                    finally { _dispatchGate.ExitWriteLock(); }
                    foreach (var entry in discards) Report(entry);
                    await Task.WhenAll(consumers.Append(cancellation));
                    Report(new("Stopped"));
                }
                finally
                {
                    _stop.Dispose();
                    _dispatchGate.Dispose();
                }
            });
            // 대기 제한 이후에도 최종 실패가 관찰되지 않은 예외로 남지 않게 한다.
            _ = _stopTask.ContinueWith(task =>
                Trace.TraceError("QueueCleanupFailed ExceptionType={0}", task.Exception!.GetBaseException().GetType().Name),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            return _stopTask;
        }
    }

    /// <summary>취소·잔여 폐기를 요청하고 제한 시간만 기다린다. timeout 뒤에도 정리 Task는 계속 관찰한다.</summary>
    public async Task StopAsync(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        try { await RequestStop().WaitAsync(timeout); }
        catch (TimeoutException)
        {
            Report(new("StopTimeout"));
            throw;
        }
    }

    private async Task ConsumeAsync(string amrId, ChannelReader<ReceivedMessage> reader)
    {
        CancellationToken token = _stop.Token;
        while (await reader.WaitToReadAsync())
        {
            ReceivedMessage? message;
            lock (_gate)
            {
                if (_stopping) break;
                if (!reader.TryRead(out message)) continue;
            }
            try
            {
                Task? processing = null;
                bool discarded;
                _dispatchGate.EnterReadLock();
                try
                {
                    discarded = _stopping;
                    if (!discarded) processing = _handler(message, token);
                }
                finally { _dispatchGate.ExitReadLock(); }

                // 큐에서 꺼냈어도 실제 호출 진입 전에 종료가 시작되면 폐기 수량에 포함한다.
                if (discarded) Report(new("ShutdownDiscarded", message.AmrId, Count: 1));
                else await processing!;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { Report(new("HandlerCancelled", message.AmrId, message.Kind)); }
            catch (Exception exception)
            { Report(new("HandlerFailed", message.AmrId, message.Kind, ExceptionType: exception.GetType().Name)); }
        }
    }

    private void Report(QueueProcessingEvent entry)
    {
        try { _report(entry); }
        catch (Exception exception)
        { Trace.TraceError("ProcessingLogFailed ExceptionType={0}", exception.GetType().Name); }
    }

    private sealed record AmrQueue(Channel<ReceivedMessage> Channel, Task Consumer);
}
