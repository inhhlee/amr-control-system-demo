namespace Amr.ControlGui;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
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
