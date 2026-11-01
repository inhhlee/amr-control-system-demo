using System.Net;
using System.Text.Json;
using Amr.Core;
using Xunit;

namespace Amr.ControlGui.Tests;

public sealed class GridDashboardTests
{
    [Fact]
    public async Task HeartbeatTimeoutAndBrokerDisconnectionRemainSeparateFromOperationalState()
    {
        await using var provider=new GridMapDataProvider(new("http://127.0.0.1:59344"));
        var observation=new GridHeartbeatObservation("AMR_001","Alive",true,DateTimeOffset.UtcNow,0,5000,500,0,0);
        var state=State() with {HeartbeatMonitor=observation};
        var device=Assert.Single(provider.Capture(state).Change.Devices);
        Assert.Equal(LivenessObservation.Alive,device.Liveness);
        device=Assert.Single(provider.Capture(state with{HeartbeatMonitor=observation with{State="Unresponsive",TimeoutCount=1}}).Change.Devices);
        Assert.Equal(LivenessObservation.Unresponsive,device.Liveness);Assert.Equal(ConnectionObservation.Connected,device.BrokerConnection);
        Assert.Equal(AmrState.BUSY,device.State);
        device=Assert.Single(provider.Capture(state with{BrokerState="Failed",HeartbeatMonitor=observation with{State="Stopped",Monitoring=false}}).Change.Devices);
        Assert.Equal(ConnectionObservation.Disconnected,device.BrokerConnection);Assert.Equal(LivenessObservation.Unknown,device.Liveness);
    }
    [Fact]
    public async Task LiveProviderRetainsTheExistingQueryValidationCancellationAndDisposalContract()
    {
        var provider = new GridMapDataProvider(new("http://127.0.0.1:59344"));
        foreach (var id in new[] { "", "AMR +", "amr/id" })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => provider.GetDeviceAsync(id));
            await Assert.ThrowsAsync<ArgumentException>(() => provider.GetCommandAsync(id,"CMD"));
        }
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetCommandAsync("AMR_001"," "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GetHistoryAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetAlarmsAsync(new(From:DateTimeOffset.MaxValue,To:DateTimeOffset.MinValue)));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.GetDevicesAsync(cancel.Token));
        await provider.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.GetDevicesAsync());
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task DisplayUsesReceivedPositionInsteadOfSimulationPositionAndHeartbeatDoesNotCreateADevice()
    {
        await using var provider = new GridMapDataProvider(new("http://127.0.0.1:59344"));
        var state = State();
        var waiting = provider.Capture(state with { Position = null, ReceivedTelemetry = null });
        Assert.Empty(waiting.Change.Devices);
        var device = Assert.Single(provider.Capture(state).Change.Devices);
        Assert.Equal(0, device.X); // navigation.Current is (0,1), but received position is (0,0).
        Assert.Equal(0, device.Y);
        Assert.Equal(ConnectionObservation.Connected, device.BrokerConnection);
        Assert.Equal(LivenessObservation.Unknown, device.Liveness);
        Assert.Equal(state.ReceivedHeartbeat!.ReceivedAtUtc, device.LastHeartbeatReceivedAt);
        Assert.Empty((await provider.GetHistoryAsync(new())).Entries);
        Assert.Null(await provider.GetCommandAsync("AMR_001", "not-an-ack"));
    }

    [Fact]
    public async Task NoPathNullDistanceIsAValidStateRatherThanAConnectionFailure()
    {
        var state = State();
        state = state with { Navigation = state.Navigation with { Phase = "Waiting", RemainingDistance = null, Path = [] } };
        using var http = new HttpClient(new Handler((_,_) => Task.FromResult(Response(state))));
        await using var provider = new GridMapDataProvider(new("http://127.0.0.1:59344"),http);
        await using var watch = provider.WatchGridAsync().GetAsyncEnumerator();
        Assert.True(await watch.MoveNextAsync());
        Assert.Null(watch.Current.Error);
        Assert.Null(watch.Current.State!.Navigation.RemainingDistance);
        Assert.Single(watch.Current.Change.Devices);
    }

    [Fact]
    public async Task NormalSessionCompletionDoesNotImplyBrokerOutageOrRobotLiveness()
    {
        await using var provider = new GridMapDataProvider(new("http://127.0.0.1:59344"));
        var device = Assert.Single(provider.Capture(State() with { BrokerState = "Closed" }).Change.Devices);
        Assert.Equal(ConnectionObservation.Unknown, device.BrokerConnection);
        Assert.Equal(LivenessObservation.Unknown, device.Liveness);
    }

    [Theory]
    [InlineData("https://127.0.0.1:59344")]
    [InlineData("http://example.com:59344")]
    [InlineData("http://127.0.0.1:59344/other")]
    [InlineData("http://user:secret@127.0.0.1:59344")]
    public void OnlyTheLocalMapOriginIsAccepted(string url) => Assert.Throws<ArgumentException>(() => new GridMapDataProvider(new(url)));

    [Fact]
    public async Task WrongIdentityCoordinatesAndSourceAreRejectedWithoutReplacingThePreviousSnapshot()
    {
        await using var provider = new GridMapDataProvider(new("http://127.0.0.1:59344"));
        var state = State();
        var first = provider.Capture(state);
        foreach (var invalid in new[] {
            state with { Source = "csharp-grid-navigation" },
            state with { Position = new(4,5) },
            state with { ReceivedTelemetry = state.ReceivedTelemetry! with { Id = "OTHER" } },
            state with { ReceivedTelemetry = state.ReceivedTelemetry! with { Position = new(1,0) } },
            state with { Navigation = state.Navigation with { FixedObstacles = [] } }
        }) Assert.Throws<JsonException>(() => provider.Capture(invalid));
        Assert.Equal(first.Change.Devices[0], (await provider.GetDevicesAsync())[0]);
    }

    [Fact]
    public async Task ServerFailureKeepsLastReceivedPositionMarksItUnknownAndCanRecover()
    {
        int call = 0;
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(++call == 2
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Response(State()))));
        await using var provider = new GridMapDataProvider(new("http://127.0.0.1:59344"), http, TimeSpan.FromMilliseconds(1));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var watch = provider.WatchGridAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await watch.MoveNextAsync());
        var first = watch.Current;
        Assert.True(await watch.MoveNextAsync());
        Assert.Contains("마지막 수신값", watch.Current.Error);
        var stale = Assert.Single(watch.Current.Change.Devices);
        Assert.Equal(first.Change.Devices[0].X, stale.X);
        Assert.Equal(ConnectionObservation.Unknown, stale.BrokerConnection);
        Assert.True(await watch.MoveNextAsync());
        Assert.Null(watch.Current.Error);
        Assert.Equal(ConnectionObservation.Connected, Assert.Single(watch.Current.Change.Devices).BrokerConnection);
    }

    [Fact]
    public async Task MalformedApiResponseDoesNotInventAPositionAndNextResponseRecovers()
    {
        int call = 0;
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(++call == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") } : Response(State()))));
        await using var provider = new GridMapDataProvider(new("http://127.0.0.1:59344"), http, TimeSpan.FromMilliseconds(1));
        await using var watch = provider.WatchGridAsync().GetAsyncEnumerator();
        Assert.True(await watch.MoveNextAsync());
        Assert.NotNull(watch.Current.Error);
        Assert.Empty(watch.Current.Change.Devices);
        Assert.True(await watch.MoveNextAsync());
        Assert.Null(watch.Current.Error);
        Assert.Single(watch.Current.Change.Devices);
    }

    [Fact]
    public async Task CancellingAWatchCancelsAndWaitsForItsPendingHttpRequest()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool cleaned = false;
        using var http = new HttpClient(new Handler(async (_, token) => {
            entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); return Response(State()); }
            finally { cleaned = true; }
        }));
        await using var provider = new GridMapDataProvider(new("http://127.0.0.1:59344"), http);
        using var cancel = new CancellationTokenSource();
        await using var watch = provider.WatchGridAsync(cancel.Token).GetAsyncEnumerator();
        var move = watch.MoveNextAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => move);
        Assert.True(cleaned);
    }

    [Fact]
    public async Task ControlsUseMarkedMapRequestsAndDoNotCreateMockCommandsOrAckResults()
    {
        var requests = new List<(string Path, string Body)>();
        using var http = new HttpClient(new Handler(async (request, token) => {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("1", Assert.Single(request.Headers.GetValues("X-Simulator-Map")));
            requests.Add((request.RequestUri!.AbsolutePath, request.Content is null ? "" : await request.Content.ReadAsStringAsync(token)));
            return new HttpResponseMessage(requests.Count == 4 ? HttpStatusCode.Conflict : HttpStatusCode.Accepted);
        }));
        await using var provider = new GridMapDataProvider(new("http://127.0.0.1:59344"), http);
        await provider.StartAsync();
        await provider.SetObstacleAsync(new(0,1,true), default);
        await provider.CancelAsync();
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.ResetAsync());
        Assert.Equal(new[] {"/api/start", "/api/grid/obstacle", "/api/cancel", "/api/grid/reset"}, requests.Select(x => x.Path));
        using var body = JsonDocument.Parse(requests[1].Body);
        Assert.Equal(1, body.RootElement.GetProperty("col").GetInt32());
        Assert.True(body.RootElement.GetProperty("blocked").GetBoolean());
        Assert.Null(await provider.GetCommandAsync("AMR_001", "anything"));
    }

    [Fact]
    public Task LiveControlsAndSelectedDetailsFitTheMinimumWindow() => OnSta(() => {
        using var http = new HttpClient(new Handler((_,_) => Task.FromResult(Response(State()))));
        var provider = new GridMapDataProvider(new("http://127.0.0.1:59344"),http);
        try
        {
            using var form = new MainForm(provider,null);
            form.Show();
            form.Size = form.MinimumSize;
            form.ApplyGridFrame(provider.Capture(State()));
            Application.DoEvents();
            foreach(var name in new[]{"gridStartButton","gridCancelButton","gridResetButton","gridDetails","detailsLabel"})
            {
                var control=form.Controls.Find(name,true).Single();
                for(Control child=control;child.Parent is not null;child=child.Parent)
                    Assert.True(child.Parent.ClientRectangle.Contains(child.Bounds),name+" clipped");
                if(control is Label label) Assert.True(label.Height>=label.GetPreferredSize(new Size(label.Width,0)).Height,name+" text clipped");
            }
            form.Close();
            Application.DoEvents();
        }
        finally {provider.DisposeAsync().AsTask().GetAwaiter().GetResult();}
    });

    [Fact]
    public async Task ClosingTheLiveFormWaitsForHttpCancellationCleanup()
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(() => {
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleanup=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var http=new HttpClient(new Handler(async(_,token)=>{
                entered.TrySetResult();
                try {await Task.Delay(Timeout.Infinite,token);return Response(State());}
                finally {cancelled.TrySetResult();await cleanup.Task;}
            }));
            var provider=new GridMapDataProvider(new("http://127.0.0.1:59344"),http);
            using var form=new MainForm(provider,null);
            Exception? failure=null;
            form.Shown+=async(_,_)=>{
                try {
                    await entered.Task.WaitAsync(timeout.Token);
                    form.Close();
                    await cancelled.Task.WaitAsync(timeout.Token);
                    Assert.False(form.IsDisposed);
                }
                catch(Exception error){failure=error;}
                finally {cleanup.TrySetResult();}
            };
            try {Application.Run(form);if(failure is not null)throw failure;done.TrySetResult();}
            catch(Exception error){done.TrySetException(error);}
            finally {provider.DisposeAsync().AsTask().GetAwaiter().GetResult();}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public Task GridMarkersEditsAndPendingCompletionMatchTheSameReceivedFrame() => OnSta(() => {
        using var http = new HttpClient(new Handler((_,_) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted))));
        var provider = new GridMapDataProvider(new("http://127.0.0.1:59344"),http);
        try
        {
            using var form = new MainForm(provider, null);
            var state = State();
            form.ApplyGridFrame(provider.Capture(state with { Position = null, ReceivedTelemetry = null, Navigation = state.Navigation with { Phase = "Ready", Moves = 0 } }));
            Assert.True(Button(form,"gridStartButton").Enabled);
            Assert.Equal("미확인",Label(form,"alarmsValue").Text);
            Assert.Contains("제공하지 않습니다",Label(form,"alarmSummary").Text);
            var map = (MapCanvas)form.Controls.Find("mapCanvas", true).Single();
            map.Size = new Size(450,300);
            Assert.Null(map.MarkerPoint("AMR_001"));
            form.ApplyGridFrame(provider.Capture(state));
            Assert.False(Button(form,"gridStartButton").Enabled);
            Assert.Contains("X 0 m / Y 0 m", form.SelectedDetails);
            var start = map.GridCellBounds(0,0)!.Value;
            var marker = map.MarkerPoint("AMR_001")!.Value;
            Assert.InRange(Math.Abs(marker.X-start.Left-start.Width/2),0,.01);
            Assert.InRange(Math.Abs(marker.Y-start.Top-start.Height/2),0,.01);
            GridCellEdit? edit = null;
            map.GridObstacleRequested += (_, value) => edit = value;
            PointF Center(RectangleF box) => new(box.Left+box.Width/2,box.Top+box.Height/2);
            Assert.False(map.TryEditGrid(Center(start)));
            Assert.False(map.TryEditGrid(Center(map.GridCellBounds(1,0)!.Value)));
            Assert.True(map.TryEditGrid(Center(map.GridCellBounds(0,2)!.Value)));
            Assert.Equal(new GridCellEdit(0,2,true),edit);
            form.ApplyGridFrame(provider.Capture(state with { Navigation = state.Navigation with { Phase = "WaitingForTelemetry" } }));
            Assert.False(Button(form,"gridStartButton").Enabled);
            form.ApplyGridFrame(provider.Capture(state with { Navigation = state.Navigation with { Phase = "Completed" }, BrokerState = "Closed" }));
            Assert.True(Button(form,"gridStartButton").Enabled);
            Assert.Contains("도착 · 수신 확인", Label(form,"gridDetails").Text);
            form.ApplyGridFrame(new(provider.Capture(state).Change,state,"서버 응답 없음 · 마지막 수신값"));
            Assert.False(Button(form,"gridStartButton").Enabled);
            Assert.Contains("마지막 수신값", form.SelectedDetails);
            using var bitmap = new Bitmap(map.Width,map.Height);
            map.DrawToBitmap(bitmap,map.ClientRectangle);
        }
        finally { provider.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    });

    private static Button Button(MainForm form,string name) => (Button)form.Controls.Find(name,true).Single();
    private static Label Label(MainForm form,string name) => (Label)form.Controls.Find(name,true).Single();
    internal static GridMapState State()
    {
        var now = DateTimeOffset.UtcNow;
        return new(new("Running", new(0,0), new(1,2), new(0,1), [[0,0,0],[1,0,0]],
            [[false,false,false],[true,false,false]], [new(0,1),new(0,2),new(1,2)], [new(0,0),new(0,1)],
            2,1,1,[new(0,2),new(1,2)],0,new(0,2),[]), "mqtt-grid-control-receiver",new(0,0),"AMR_001",
            new("AMR_001",1,new(0,0),1,"BUSY",now),new("AMR_001",1,now),[new(0,0)],1,1,0,1,"Subscribed",null,"127.0.0.1:1884",["GridControlReceived"]);
    }
    private static HttpResponseMessage Response(GridMapState state) => new(HttpStatusCode.OK) {
        Content = new StringContent(JsonSerializer.Serialize(state,new JsonSerializerOptions(JsonSerializerDefaults.Web)))
    };
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) => send(request,cancellationToken); }
    private static async Task OnSta(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try {action();done.SetResult();} catch(Exception error){done.SetException(error);} });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground=true; thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
