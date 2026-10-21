using Amr.Simulator.Configuration;
using Amr.Simulator.Simulation;
using Amr.Simulator.Telemetry;

namespace Amr.Simulator;

public static class SimulatorApplication
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default, ISimulationClock? clock = null)
    {
        if (args is ["--help"])
        {
            await output.WriteLineAsync("Usage: Amr.Simulator --config <JSON file> [--mqtt --host <host> --port <port> --telemetry-ms <ms> --heartbeat-ms <ms>]");
            return 0;
        }
        string config;
        SimulatorMqttOptions? mqtt;
        try { (config, mqtt) = SimulatorMqttOptions.Parse(args); }
        catch (ArgumentException)
        {
            await error.WriteLineAsync("ConfigurationRejected Field=arguments Reason=--config <JSON file> 옵션을 지정하세요.");
            return 1;
        }

        try
        {
            var settings = await SimulatorSettingsLoader.LoadAsync(config, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var amr = new SimulatedAmr(settings);
            await output.WriteLineAsync(FormattableString.Invariant(
                $"ConfigurationLoaded AmrId={settings.AmrId} InitialX={settings.InitialPosition.X:R} InitialY={settings.InitialPosition.Y:R} SpeedMps={settings.SpeedMps:R} UpdateIntervalMs={settings.UpdateIntervalMs} WaypointCount={settings.Waypoints.Count}"));
            for (int index = 0; index < settings.Waypoints.Count; index++)
            {
                var point = settings.Waypoints[index];
                await output.WriteLineAsync(FormattableString.Invariant($"Waypoint Index={index} X={point.X:R} Y={point.Y:R}"));
            }
            await output.WriteLineAsync(FormattableString.Invariant(
                $"AmrInitialized AmrId={amr.Id} X={amr.Position.X:R} Y={amr.Position.Y:R} State={amr.State} CurrentSpeedMps={amr.CurrentSpeedMps:R}"));
            await output.WriteLineAsync("InitializationCompleted");
            if (mqtt is not null)
                return await MqttSimulationSession.RunAsync(amr, mqtt, new MqttSimulationTransport(mqtt.Connection),
                    output, error, cancellationToken, clock);
            await new SimulationRunner(clock).RunAsync(amr, output, cancellationToken);
            return 0;
        }
        catch (ConfigurationException exception)
        {
            await error.WriteLineAsync($"ConfigurationRejected Field={exception.Field} Reason={exception.Reason}");
            return 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("InitializationCanceled");
            return 0;
        }
        catch (Exception exception)
        {
            await error.WriteLineAsync($"SimulationFailed Reason={exception.GetType().Name}");
            return 1;
        }
    }
}
