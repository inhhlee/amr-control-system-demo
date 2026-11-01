namespace Amr.ControlGui;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length != 0)
        {
            if (args.Length != 2 || args[0] != "--grid-url" || !Uri.TryCreate(args[1], UriKind.Absolute, out var endpoint))
                throw new ArgumentException("Usage: Amr.ControlGui [--grid-url http://127.0.0.1:<port>]");
            var grid = new GridMapDataProvider(endpoint);
            try
            {
                using var liveForm = new MainForm(grid, null);
                Application.Run(liveForm);
            }
            finally { grid.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            return;
        }
        // 실행 조립 지점: 인자가 없으면 최신 Figma 피드백용 Mock 화면을 실행한다.
        // 위 --grid-url 분기는 기존 격자/MQTT 공급자다. 아래 수치는 Mock 개발 설정이다.
        var provider = new MockAmrDataProvider(subscriptionCapacity: 64, commandCapacity: 128, feedbackDemo: true);
        try
        {
            using var form = new MainForm(provider, new MockCommandDispatcher(provider),
                provider.AdvanceDeviceAsync, provider.CompleteCommandAsync);
            Application.Run(form);
        }
        finally
        {
            // 폼은 자기 구독만 정리한다. 공유 공급자의 소유자는 시작 지점이다.
            provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
