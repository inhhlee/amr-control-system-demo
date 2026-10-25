using System.Collections.Immutable;
using System.Text.Json;
using Amr.Core;
using Amr.ControlGui;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string output = args.Single();
        Directory.CreateDirectory(output);
        using var form = new MainForm();
        form.Show();
        Application.DoEvents(); // Process OnShown before testing an explicit resize.
        form.Size = form.MinimumSize;
        var devices = ImmutableArray.Create(
            new AmrSnapshot("AMR_001", 0, 0, 0, AmrState.IDLE, ConnectionObservation.Unknown, LivenessObservation.Unknown, null, null, 0),
            new AmrSnapshot("AMR_002", 6, 2, 1, AmrState.BUSY, ConnectionObservation.Unknown, LivenessObservation.Unknown, null, null, 1));
        var command = new CommandSnapshot("AMR_002", "MOCK-review-45", CommandKind.MOVE, 10, 5,
            DateTimeOffset.UtcNow, PublishObservation.MockOnly, CommandResult.Success, false, null,
            "Mock 성공 시나리오", null, null, false, 2);
        form.ApplyChange(new(2, AmrChangeKind.InitialSnapshot, devices, [], [command]));
        form.PerformLayout();
        Application.DoEvents();
        var grid = (DataGridView)form.Controls.Find("commandGrid", true).Single();
        var details = (Label)form.Controls.Find("detailsLabel", true).Single();
        var rowBounds = grid.GetRowDisplayRectangle(0, true);
        int verticalScrollHeight = grid.Controls.OfType<HScrollBar>().Where(x => x.Visible).Sum(x => x.Height);
        int availableRowHeight = grid.ClientSize.Height - grid.ColumnHeadersHeight - verticalScrollHeight;
        bool fullResultRowVisible = availableRowHeight >= grid.Rows[0].Height && rowBounds.Height >= grid.Rows[0].Height;
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, "minimum-size.png"));
        }
        var result = new
        {
            CodeVersion = "67c6d2829e256ab3d8ef428a4479cc59cc524734",
            Scenario = "Show form at its declared minimum size with one successful Mock command",
            Expected = "At least one full command result row is visible",
            Window = new { form.Width, form.Height, form.DeviceDpi },
            CommandGrid = new { ClientHeight = grid.ClientSize.Height, HeaderHeight = grid.ColumnHeadersHeight,
                HorizontalScrollbarHeight = verticalScrollHeight, RequiredRowHeight = grid.Rows[0].Height,
                AvailableRowHeight = availableRowHeight, DisplayedRowHeight = rowBounds.Height,
                FullResultRowVisible = fullResultRowVisible },
            SelectedDetails = new { Height = details.Height, PreferredTextHeight = details.GetPreferredSize(new Size(details.Width, 0)).Height },
            Result = fullResultRowVisible ? "PASS" : "FAIL: the command result is clipped at the supported minimum window size",
            ExecutedAt = DateTimeOffset.Now
        };
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "verification.json"), json);
        Console.WriteLine(json);
        form.Close();
        return fullResultRowVisible ? 0 : 1;
    }
}
