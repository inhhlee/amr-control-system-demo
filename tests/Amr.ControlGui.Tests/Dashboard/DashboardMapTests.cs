using System.Collections.Immutable;
using Amr.Core;
using Xunit;

namespace Amr.ControlGui.Tests;

public sealed class DashboardMapTests
{
    [Fact]
    public Task DashboardSeparatesOperationalStateFromConnectionAndLiveness() => OnSta(() =>
    {
        using var form = new MainForm();
        var devices = ImmutableArray.Create(
            Device("AMR_A", 0, 0),
            Device("AMR_B", 5, 2) with { State = AmrState.BUSY, BrokerConnection = ConnectionObservation.Connected, Liveness = LivenessObservation.Alive },
            Device("AMR_C", -3, -1) with { State = AmrState.STOPPED, BrokerConnection = ConnectionObservation.Disconnected, Liveness = LivenessObservation.Unresponsive },
            Device("AMR_D", 7, 4) with { State = AmrState.FAULT, BrokerConnection = ConnectionObservation.Connected });
        form.ApplyChange(new(1, AmrChangeKind.InitialSnapshot, devices, [new("AMR_D", "E01", "시험 알람", DateTimeOffset.UtcNow, 1)], []));
        Assert.Equal("전체 4대", Label(form, "devicesValue").Text);
        Assert.Equal("연결 끊김 관측", Label(form, "communicationValue").Text);
        Assert.Equal("연결 2 · 끊김 1 · 미확인 1", Label(form, "communicationDetail").Text);
        Assert.Contains("생존 확인 1 · 응답 없음 1", Label(form, "devicesDetail").Text);
        Assert.Equal("대기 1 · 이동 1", Label(form, "operationValue").Text);
        Assert.Equal("정지 1 · 오류 1", Label(form, "operationDetail").Text);
        Assert.Equal("현재 1건", Label(form, "alarmsValue").Text);
        Assert.Contains("AMR_D · E01", Label(form, "alarmSummary").Text);
    });

    [Fact]
    public Task EmptyAndUnknownDataAreNotReportedAsConnected() => OnSta(() =>
    {
        using var form = new MainForm();
        form.ApplyChange(new(0, AmrChangeKind.InitialSnapshot, [], [], []));
        Assert.Equal("데이터 없음", Label(form, "communicationValue").Text);
        Assert.Equal("전체 0대", Label(form, "devicesValue").Text);
        Assert.Contains("장비가 없습니다", Map(form).AccessibleDescription);
        form.ApplyChange(Change(1, Device("AMR_001", 0, 0)));
        Assert.Equal("연결 미확인", Label(form, "communicationValue").Text);
        Assert.Contains("미관측", form.SelectedDetails);
        Assert.Equal("현재 0건", Label(form, "alarmsValue").Text);
    });

    [Fact]
    public void MapCoordinatesUseEqualScaleAndPositiveYPointsUp()
    {
        Assert.True(MapViewport.TryCreate(new RectangleF(44, 24, 500, 300),
            [Device("A", -4, -2), Device("B", 10, 6)], out var view));
        var origin = view.Project(0, 0);
        var right = view.Project(1, 0);
        var up = view.Project(0, 1);
        Assert.True(right.X > origin.X);
        Assert.True(up.Y < origin.Y);
        Assert.InRange(Math.Abs((right.X - origin.X) - (origin.Y - up.Y)), 0, .001f);
        Assert.True(view.Plot.Contains(view.Project(-4, -2)));
        Assert.True(view.Plot.Contains(view.Project(10, 6)));
    }

