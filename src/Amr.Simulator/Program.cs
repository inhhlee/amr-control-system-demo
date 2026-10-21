using Amr.Simulator;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);
using var shutdown = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};
Console.CancelKeyPress += cancelHandler;
try
{
    return await SimulatorApplication.RunAsync(args, Console.Out, Console.Error, shutdown.Token);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
