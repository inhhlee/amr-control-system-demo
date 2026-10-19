using Amr.Communication.ConsoleApp;
using MQTTnet;

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
    return await ConsoleRunner.RunAsync(args, () => new MqttClientFactory().CreateMqttClient(),
        shutdown.Token, Console.Out, Console.Error);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
