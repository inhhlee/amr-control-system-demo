using System.Text;
using Amr.Communication.ConsoleApp;
using Xunit;

namespace Amr.Communication.Tests;

public sealed class TrialConsolePromptTests
{
    [Fact]
    public void IncomingLogsPreservePartialCommandAndItsEditingCursor()
    {
        var terminal = new Terminal(60);
        var display = new TrialConsoleDisplay(terminal, false, interactive: true, columns: () => terminal.Width);
        var editor = new TrialConsoleLineEditor();
        display.BeginInput();
        Type(editor, "move 100 2");
        editor.Apply(Key(ConsoleKey.LeftArrow)); editor.Apply(Key(ConsoleKey.LeftArrow));
        editor.Apply(Key(ConsoleKey.Backspace));
        display.UpdateInput(editor.Text, editor.Cursor);
        for (int i = 0; i < 100; i++) display.Line("RX", $"packet-{i}");
        Assert.Equal("trial> move 10 2", terminal.CurrentLine.TrimEnd());
        Assert.Equal(7 + editor.Cursor, terminal.Column);
        Assert.Equal(100, terminal.Lines.Count(line => line.Contains("[RX]")));
        Assert.True(editor.Apply(Key(ConsoleKey.Enter)));
        Assert.Equal("move 10 2", editor.Finish());
        display.EndInput(true);
        display.Line("SEND", "single request");
        Assert.Contains("trial> move 10 2", terminal.Lines.Select(x => x.TrimEnd()));
        Assert.Single(terminal.Lines, line => line.Contains("[SEND]"));
    }

    [Fact]
    public void NarrowScreenShowsOneInputRowWithoutTruncatingSubmittedCommand()
    {
        var terminal = new Terminal(24);
        var display = new TrialConsoleDisplay(terminal, false, interactive: true, columns: () => terminal.Width);
        var editor = new TrialConsoleLineEditor();
        string command = "move " + new string('1', 30) + " 2";
        display.BeginInput(); Type(editor, command); display.UpdateInput(editor.Text, editor.Cursor);
        display.Line("RX", "data");
        Assert.StartsWith("trial> ", terminal.CurrentLine);
        Assert.EndsWith(" 2", terminal.CurrentLine.TrimEnd());
        Assert.True(terminal.Column < terminal.Width);
        editor.Apply(Key(ConsoleKey.Home)); display.UpdateInput(editor.Text, editor.Cursor);
        Assert.StartsWith("trial> move ", terminal.CurrentLine);
        Assert.Equal(7, terminal.Column);
        Assert.Equal(command, editor.Finish());
    }

    [Fact]
    public void EditingSupportsInsertionDeleteAndEscapeWithoutLosingCommand()
    {
        var editor = new TrialConsoleLineEditor(); Type(editor, "move 100 2");
        editor.Apply(Key(ConsoleKey.Home));
        for (int i = 0; i < 7; i++) editor.Apply(Key(ConsoleKey.RightArrow));
        editor.Apply(Key(ConsoleKey.Delete));
        Assert.Equal("move 10 2", editor.Text);
        editor.Insert('5');
        Assert.Equal("move 105 2", editor.Text);
        editor.Apply(Key(ConsoleKey.End)); Assert.Equal(editor.Text.Length, editor.Cursor);
        editor.Apply(Key(ConsoleKey.Escape)); Assert.False(editor.HasInput); Assert.Equal(0, editor.Cursor);
        Type(editor, "stop"); Assert.Equal("stop", editor.Finish());
    }

    [Fact]
    public void OversizeOrNonAsciiInputIsRejectedUntilEnterOrEscape()
    {
        var editor = new TrialConsoleLineEditor();
        Type(editor, new string('x', 128)); Assert.Equal(128, editor.Finish().Length);
        Type(editor, new string('x', 129)); Assert.Equal("<invalid-input>", editor.Finish());
        editor.Insert('한'); Assert.Equal("<invalid-input>", editor.Finish());
        Type(editor, new string('x', 129)); editor.Apply(Key(ConsoleKey.Escape));
        Type(editor, "status"); Assert.Equal("status", editor.Finish());
    }

    [Fact]
    public async Task ConcurrentLogAndKeyRenderingRemainWholeAndRetainFinalInput()
    {
        var terminal = new Terminal(80);
        var display = new TrialConsoleDisplay(terminal, false, interactive: true, columns: () => terminal.Width);
        display.BeginInput();
        await Task.WhenAll(Task.Run(() => { for (int i = 0; i < 200; i++) display.Line("RX", $"packet-{i}"); }),
            Task.Run(() => { for (int i = 0; i < 200; i++) display.UpdateInput("move 10 2", i % 10); }));
        display.UpdateInput("stop", 4);
        Assert.Equal("trial> stop", terminal.CurrentLine.TrimEnd());
        Assert.Equal(200, terminal.Lines.Count(line => line.Contains("[RX] packet-")));
        display.EndInput(false); display.Line("END", "cancelled");
        Assert.DoesNotContain(terminal.Lines, line => line.Contains("trial> stop"));
    }

    [Fact]
    public void RedirectedOutputKeepsPlainLogsWithoutPromptOrCursorControls()
    {
        var output = new StringWriter(); var display = new TrialConsoleDisplay(output, false);
        display.BeginInput(); display.UpdateInput("move 10 2", 9); display.Line("RX", "plain"); display.EndInput(true);
        string result = output.ToString();
        Assert.Contains("[RX] plain", result); Assert.DoesNotContain("trial>", result);
        Assert.DoesNotContain('\b', result); Assert.DoesNotContain('\u001b', result);
        Assert.Single(result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
    private static void Type(TrialConsoleLineEditor editor, string value) { foreach (char ch in value) editor.Insert(ch); }

    // CR/BS·덮어쓰기·줄바꿈을 해석해 실제 화면에 남는 입력줄과 커서를 검사한다.
    private sealed class Terminal(int width) : TextWriter
    {
        private readonly List<StringBuilder> _lines = [new()];
        public int Width => width;
        public int Column { get; private set; }
        public string CurrentLine => _lines[^1].ToString();
        public IEnumerable<string> Lines => _lines.Select(x => x.ToString());
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value)
        {
            if (value == '\r') { Column = 0; return; }
            if (value == '\b') { Column = Math.Max(0, Column - 1); return; }
            if (value == '\n') { _lines.Add(new()); Column = 0; return; }
            if (Column == Width) { _lines.Add(new()); Column = 0; }
            var row = _lines[^1]; while (row.Length <= Column) row.Append(' '); row[Column++] = value;
        }
        public override void Write(string? value) { foreach (char ch in value ?? "") Write(ch); }
        public override void WriteLine(string? value) { Write(value); Write('\r'); Write('\n'); }
    }
}
