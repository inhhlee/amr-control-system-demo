using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Amr.Core;
using Xunit;

namespace Amr.ControlGui.Tests;

public sealed class MainFormTests
{
    [Fact]
    public async Task ClosingWaitsForAnInFlightQueryCleanupAndLeavesTheSharedProviderAlive()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var provider = new DelayedSelectionProvider();
            Exception? failure = null;
            using var form = new MainForm(provider, new MockCommandDispatcher(provider.Mock));
            form.Shown += async (_, _) =>
            {
                try
                {
                    // 첫 스냅샷을 UI에 적용하고 다음 항목을 요청한 경계에서 선택을 바꾼다.
                    await provider.InitialConsumed.Task.WaitAsync(timeout.Token);
                    var grid = (DataGridView)form.Controls.Find("deviceGrid", true).Single();
                    grid.CurrentCell = grid.Rows[1].Cells[0];
                    await provider.QueryEntered.Task.WaitAsync(timeout.Token);
                    await Task.Run(() => provider.Mock.AdvanceDeviceAsync("AMR_001"), timeout.Token);
                    form.Close();
                    await provider.CancellationObserved.Task.WaitAsync(timeout.Token);
                    Assert.False(form.IsDisposed);
                    provider.CleanupComplete.TrySetResult();
                }
                catch (Exception error)
                {
                    failure = error;
                    provider.CleanupComplete.TrySetResult();
                    form.Close();
                }
            };
            try
            {
                Application.Run(form);
                Assert.Equal(0, provider.Mock.SubscriptionCount);
                Assert.Equal(2, provider.Mock.GetDevicesAsync().GetAwaiter().GetResult().Count);
                if (failure is not null) throw failure;
                completed.SetResult();
            }
            catch (Exception error) { completed.TrySetException(error); }
            finally { provider.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public async Task LateQueriesAndOlderNotificationsCannotOverwriteNewerDisplayedPosition()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new MainForm();
                var original = new AmrSnapshot("AMR_001", 0, 0, 0, AmrState.IDLE,
                    ConnectionObservation.Unknown, LivenessObservation.Unknown, null, null, 0);
                form.ApplyChange(Change(original));
                form.ApplyChange(Change(original with { X = 2, Revision = 2 }));
                form.ApplyDeviceQuery(original);
                Assert.Contains("X 2 m", form.SelectedDetails);
                form.ApplyDeviceQuery(original with { X = 4, Revision = 4 });
                form.ApplyChange(Change(original with { X = 3, Revision = 3 }));
                Assert.Contains("X 4 m", form.SelectedDetails);
                form.Dispose(); // 윈도 닫힘 뒤 using의 재정리도 안전해야 한다.
                completed.SetResult();
            }
            catch (Exception error) { completed.SetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static AmrChange Change(AmrSnapshot device) => new(device.Revision,
        AmrChangeKind.DeviceChanged, ImmutableArray.Create(device), [], [], device.AmrId);

    private sealed class DelayedSelectionProvider : IAmrDataProvider
    {
        public MockAmrDataProvider Mock { get; } = new(8, 16);
        public TaskCompletionSource InitialConsumed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource QueryEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CleanupComplete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ProviderMode Mode => ProviderMode.Mock;
        public async Task<AmrSnapshot?> GetDeviceAsync(string id, CancellationToken token = default)
        {
            QueryEntered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult();
                await CleanupComplete.Task;
                throw;
            }
            return null;
        }
        public Task<IReadOnlyList<AmrSnapshot>> GetDevicesAsync(CancellationToken token = default) => Mock.GetDevicesAsync(token);
        public Task<IReadOnlyList<AlarmSnapshot>> GetAlarmsAsync(AmrQuery query, CancellationToken token = default) => Mock.GetAlarmsAsync(query, token);
        public Task<HistoryResult> GetHistoryAsync(AmrQuery query, CancellationToken token = default) => Mock.GetHistoryAsync(query, token);
        public Task<CommandSnapshot?> GetCommandAsync(string id, string commandId, CancellationToken token = default) => Mock.GetCommandAsync(id, commandId, token);
        public async IAsyncEnumerable<AmrChange> WatchAsync([EnumeratorCancellation] CancellationToken token = default)
        {
            await foreach (var change in Mock.WatchAsync(token))
            {
                yield return change;
                if (change.Kind == AmrChangeKind.InitialSnapshot) InitialConsumed.TrySetResult();
            }
        }
        public ValueTask DisposeAsync() => Mock.DisposeAsync();
    }
}