    [Fact]
    public Task MarkerAndGridSelectionStayTogetherAfterPositionChangeAndResize() => OnSta(() =>
    {
        var provider = new MockAmrDataProvider(8, 16);
        try
        {
        using var form = new MainForm(provider, new MockCommandDispatcher(provider));
        var first = Device("AMR_001", 0, 0);
        var second = Device("AMR_002", 5, 2);
        form.ApplyChange(Change(1, first, second));
        var map = Map(form);
        map.Size = new Size(500, 350);
        map.SelectMarkerAt(map.MarkerPoint(second.AmrId)!.Value);
        Assert.Equal(second.AmrId, form.SelectedDeviceId);
        Assert.Equal(second.AmrId, map.SelectedAmrId);
        Assert.Contains("AMR_002", form.SelectedDetails);
        var before = map.MarkerPoint(second.AmrId)!.Value;
        form.ApplyChange(Change(2, first, second with { X = 6, State = AmrState.BUSY, SpeedMps = 1, Revision = 2 }));
        Assert.True(map.MarkerPoint(second.AmrId)!.Value.X > before.X);
        Assert.Contains("X 6 m", form.SelectedDetails);
        Assert.Equal("대기 1 · 이동 1", Label(form, "operationValue").Text);
        var grid = (DataGridView)form.Controls.Find("deviceGrid", true).Single();
        grid.CurrentCell = grid.Rows[0].Cells[0];
        Assert.Equal(first.AmrId, map.SelectedAmrId);
        map.Size = new Size(320, 240);
        Assert.True(map.TryGetViewport(out var resized));
        Assert.True(resized.Plot.Contains(map.MarkerPoint(second.AmrId)!.Value));
        }
        finally { provider.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    });

    [Fact]
    public Task OverlappingMarkersCanBothBeSelectedAndInvalidCoordinatesDoNotPaintAsValidPositions() => OnSta(() =>
    {
        using var map = new MapCanvas { Size = new Size(500, 350) };
        map.SetScene([Device("A", 0, 0), Device("B", 0, 0), Device("C", double.NaN, 1)], "A");
        var point = map.MarkerPoint("A")!.Value;
        map.SelectMarkerAt(point);
        Assert.Equal("B", map.SelectedAmrId);
        map.SelectMarkerAt(point);
        Assert.Equal("A", map.SelectedAmrId);
        Assert.Null(map.MarkerPoint("C"));
        Assert.Contains("잘못된 좌표 1건", map.AccessibleDescription);
        using var bitmap = new Bitmap(map.Width, map.Height);
        map.DrawToBitmap(bitmap, map.ClientRectangle);
        map.SetScene([Device("C", double.PositiveInfinity, 1)], null);
        Assert.False(map.TryGetViewport(out _));
        map.DrawToBitmap(bitmap, map.ClientRectangle);
    });

    [Fact]
    public void ExtremeRangesAndTooSmallAreasAreExplicitlyUnrenderable()
    {
        Assert.False(MapViewport.TryCreate(new RectangleF(0, 0, 30, 30), [Device("A", 0, 0)], out _));
        Assert.False(MapViewport.TryCreate(new RectangleF(0, 0, 500, 300),
            [Device("A", -double.MaxValue, 0), Device("B", double.MaxValue, 1)], out _));
        Assert.False(MapViewport.TryCreate(new RectangleF(0, 0, 500, 300), [Device("A", 1e308, 0)], out _));
    }

    [Fact]
    public Task SummaryDetailsAndBothDeviceRowsFitAtTheMinimumWindowSize() => OnSta(() =>
    {
        using var form = new MainForm();
        form.Show();
        Application.DoEvents();
        form.Size = form.MinimumSize;
        form.ApplyChange(Change(1, Device("AMR_001", 0, 0), Device("AMR_002", 5, 2)));
        foreach (string name in new[] { "communicationDetail", "devicesDetail", "operationDetail", "alarmsDetail" })
        {
            var label = Label(form, name);
            Assert.True(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height, name);
            var card = label.Parent!.Parent!;
            Assert.True(card.Parent!.ClientRectangle.Contains(card.Bounds), name + " card bounds");
        }
        var grid = (DataGridView)form.Controls.Find("deviceGrid", true).Single();
        Assert.True(grid.ClientSize.Height >= grid.ColumnHeadersHeight + grid.Rows.Cast<DataGridViewRow>().Sum(x => x.Height));
        Assert.True(Map(form).TryGetViewport(out _));
        form.Close();
    });

    [Fact]
    public Task CommandResultsAndControlsFitAfterResizingTheShownWindow() => OnSta(() =>
    {
        using var form = new MainForm();
        form.Show();
        Application.DoEvents(); // OnShown chooses the initial size before the user resizes it.
        var commands = ImmutableArray.Create(
            new CommandSnapshot("AMR_001", "MOCK-success", CommandKind.MOVE, 10, 5,
                DateTimeOffset.UtcNow, PublishObservation.MockOnly, CommandResult.Success,
                false, null, "Mock 성공 시나리오", null, null, false, 1),
            new CommandSnapshot("AMR_002", "MOCK-failure", CommandKind.MOVE, 10, 5,
                DateTimeOffset.UtcNow, PublishObservation.MockOnly, CommandResult.Failure,
                false, null, "Mock 실패 시나리오", null, null, false, 2));
        form.ApplyChange(new(2, AmrChangeKind.InitialSnapshot,
            [Device("AMR_001", 0, 0), Device("AMR_002", 5, 2)], [], commands));
        var grid = (DataGridView)form.Controls.Find("commandGrid", true).Single();
        foreach (var size in new[] { form.MinimumSize, new Size(1200, 900), form.MinimumSize })
        {
            form.Size = size;
            Application.DoEvents();
            Assert.Equal(size, form.Size);
            foreach (string name in new[] { "targetX", "targetY", "submitButton", "advanceButton", "completeSuccessButton", "completeFailureButton", "submissionLabel", "commandGrid" })
            {
                var control = form.Controls.Find(name, true).Single();
                for (Control child = control; child.Parent is not null; child = child.Parent)
                    Assert.True(child.Parent.ClientRectangle.Contains(child.Bounds), name + " clipped by " + child.Parent.GetType().Name);
            }
            var scrollbar = Assert.Single(grid.Controls.OfType<HScrollBar>(), x => x.Visible);
            foreach (DataGridViewRow row in grid.Rows)
            {
                grid.FirstDisplayedScrollingRowIndex = row.Index;
                Assert.True(grid.ClientSize.Height - grid.ColumnHeadersHeight - scrollbar.Height >= row.Height);
                Assert.Equal(row.Height, grid.GetRowDisplayRectangle(row.Index, true).Height);
            }
            grid.FirstDisplayedScrollingColumnIndex = grid.Columns["Reason"]!.Index;
            Assert.True(grid.Columns["Reason"]!.Displayed);
            grid.FirstDisplayedScrollingColumnIndex = grid.Columns["AmrId"]!.Index;
        }
        form.Close();
    });

    [Fact]
    public Task RequestCountersFollowSeparateMockAcceptanceAndCompletion() => OnSta(() =>
    {
        var provider = new MockAmrDataProvider(8, 16);
        try
        {
            using var form = new MainForm();
            var dispatcher = new MockCommandDispatcher(provider);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var watch = provider.WatchAsync(timeout.Token).GetAsyncEnumerator();
            try
            {
                Assert.True(watch.MoveNextAsync().AsTask().GetAwaiter().GetResult());
                form.ApplyChange(watch.Current);
                var accepted = dispatcher.DispatchAsync(new("AMR_001", CommandKind.MOVE, 10, 5)).GetAwaiter().GetResult();
                Assert.True(watch.MoveNextAsync().AsTask().GetAwaiter().GetResult());
                form.ApplyChange(watch.Current);
                Assert.Contains("대기 1 · 성공 0 · 실패 0", Label(form, "alarmsDetail").Text);
                provider.CompleteCommandAsync("AMR_001", accepted.CommandId!, true).GetAwaiter().GetResult();
                Assert.True(watch.MoveNextAsync().AsTask().GetAwaiter().GetResult());
                form.ApplyChange(watch.Current);
                Assert.Contains("대기 0 · 성공 1 · 실패 0", Label(form, "alarmsDetail").Text);
                Assert.Equal("대기 2 · 이동 0", Label(form, "operationValue").Text);
                Assert.Equal("현재 0건", Label(form, "alarmsValue").Text);
            }
            finally { watch.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        finally { provider.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    });

    private static Label Label(MainForm form, string name) => (Label)form.Controls.Find(name, true).Single();
    private static MapCanvas Map(MainForm form) => (MapCanvas)form.Controls.Find("mapCanvas", true).Single();
    private static AmrSnapshot Device(string id, double x, double y) => new(id, x, y, 0, AmrState.IDLE,
        ConnectionObservation.Unknown, LivenessObservation.Unknown, null, null, 0);
    private static AmrChange Change(long revision, params AmrSnapshot[] devices) => new(revision, AmrChangeKind.DeviceChanged, devices.ToImmutableArray(), [], []);
    private static async Task OnSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); completion.SetResult(); } catch (Exception error) { completion.SetException(error); } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
