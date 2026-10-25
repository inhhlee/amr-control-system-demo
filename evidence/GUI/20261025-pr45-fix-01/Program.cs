using System.Text.Json;
using Amr.ControlGui;

namespace Pr45Fix;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string output = args.Single();
        Directory.CreateDirectory(output);
        var observations = new List<object>();
        string? failure = null;
        var provider = new MockAmrDataProvider(64, 128);
        using var form = new MainForm(provider, new MockCommandDispatcher(provider),
            provider.AdvanceDeviceAsync, provider.CompleteCommandAsync);
        T Find<T>(string name) where T : Control => (T)form.Controls.Find(name, true).Single();
        var grid = Find<DataGridView>("commandGrid");
        async Task Until(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!condition()) await Task.Delay(10, timeout.Token);
        }
        void Capture(string filename)
        {
            var scrollbar = grid.Controls.OfType<HScrollBar>().Single(x => x.Visible);
            int available = grid.ClientSize.Height - grid.ColumnHeadersHeight - scrollbar.Height;
            if (available < grid.Rows[0].Height) throw new InvalidOperationException("Command row is clipped");
            foreach (string name in new[] { "targetX", "targetY", "submitButton", "advanceButton", "completeSuccessButton", "completeFailureButton", "submissionLabel", "commandGrid" })
            {
                for (Control child = Find<Control>(name); child.Parent is not null; child = child.Parent)
                    if (!child.Parent.ClientRectangle.Contains(child.Bounds)) throw new InvalidOperationException(name + " is clipped");
            }
            observations.Add(new { filename, form.Width, form.Height, form.DeviceDpi,
                GridHeight = grid.ClientSize.Height, HeaderHeight = grid.ColumnHeadersHeight,
                HorizontalScrollbarHeight = scrollbar.Height, AvailableRowHeight = available,
                RequiredRowHeight = grid.Rows[0].Height, Summary = Find<Label>("alarmsDetail").Text,
                Results = grid.Rows.Cast<DataGridViewRow>().Select(x => x.Cells["Result"].Value).ToArray() });
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, filename));
        }
        form.Shown += async (_, _) =>
        {
            try
            {
                await Task.Yield();
                await Until(() => Find<DataGridView>("deviceGrid").Rows.Count == 2);
                Find<DataGridView>("deviceGrid").CurrentCell = Find<DataGridView>("deviceGrid").Rows[1].Cells[0];
                Find<Button>("advanceButton").PerformClick();
                await Until(() => form.SelectedDetails.Contains("X 6 m") && Find<Button>("submitButton").Enabled);
                Find<Button>("submitButton").PerformClick();
                await Until(() => grid.Rows.Count == 1 && Find<Button>("completeSuccessButton").Enabled);
                Find<Button>("completeSuccessButton").PerformClick();
                await Until(() => grid.Rows[0].Cells["Result"].Value as string == "성공 (Mock)");
                form.Size = form.MinimumSize;
                await Task.Yield();
                Capture("01-minimum-success.png");
                Find<Button>("submitButton").PerformClick();
                await Until(() => grid.Rows.Count == 2 && Find<Button>("completeFailureButton").Enabled);
                Find<Button>("completeFailureButton").PerformClick();
                await Until(() => grid.Rows[0].Cells["Result"].Value as string == "실패 (Mock)");
                Capture("02-minimum-failure.png");
                form.Size = new Size(1200, 900);
                await Task.Yield();
                Capture("03-expanded.png");
                form.Size = form.MinimumSize;
                await Task.Yield();
                Capture("04-minimum-again.png");
            }
            catch (Exception error) { failure = error.ToString(); }
            finally { form.Close(); }
        };
        try { Application.Run(form); }
        finally { provider.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        var result = new { BaseCommit = "67c6d2829e256ab3d8ef428a4479cc59cc524734",
            WorkingChanges = "See source-manifest.json for the tested source hashes", ExecutedAt = DateTimeOffset.Now,
            Result = failure is null ? "PASS" : "FAIL", Failure = failure, Closed = form.IsDisposed, Observations = observations };
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "ui-verification.json"), json);
        Console.WriteLine(json);
        return failure is null && form.IsDisposed ? 0 : 1;
    }
}
