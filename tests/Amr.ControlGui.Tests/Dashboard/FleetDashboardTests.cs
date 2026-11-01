using System.Net;
using System.Text.Json;
using Amr.Core;
using Xunit;

namespace Amr.ControlGui.Tests;

public sealed class FleetDashboardTests
{
    [Fact]
    public async Task CoreSnapshotIsAuthoritativeInsteadOfGuiRecalculatingRawHeartbeat()
    {
        var run = new Fixture();
        await using var provider = new GridMapDataProvider(new("http://127.0.0.1:59344"));
        var state = run.State();
        state = state with { HeartbeatMonitor = new("AMR_001", "Unresponsive", true, null, 9000, 5000, 500, 1, 0) };
        var frame = provider.Capture(state);
        Assert.Equal(2, frame.Change.Devices.Length);
        Assert.All(frame.Change.Devices, x => Assert.Equal(LivenessObservation.Alive, x.Liveness));
        run.LostA();
        frame = provider.Capture(run.State());
        Assert.Equal(LivenessObservation.Unresponsive, frame.Change.Devices.Single(x => x.AmrId == "AMR_001").Liveness);
        Assert.Equal(LivenessObservation.Alive, frame.Change.Devices.Single(x => x.AmrId == "AMR_002").Liveness);
        Assert.Equal("COMM_LOST", Assert.Single(frame.Change.Alarms).Code);
    }

    [Fact]
    public async Task MalformedFleetDomainIdsAndOutOfMapPositionsAreRejected()
    {
        var run = new Fixture();
        await using var provider = new GridMapDataProvider(new("http://127.0.0.1:59344"));
        var state = run.State();
        Assert.Throws<JsonException>(() => provider.Capture(state with { Domain = null }));
        Assert.Throws<JsonException>(() => provider.Capture(state with { Fleet = [state.Fleet![0], state.Fleet[0]] }));
        var broken = state.Domain! with { Devices = [state.Domain.Devices[0] with { X = 99 }, state.Domain.Devices[1]] };
        Assert.Throws<JsonException>(() => provider.Capture(state with { Domain = broken }));
    }

    [Fact]
    public async Task SelectedAmrIsSentWithResumeRequestAndControlHeader()
    {
        var requests = new List<(string Path, string Body, bool Header)>();
        using var http = new HttpClient(new Handler(async (request, token) => {
            requests.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(token), request.Headers.Contains("X-Simulator-Map")));
            return new(HttpStatusCode.Accepted);
        }));
        await using var provider = new GridMapDataProvider(new("http://127.0.0.1:59344"), http);
        await provider.FleetActionAsync("start", "AMR_002", default);
        Assert.Equal("/api/start", Assert.Single(requests).Path);
        Assert.Contains("AMR_002", requests[0].Body);
        Assert.True(requests[0].Header);
    }

    [Fact]
    public async Task FormShowsIndependentStatusAlarmAndExplicitResumeForSelectedAmr() => await OnSta(() => {
        var run = new Fixture();
        var provider = new GridMapDataProvider(new("http://127.0.0.1:59344"));
        using var form = new MainForm(provider, null);
        try
        {
            var first = run.State() with { HeartbeatMonitor = new("AMR_001", "Unresponsive", true, null, 9000, 5000, 500, 1, 0) };
            form.ApplyGridFrame(provider.Capture(first));
            Assert.Contains("AMR 생존 확인됨", Label(form, "gridDetails").Text);
            var table = (DataGridView)form.Controls.Find("deviceGrid", true).Single();
            Assert.Equal(2, table.Rows.Count);
            run.LostA(); form.ApplyGridFrame(provider.Capture(run.State()));
            Assert.Contains("응답 없음 1", Label(form, "devicesDetail").Text);
            Assert.Contains("AMR_001", Label(form, "alarmSummary").Text);
            Assert.Contains("보류", form.SelectedDetails);
            Assert.False(Button(form, "gridStartButton").Enabled);
            run.Alive("AMR_001", 3); form.ApplyGridFrame(provider.Capture(run.State()));
            Assert.Contains("보류", form.SelectedDetails);
            Assert.Equal("미션 재개", Button(form, "gridStartButton").Text);
            Assert.True(Button(form, "gridStartButton").Enabled);
            table.ClearSelection(); table.Rows[1].Selected = true; table.CurrentCell = table.Rows[1].Cells[0];
            form.ApplyGridFrame(provider.Capture(run.State()));
            Assert.Equal("AMR_002", form.SelectedDeviceId);
            Assert.DoesNotContain("보류", form.SelectedDetails);
            Assert.Contains("생존 확인 2", Label(form, "devicesDetail").Text);
        }
        finally { provider.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    });

    private static Label Label(MainForm form, string name) => (Label)form.Controls.Find(name, true).Single();
    private static Button Button(MainForm form, string name) => (Button)form.Controls.Find(name, true).Single();
    private static async Task OnSta(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); done.SetResult(); } catch (Exception error) { done.SetException(error); } });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
    private sealed class Fixture
    {
        public AmrDomainManager Domain = new(["AMR_001", "AMR_002"]);
        private readonly Guid _session = Guid.NewGuid();
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
        public Fixture()
        {
            Domain.BeginSession(_session);
            Alive("AMR_001", 1); Alive("AMR_002", 1);
            Domain.ReceiveTelemetry(new(_session, "AMR_001", 0, 0, 1, AmrState.BUSY, _now));
            Domain.ReceiveTelemetry(new(_session, "AMR_002", 2, -1, 1, AmrState.BUSY, _now));
            Domain.StartMission("AMR_001", "M1"); Domain.StartMission("AMR_002", "M2");
        }
        public void Alive(string id, long seq) => Domain.Observe(new(_session, seq, id, ConnectionObservation.Connected, LivenessObservation.Alive, true, _now, _now));
        public void LostA() => Domain.Observe(new(_session, 2, "AMR_001", ConnectionObservation.Connected, LivenessObservation.Unresponsive, true, _now, _now));
        public GridMapState State()
        {
            var a = GridDashboardTests.State();
            var b = a with { AmrId = "AMR_002", Position = new(1, 2), ReceivedTelemetry = a.ReceivedTelemetry! with { Id = "AMR_002", Position = new(2, -1) },
                ReceivedHeartbeat = a.ReceivedHeartbeat! with { Id = "AMR_002" }, ReceivedTrail = [new(1, 2)] };
            return a with { Fleet = [a, b], Domain = Domain.Read() };
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
}
