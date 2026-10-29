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
        // 첫 Mock 실행본의 명시적 개발 설정. 실제 공급자의 운영값이 아니다.
        var provider = new MockAmrDataProvider(subscriptionCapacity: 64, commandCapacity: 128);
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
