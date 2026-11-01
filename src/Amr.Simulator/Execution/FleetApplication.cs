using Amr.Simulator.Configuration;
using Amr.Simulator.Simulation;
using Amr.Simulator.Telemetry;
using System.Text.Json;

namespace Amr.Simulator;

public static class FleetApplication
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        CancellationToken token = default)
    {
        try
        {
            var normalized = args.Select(a => a == "--fleet-config" ? "--config" : a).ToArray();
            var (path, mqtt) = SimulatorMqttOptions.Parse(normalized);
            // 마지막 장비의 오류도 모든 생성/네트워크 작업 시작 전에 발견한다.
            var fleet = await FleetSettingsLoader.LoadAsync(path, token);
            return await RunAsync(fleet, mqtt, output, error, token);
        }
        catch (ArgumentException)
        {
            await error.WriteLineAsync("ConfigurationRejected Field=arguments Reason=--fleet-config <JSON file> 및 MQTT 옵션을 확인하세요.");
            return 1;
        }
        catch (ConfigurationException e)
        {
            await error.WriteLineAsync($"ConfigurationRejected Field={e.Field} Reason={e.Reason}");
            return 1;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await output.WriteLineAsync("FleetCanceled");
            return 0;
        }
        catch (Exception e)
        {
            await error.WriteLineAsync($"FleetFailed Reason={e.GetType().Name}");
            return 1;
        }
    }

    public static async Task<int> RunAsync(FleetSettings fleet, SimulatorMqttOptions? mqtt,
        TextWriter output, TextWriter error, CancellationToken token = default,
        Func<ConfiguredAmr, ISimulationClock>? clockFactory = null,
        Func<ConfiguredAmr, ISimulationTransport>? transportFactory = null)
    {
        output = TextWriter.Synchronized(output);
        error = TextWriter.Synchronized(error);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var tasks = new List<Task<int>>();
        stop.Token.ThrowIfCancellationRequested();
        if (mqtt is not null)
            foreach (var configured in fleet.Amrs)
            {
                var snapshot = SimulationSnapshot.Capture(new SimulatedAmr(configured.Settings));
                _ = SimulationMessages.Create(snapshot, false, 0);
                _ = SimulationMessages.Create(snapshot, true, 0);
            }
        await output.WriteLineAsync($"FleetConfigurationLoaded AmrCount={fleet.Amrs.Count}");
        foreach (var configured in fleet.Amrs)
        {
            var settings = configured.Settings;
            await output.WriteLineAsync(FormattableString.Invariant(
                $"FleetAmrConfigured AmrId={settings.AmrId} InitialX={settings.InitialPosition.X:R} InitialY={settings.InitialPosition.Y:R} SpeedMps={settings.SpeedMps:R} FaultCount={configured.Faults.Count}"));
            foreach (var fault in configured.Faults)
                await output.WriteLineAsync($"FaultScenarioConfigured AmrId={settings.AmrId} Code={JsonSerializer.Serialize(fault.Code)} ActivateAfterMs={fault.ActivateAfterMs} ResolveAfterMs={fault.ResolveAfterMs} Execution=PendingAlarmImplementation");
        }
        foreach (var configured in fleet.Amrs) tasks.Add(RunAmrAsync(configured));
        int[] results = await Task.WhenAll(tasks);
        int code = results.Any(r => r != 0) ? 1 : 0;
        await output.WriteLineAsync($"FleetEnded ExitCode={code}");
        return code;

        async Task<int> RunAmrAsync(ConfiguredAmr configured)
        {
            try
            {
                stop.Token.ThrowIfCancellationRequested();
                var amr = new SimulatedAmr(configured.Settings);
                var clock = clockFactory?.Invoke(configured);
                if (mqtt is null)
                {
                    await new SimulationRunner(clock).RunAsync(amr, output, stop.Token);
                    return 0;
                }
                // 각 연결에는 별도 설정 인스턴스가 생성한 고유 ClientId를 사용한다.
                var options = mqtt with { Connection = new(mqtt.Connection.Host, mqtt.Connection.Port) };
                var transport = transportFactory?.Invoke(configured) ?? new MqttSimulationTransport(options.Connection);
                int result = await MqttSimulationSession.RunAsync(amr, options, transport, output, error, stop.Token, clock);
                if (result != 0) stop.Cancel();
                return result;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
            catch (Exception e)
            {
                await error.WriteLineAsync($"FleetAmrFailed AmrId={configured.Settings.AmrId} Reason={e.GetType().Name}");
                stop.Cancel();
                return 1;
            }
        }
    }
}
