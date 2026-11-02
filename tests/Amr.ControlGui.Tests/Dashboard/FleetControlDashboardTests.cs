using System.Collections.Immutable;
using Amr.Core;
using Amr.Infrastructure;
using Xunit;

namespace Amr.ControlGui.Tests;

public sealed class FleetControlDashboardTests
{
    [Fact]
    public void LongHorizontalMissionUsesFullPlotWithoutClippingMarkers()
    {
        var space = new RectangleF(44, 24, 480, 300);
        var a = new AmrSnapshot("A", 4, 2, 0, AmrState.IDLE, ConnectionObservation.Connected,
            LivenessObservation.Alive, null, null, 1);
        Assert.True(MapViewport.TryCreate(space, [a, a with { AmrId = "B", X = 1000, Y = 5 }], out var view));
        Assert.Equal(space, view.Plot);
        Assert.Equal(view.Plot.Width / view.SpanX, view.Plot.Height / view.SpanY, 8);
        foreach (var point in new[] { view.Project(4, 2), view.Project(1000, 5) })
        {
            Assert.InRange(point.Y - 18, view.Plot.Top, view.Plot.Bottom);
            Assert.InRange(point.Y + 18, view.Plot.Top, view.Plot.Bottom);
        }
    }

    [Fact]
    public async Task OneMapKeepsSelectionDraftsAndLateAckInTheCorrectDevicePanel()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var provider = new FleetMqttAmrDataProvider(new(), [new("A"), new("B")]);
                try
                {
                    using var form = new MainForm(provider, provider);
                    form.Show(); Application.DoEvents();
                    static AmrSnapshot Device(string id, int x) => new(id, x, 2, 0, AmrState.IDLE, ConnectionObservation.Connected,
                        LivenessObservation.Alive, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1)
                        { Journey = new(new("S", x, 2), new("G", x + 10, 2), SimulationPhase.Ready, 10, 0) };
                    var a = Device("A", 1); var b = Device("B", 5);
                    var ready = new DataConnectionSnapshot("test", "Fleet", DataConnectionState.Connected, 0, 5, 5, 0, true) { RegisteredAmrIds = ["A", "B"] };
                    var connections = ImmutableDictionary<string, DataConnectionSnapshot>.Empty.Add("A", ready with { AmrId = "A" }).Add("B", ready with { AmrId = "B" });
                    var change = new AmrChange(1, AmrChangeKind.InitialSnapshot, [a, b], [], []) { Connection = ready, DeviceConnections = connections };
                    form.ApplyChange(change);
                    T Find<T>(string name) where T : Control => (T)form.Controls.Find(name, true).Single();
                    var map = Find<MapCanvas>("mapCanvas");
                    Assert.Contains("장비 2대", map.AccessibleDescription);
                    Assert.NotNull(map.MarkerPoint("A")); Assert.NotNull(map.MarkerPoint("B"));
                    Assert.Single(Application.OpenForms.Cast<Form>(), x => x is MainForm);
                    Assert.Empty(Application.OpenForms.Cast<Form>().OfType<SimulationForm>());
                    Find<TextBox>("fleetTargetX").Text = "123";
                    map.SelectMarkerAt(map.MarkerPoint("B")!.Value);
                    Assert.Equal("B", form.SelectedDeviceId);
                    Assert.Equal("15", Find<TextBox>("fleetTargetX").Text);
                    var late = new CommandSnapshot("A", "A-late", CommandKind.MOVE, 11, 2, DateTimeOffset.UtcNow,
                        PublishObservation.Returned, CommandResult.Success, false, "SUCCESS", "ARRIVED", 100, DateTimeOffset.UtcNow, false, 2);
                    form.ApplyChange(change with { Revision = 2, Devices = [a with { Revision = 2, X = 11 }, b with { Revision = 2 }], Commands = [late] });
                    Assert.Equal("B", form.SelectedDeviceId);
                    Assert.DoesNotContain("A-late", Find<Label>("fleetResponse").Text);
                    Assert.Contains("B", Find<Label>("fleetResponse").Text);
                    map.SelectMarkerAt(map.MarkerPoint("A")!.Value);
                    Assert.Equal("123", Find<TextBox>("fleetTargetX").Text);
                    Assert.Contains("A-late", Find<Label>("fleetResponse").Text);
                    // A 연결 실패 시 전체 요약이 미확인이어도 B 제어는 유지한다.
                    form.ApplyChange(change with { Revision = 3, Connection = ready with { TelemetryIsCurrent = false },
                        DeviceConnections = connections.SetItem("A", ready with { AmrId = "A", State = DataConnectionState.Failed, TelemetryIsCurrent = false }) });
                    map.SelectMarkerAt(map.MarkerPoint("B")!.Value);
                    Assert.True(Find<Button>("fleetMove").Enabled);
                    Assert.DoesNotContain("마지막 수신값", form.SelectedDetails);
                    map.SelectMarkerAt(map.MarkerPoint("A")!.Value);
                    Assert.False(Find<Button>("fleetMove").Enabled);
                    Assert.Contains("마지막 수신값", form.SelectedDetails);
                    form.Close(); while (!form.IsDisposed) { Application.DoEvents(); Thread.Sleep(5); }
                }
                finally { provider.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                done.TrySetResult();
            }
            catch (Exception error) { done.TrySetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
