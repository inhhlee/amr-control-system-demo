using Amr.Communication.ConsoleApp.Logging;
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
var seq = SeqLogging.FromEnvironment(Console.Error);
try
{
    return await ConsoleRunner.RunAsync(args, () => new MqttClientFactory().CreateMqttClient(),
        shutdown.Token, Console.Out, Console.Error, seq?.Log);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
    if (seq is not null) await seq.StopAsync(Console.Error);
}
