using Amr.Core;
using Xunit;

namespace Amr.ControlGui.Tests;

public sealed class FeedbackViewTests
{
    [Fact]
    public Task StartStopWindowIsBoundToSelectionAndOnlyHistoryViewIsReset() => OnSta(() =>
    {
        var provider = new MockAmrDataProvider(64, 128, feedbackDemo: true);
        try
        {
            using var form = new MainForm(provider, new MockCommandDispatcher(provider));
            form.Show();
            ApplyCurrent(form, provider);
            Assert.Equal("전체(등록) 5대", Find<Label>(form, "devicesValue").Text);
            Assert.Equal("발생 중인 알람 2건", Find<Label>(form, "alarmsValue").Text);
            Assert.Contains("비고: 원인 확인", Find<Label>(form, "alarmDetail").Text);
            Find<Button>(form, "openSimulation").PerformClick();
            var simulation = Assert.Single(form.OwnedForms.OfType<SimulationForm>());
            WithSourceSettings(simulation, dialog => Find<Button>(dialog, "applySourceS02").PerformClick());
            Assert.Contains("X 4.0", Find<Label>(simulation, "sourceCoordinates").Text);
            Find<Button>(simulation, "simulationStart").PerformClick();
            provider.AdvanceSimulations(2);
            ApplyCurrent(form, provider);
            Assert.False(Find<Button>(simulation, "simulationStart").Enabled);
            Assert.True(Find<Button>(simulation, "simulationStop").Enabled);
            Assert.False(Find<Button>(simulation, "sourceSettings").Enabled);
            var grid = Find<DataGridView>(form, "deviceGrid");
            grid.CurrentCell = grid.Rows[1].Cells[0];
            Assert.Equal("전체(등록) 5대", Find<Label>(form, "devicesValue").Text);
            Assert.Equal("오류 1건 · 경고 1건", Find<Label>(form, "alarmsDetail").Text);
            Assert.Equal("AMR-01", Find<Label>(simulation, "simulationAmrId").Text);
            Find<Button>(simulation, "simulationStop").PerformClick();
            ApplyCurrent(form, provider);
            Assert.Contains("정지", Find<Label>(simulation, "simulationState").Text);
            Assert.False(Find<Button>(simulation, "simulationStop").Enabled);
            Assert.False(Find<Button>(simulation, "simulationStart").Enabled);
            Assert.Equal(SimulationPhase.Ready, provider.GetDeviceAsync("AMR-02").Result!.Journey!.Phase);

            var before = provider.GetHistoryAsync(new()).Result;
            Find<TextBox>(form, "historyAmr").Text = "AMR-01";
            Find<ComboBox>(form, "historyType").SelectedItem = "명령";
            Find<Button>(form, "historySearch").PerformClick();
            var history = Find<DataGridView>(form, "historyGrid");
            Assert.Equal(2, history.RowCount);
            history.CurrentCell = history.Rows[0].Cells[0];
            Find<Button>(form, "historyReset").PerformClick();
            Assert.Empty(Find<TextBox>(form, "historyAmr").Text);
            Assert.Equal(0, Find<ComboBox>(form, "historyType").SelectedIndex);
            Assert.False(Find<DateTimePicker>(form, "historyFrom").Checked);
            Assert.False(Find<DateTimePicker>(form, "historyTo").Checked);
            Assert.Empty(history.SelectedRows.Cast<DataGridViewRow>());
            Assert.Equal(before.Entries.Length, history.RowCount);
            Assert.Equal(before.Entries.ToArray(), provider.GetHistoryAsync(new()).Result.Entries.ToArray());
            Assert.Contains("이력을 선택하세요", Find<Label>(form, "historyDetail").Text);
            form.Close();
            Application.DoEvents();
        }
        finally { provider.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    });

    [Fact]
    public Task SourceSettingsOnlyAppliesExplicitChoiceAndKeepsOtherAmrUnchanged() => OnSta(() =>
    {
        var provider = new MockAmrDataProvider(64, 128, feedbackDemo: true);
        try
        {
            using var form = new MainForm(provider, new MockCommandDispatcher(provider));
            form.Show();
            ApplyCurrent(form, provider);
            Find<Button>(form, "openSimulation").PerformClick();
            var simulation = Assert.Single(form.OwnedForms.OfType<SimulationForm>());
            var before = provider.GetDeviceAsync("AMR-01").Result!;
            var other = provider.GetDeviceAsync("AMR-02").Result!;
            var history = provider.GetHistoryAsync(new()).Result.Entries.ToArray();
            WithSourceSettings(simulation, dialog =>
            {
                Assert.Equal("출발지 설정 · AMR-01", dialog.Text);
                Assert.Equal("S-01", Find<Label>(dialog, "sourceOptionS01").Text);
                Assert.Equal("S-02", Find<Label>(dialog, "sourceOptionS02").Text);
                Assert.Equal(before, provider.GetDeviceAsync("AMR-01").Result);
                foreach (string name in new[] { "applySourceS01", "applySourceS02", "closeSourceSelection", "sourceSelectionHint" })
                    for (Control child = Find<Control>(dialog, name); child.Parent is not null; child = child.Parent)
                        Assert.True(child.Parent.ClientRectangle.Contains(child.Bounds), name + " clipped: " + child.Bounds);
                Find<Button>(dialog, "closeSourceSelection").PerformClick();
            });
            Assert.Equal(before, provider.GetDeviceAsync("AMR-01").Result);
            Assert.Equal(history, provider.GetHistoryAsync(new()).Result.Entries.ToArray());
            WithSourceSettings(simulation, dialog => dialog.Close());
            Assert.Equal(before, provider.GetDeviceAsync("AMR-01").Result);
            Assert.Equal(history, provider.GetHistoryAsync(new()).Result.Entries.ToArray());
            WithSourceSettings(simulation, dialog => Find<Button>(dialog, "applySourceS02").PerformClick());
            ApplyCurrent(form, provider);
            var applied = provider.GetDeviceAsync("AMR-01").Result!;
            Assert.Equal("S-02", applied.Journey!.Source.Name);
            Assert.Equal((4d, 2d), (applied.X, applied.Y));
            Assert.Equal(SimulationPhase.Ready, applied.Journey.Phase);
            Assert.Equal("S-02", Find<Label>(simulation, "sourceName").Text);
            Assert.Contains("X 4.0", Find<Label>(simulation, "sourceCoordinates").Text);
            Assert.Equal(other, provider.GetDeviceAsync("AMR-02").Result);
            Assert.Equal(history.Length + 1, provider.GetHistoryAsync(new()).Result.Entries.Length);
            form.Close();
            Application.DoEvents();
        }
        finally { provider.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    });

    [Fact]
    public Task WindowsFitWorkingAreaAndZoomFitRestoresProjection() => OnSta(() =>
    {
        var provider = new MockAmrDataProvider(64, 128, feedbackDemo: true);
        try
        {
            using var form = new MainForm(provider, new MockCommandDispatcher(provider));
            form.Show();
            ApplyCurrent(form, provider);
            var map = Find<MapCanvas>(form, "mapCanvas");
            Assert.True(map.TryGetViewport(out var original));
            Find<Button>(form, "zoomIn").PerformClick();
            Assert.True(map.TryGetViewport(out var enlarged));
            Assert.True(enlarged.SpanX < original.SpanX);
            Find<Button>(form, "zoomFit").PerformClick();
            Assert.True(map.TryGetViewport(out var reset));
            Assert.Equal(original, reset);
            foreach (var size in new[] { form.MinimumSize, new Size(1200, 900) })
            {
                form.Size = size;
                form.PerformLayout();
                foreach (string name in new[] { "openSimulation", "historySearch", "historyReset", "historyGrid", "alarmGrid", "alarmDetail" })
                {
                    var control = Find<Control>(form, name);
                    for (Control child = control; child.Parent is not null; child = child.Parent)
                        Assert.True(child.Parent.ClientRectangle.Contains(child.Bounds), name + " clipped: " + child.GetType().Name + child.Bounds + " / " + child.Parent.GetType().Name + child.Parent.ClientRectangle);
                }
            }
            Find<Button>(form, "openSimulation").PerformClick();
            var simulation = Assert.Single(form.OwnedForms.OfType<SimulationForm>());
            Assert.True(Screen.FromControl(simulation).WorkingArea.Contains(simulation.Bounds));
            Find<Button>(simulation, "simulationStart").PerformClick();
            simulation.Close();
            Assert.Equal(SimulationPhase.Stopped, provider.GetDeviceAsync("AMR-01").Result!.Journey!.Phase);
            form.Close();
            Application.DoEvents();
        }
        finally { provider.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    });

    private static void WithSourceSettings(SimulationForm simulation, Action<SourceSelectionForm> action)
    {
        Exception? failure = null;
        simulation.BeginInvoke(new Action(() =>
        {
            try { action(Assert.Single(simulation.OwnedForms.OfType<SourceSelectionForm>())); }
            catch (Exception ex) { failure = ex; }
            finally { foreach (var dialog in simulation.OwnedForms.OfType<SourceSelectionForm>()) dialog.Close(); }
        }));
        Find<Button>(simulation, "sourceSettings").PerformClick();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static void ApplyCurrent(MainForm form, MockAmrDataProvider provider)
    {
        var watch = provider.WatchAsync().GetAsyncEnumerator();
        try { Assert.True(watch.MoveNextAsync().AsTask().GetAwaiter().GetResult()); form.ApplyChange(watch.Current); }
        finally { watch.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    private static async Task OnSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); completion.TrySetResult(); } catch (Exception ex) { completion.TrySetException(ex); } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
