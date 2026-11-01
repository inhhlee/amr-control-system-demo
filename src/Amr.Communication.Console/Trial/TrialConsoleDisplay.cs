using System.Globalization;
using System.Text;

namespace Amr.Communication.ConsoleApp;

internal sealed class TrialConsoleDisplay(TextWriter output, bool color, TimeProvider? clock = null,
    bool interactive = false, Func<int>? columns = null)
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private bool _reading;
    private string _input = "";
    private int _cursor, _renderedLength;

    public void Header(TrialConsoleOptions options)
    {
        Line("안내", "과제 1 · MQTT 통신 시험 프로그램", "36");
        Line("설정", $"Broker={options.Connection.Host}:{options.Connection.Port} AMR={options.AmrId}");
        Line("설정", $"재시도: {options.Reconnect.Delay.TotalSeconds}초 간격 / 최대 {options.Reconnect.MaxAttempts}회 (최초 시도 제외)");
        Line("안내", "입력: move 10 20 | stop | reset | status | detail on/off | help | /exit  /  Ctrl+C 종료");
        if (interactive) Line("안내", "위쪽은 수신 로그, 마지막 trial> 줄은 명령 입력입니다. ←/→·Home/End·Backspace/Delete로 수정, Esc로 비우기.");
        Line("안내", "명령 발행과 응답 수신을 관찰합니다. AMR 실제 수행·요청/응답 업무 매핑은 별도입니다.");
    }

    public void Line(string kind, string message, string? ansiColor = null)
    {
        lock (_gate)
        {
            if (_reading) ClearInput();
            string prefix = $"[{_clock.GetLocalNow():HH:mm:ss}] [{kind}] ";
            output.WriteLine(color && ansiColor is not null
                ? $"\u001b[{ansiColor}m{prefix}{Safe(message)}\u001b[0m" : prefix + Safe(message));
            if (_reading) DrawInput();
            output.Flush();
        }
    }

    public void BeginInput()
    {
        if (!interactive) return;
        lock (_gate) { _reading = true; _input = ""; _cursor = 0; DrawInput(); output.Flush(); }
    }

    public void UpdateInput(string text, int cursor)
    {
        if (!interactive) return;
        lock (_gate)
        {
            if (!_reading) return;
            ClearInput(); _input = text; _cursor = cursor; DrawInput(); output.Flush();
        }
    }

    public void EndInput(bool submitted)
    {
        if (!interactive) return;
        lock (_gate)
        {
            if (!_reading) return;
            ClearInput(); _reading = false;
            if (submitted) output.WriteLine("trial> " + _input);
            _input = ""; _cursor = 0; output.Flush();
        }
    }

    private int Columns()
    {
        try { int width = columns?.Invoke() ?? Console.WindowWidth; return width <= 0 ? 80 : Math.Clamp(width, 3, 4096); }
        catch (Exception error) when (error is IOException or PlatformNotSupportedException) { return 80; }
    }

    // CR/공백/Backspace만 사용한다. 터미널 모드 변경이나 ANSI 커서 제어는 필요 없다.
    // 로그와 키 입력이 같은 잠금을 사용하므로 글자 단위 출력도 서로 끼어들지 않는다.
    private void ClearInput()
    {
        output.Write('\r');
        output.Write(new string(' ', Math.Min(_renderedLength, Columns() - 1)));
        output.Write('\r');
    }

    private void DrawInput()
    {
        int width = Columns();
        string prefix = width < 12 ? "> " : "trial> ";
        int length = Math.Max(0, width - prefix.Length - 1); // 마지막 열은 자동 줄바꿈 방지용이다.
        int start = Math.Clamp(_cursor - length + 1, 0, Math.Max(0, _input.Length - length));
        string visible = _input.Substring(start, Math.Min(length, _input.Length - start));
        output.Write(prefix + visible);
        int back = visible.Length - (_cursor - start);
        if (back > 0) output.Write(new string('\b', back));
        _renderedLength = prefix.Length + visible.Length;
    }

    private static string Safe(string text)
    {
        var result = new StringBuilder();
        foreach (char c in text.Take(1200))
            if (char.IsControl(c)) result.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else result.Append(c);
        if (text.Length > 1200) result.Append("… (일부 표시)");
        return result.ToString();
    }
}
