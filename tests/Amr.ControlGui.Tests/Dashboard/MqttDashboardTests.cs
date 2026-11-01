using Amr.Core;
using Amr.Infrastructure;
using Xunit;

namespace Amr.ControlGui.Tests;

public sealed class MqttDashboardTests
{
    [Fact]
    public async Task ActualModeUsesLatestLayoutAndDistinguishesConnectionAndFreshness()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var provider = new MqttAmrDataProvider(new());
                try
                {
                    using var form = new MainForm(provider, null);
                    form.Show();
                    form.ApplyChange(new(1, AmrChangeKind.InitialSnapshot, [], [], [])
                    { Connection = new("127.0.0.1:1884", "AMR_001", DataConnectionState.Connecting, 0, 5, 5, 0, false) });
                    Assert.Contains("ACTUAL", Find<Label>(form, "modeLabel").Text);
                    Assert.Contains("접속 중", Find<Label>(form, "communicationValue").Text);
                    Assert.False(Find<Button>(form, "openSimulation").Enabled); // 위치 미수신·명령 공급자 미지정
                    Assert.NotNull(Find<TabControl>(form, "feedbackTabs"));
                    Assert.Equal(0, Find<DataGridView>(form, "deviceGrid").Rows.Count);
                    var device = new AmrSnapshot("AMR_001", 4, 2, 1, AmrState.BUSY, ConnectionObservation.Disconnected,
                        LivenessObservation.Unknown, null, DateTimeOffset.UtcNow, 2);
                    form.ApplyChange(new(2, AmrChangeKind.DeviceChanged, [device], [], [])
                    { Connection = new("127.0.0.1:1884", "AMR_001", DataConnectionState.ReconnectWaiting, 1, 5, 5, 0, false) });
                    Assert.Contains("재접속 대기", Find<Label>(form, "communicationValue").Text);
                    Assert.Contains("마지막 수신값", form.SelectedDetails);
                    Assert.Contains("미확인", Find<Label>(form, "operationValue").Text);
                    Assert.Contains("마지막 수신", Find<DataGridView>(form, "deviceGrid").Rows[0].Cells[4].Value!.ToString());
                    form.ApplyChange(new(3, AmrChangeKind.DeviceChanged, [device with { X = 8, BrokerConnection = ConnectionObservation.Connected }], [], [])
                    { Connection = new("127.0.0.1:1884", "AMR_001", DataConnectionState.Connected, 0, 5, 5, 1, true) });
                    Assert.Contains("서버 연결됨", Find<Label>(form, "communicationValue").Text);
                    Assert.DoesNotContain("마지막 수신값", form.SelectedDetails);
                    Assert.Contains("X 8", form.SelectedDetails);
                    Assert.Contains("형식 오류 거부 1건", Find<Label>(form, "statusLabel").Text);
                    using (var simulation = new SimulationForm(device with { State = AmrState.IDLE,
                        Journey = new(new("S", 4, 2), new("G", 8, 2), SimulationPhase.Ready, 4, 0) }, provider, provider))
                    {
                        simulation.Show(); Application.DoEvents();
                        var destination = Find<Button>(simulation, "destinationSettings");
                        Assert.True(destination.Visible);
                        Assert.True(destination.Parent!.ClientRectangle.Contains(destination.Bounds));
                        Assert.Same(destination, destination.Parent.GetChildAtPoint(new(destination.Left + destination.Width / 2, destination.Top + destination.Height / 2)));
                        simulation.Close();
                    }
                    form.Close(); Application.DoEvents();
                }
                finally { provider.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                completion.TrySetResult();
            }
            catch (Exception error) { completion.TrySetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static T Find<T>(Control form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();

    [Fact]
    public void MqttOptionsKeepTrialDefaults()
    {
        var options = MqttGuiOptions.Parse(["--mqtt"]);
        Assert.Equal("AMR_001", options.AmrId); Assert.Equal(1884, options.Connection.Port);
        Assert.Equal(5, options.Reconnect.MaxAttempts); Assert.Equal(TimeSpan.FromSeconds(5), options.Reconnect.Delay);
    }

    [Theory]
    [InlineData("--port", "0")]
    [InlineData("--port", "65536")]
    [InlineData("--amr-id", "AMR/001")]
    [InlineData("--amr-id", "")]
    [InlineData("--reconnect-attempts", "-1")]
    [InlineData("--reconnect-delay-seconds", "0")]
    [InlineData("--unknown", "1")]
    public void InvalidMqttOptionsAreRejected(string option, string value) =>
        Assert.Throws<ArgumentException>(() => MqttGuiOptions.Parse(["--mqtt", option, value]));
}
