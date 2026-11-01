using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Amr.Communication.ConsoleApp;

/// <summary>한 입력 소유자만 읽는다. Console.In.ReadLineAsync의 동기 대기로 종료를 막지 않는다.</summary>
internal sealed class TrialConsoleInput : IDisposable
{
    private readonly bool _redirected = Console.IsInputRedirected;
    private readonly Stream? _stream;
    private readonly nint _handle;
    private readonly bool _pipe;
    private readonly StringBuilder _line = new();
    private bool _tooLong, _ended;

    public TrialConsoleInput()
    {
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
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (_line.Length > 0) { _line.Length--; Console.Write("\b \b"); }
                    continue;
                }
                value = key.Key == ConsoleKey.Enter ? '\n' : key.KeyChar;
                if (value == '\n') Console.WriteLine();
                else if (!char.IsControl((char)value)) Console.Write((char)value);
            }
            if (value == '\n') return Finish();
            if (value >= 32 && value <= 126 && _line.Length < 128) _line.Append((char)value);
            else if (value != 0) _tooLong = true;
        }
        return _line.Length > 0 || _tooLong ? Finish() : null;
    }

    private string Finish()
    {
        string value = _tooLong ? "<invalid-input>" : _line.ToString();
        _line.Clear(); _tooLong = false;
        return value;
    }

    public void Dispose() => _stream?.Dispose();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GetStdHandle(int number);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekNamedPipe(nint pipe, nint buffer, uint size, nint read, out uint available, nint left);
}
