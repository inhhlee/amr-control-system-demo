using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Amr.Communication.ConsoleApp;

/// <summary>한 입력 소유자만 읽는다. Console.In.ReadLineAsync의 동기 대기로 종료를 막지 않는다.</summary>
internal sealed class TrialConsoleInput : IDisposable
{
    private readonly TrialConsoleDisplay _display;
    private readonly bool _redirected = Console.IsInputRedirected;
    private readonly Stream? _stream;
    private readonly nint _handle;
    private readonly bool _pipe;
    private readonly TrialConsoleLineEditor _line = new();
    private bool _ended;

    public TrialConsoleInput(TrialConsoleDisplay display)
    {
        _display = display;
        if (!_redirected) return;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Redirected trial input is supported on Windows.");
        _handle = GetStdHandle(-10);
        uint type = GetFileType(_handle);
        if (type is not (1 or 3)) throw new IOException("Standard input must be a console, file or pipe.");
        _pipe = type == 3;
        _stream = Console.OpenStandardInput();
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken token)
    {
        bool submitted = false;
        if (!_redirected) _display.BeginInput();
        try
        {
            while (!_ended)
            {
                token.ThrowIfCancellationRequested();
                int value;
                if (_redirected)
                {
                    if (_pipe)
                    {
                        if (!PeekNamedPipe(_handle, 0, 0, 0, out uint available, 0))
                        {
                            int error = Marshal.GetLastWin32Error();
                            if (error is 109 or 232) { _ended = true; break; }
                            throw new IOException("Cannot inspect standard input.", new Win32Exception(error));
                        }
                        if (available == 0) { await Task.Delay(40, token); continue; }
                    }
                    // 이 입력 소유자 외에는 같은 핸들을 읽지 않는다. 확인된 바이트만 동기적으로 읽는다.
                    value = _stream!.ReadByte();
                    if (value == -1) { _ended = true; break; }
                    if (value == '\r') continue;
                }
                else
                {
                    if (!Console.KeyAvailable) { await Task.Delay(40, token); continue; }
                    var key = Console.ReadKey(intercept: true);
                    if (_line.Apply(key)) { submitted = true; return _line.Finish(); }
                    _display.UpdateInput(_line.Text, _line.Cursor);
                    continue;
                }
                if (value == '\n') return _line.Finish();
                _line.Insert(value);
            }
            return _line.HasInput ? _line.Finish() : null;
        }
        finally { if (!_redirected) _display.EndInput(submitted); }
    }

    public void Dispose() => _stream?.Dispose();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GetStdHandle(int number);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekNamedPipe(nint pipe, nint buffer, uint size, nint read, out uint available, nint left);
}

// 기존처럼 ASCII 128자까지 입력받는다. 화면의 일부 표시와 실제 발행 내용은 구분한다.
internal sealed class TrialConsoleLineEditor
{
    private readonly StringBuilder _text = new();
    private bool _invalid;
    public string Text => _text.ToString();
    public int Cursor { get; private set; }
    public bool HasInput => _text.Length != 0 || _invalid;

    public bool Apply(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Enter: return true;
            case ConsoleKey.LeftArrow: Cursor = Math.Max(0, Cursor - 1); break;
            case ConsoleKey.RightArrow: Cursor = Math.Min(_text.Length, Cursor + 1); break;
            case ConsoleKey.Home: Cursor = 0; break;
            case ConsoleKey.End: Cursor = _text.Length; break;
            case ConsoleKey.Backspace: if (Cursor > 0) _text.Remove(--Cursor, 1); break;
            case ConsoleKey.Delete: if (Cursor < _text.Length) _text.Remove(Cursor, 1); break;
            case ConsoleKey.Escape: _text.Clear(); Cursor = 0; _invalid = false; break;
            default: Insert(key.KeyChar); break;
        }
        return false;
    }

    public void Insert(int value)
    {
        if (value >= 32 && value <= 126 && _text.Length < 128) _text.Insert(Cursor++, (char)value);
        else if (value != 0) _invalid = true;
    }

    public string Finish()
    {
        string result = _invalid ? "<invalid-input>" : Text;
        _text.Clear(); Cursor = 0; _invalid = false;
        return result;
    }
}
