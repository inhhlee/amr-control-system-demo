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
            var (path, mqtt) = SimulatorMqttOptions.Parse(normalized, useFleetDefaults: true);
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
        Func<ConfiguredAmr, IAmrDeviceTransport>? transportFactory = null)
    {
        output = TextWriter.Synchronized(output);
        error = TextWriter.Synchronized(error);
        token.ThrowIfCancellationRequested();
        if (mqtt is null && fleet.Amrs.Any(x => x.Mode == AmrOperationMode.Command))
            throw new ConfigurationException("$.amrs.mode", "command 운전 모드는 --mqtt가 필요합니다.");
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
                $"FleetAmrConfigured AmrId={settings.AmrId} InitialX={settings.InitialPosition.X:R} InitialY={settings.InitialPosition.Y:R} SpeedMps={settings.SpeedMps:R} Mode={configured.Mode} FaultCount={configured.Faults.Count}"));
            foreach (var fault in configured.Faults)
                await output.WriteLineAsync($"FaultScenarioConfigured AmrId={settings.AmrId} Code={JsonSerializer.Serialize(fault.Code)} ActivateAfterMs={fault.ActivateAfterMs} ResolveAfterMs={fault.ResolveAfterMs} Execution=DeviceLoop");
        }
        var devices = new List<AmrDeviceContext>();
        try
        {
            // 모든 컨텍스트를 조립한 뒤 시작한다. 생성 실패 시 아직 시작하지 않은 자원도 정리한다.
            foreach (var configured in fleet.Amrs)
            {
                var options = mqtt is null ? null : mqtt with { Connection = new(mqtt.Connection.Host, mqtt.Connection.Port,
                    (int)mqtt.Connection.ConnectTimeout.TotalSeconds) };
                devices.Add(new(configured, options, output, error, clockFactory?.Invoke(configured),
                    transportFactory?.Invoke(configured)));
            }
            // 한 장비 실패는 해당 장비만 종료한다. 정상 장비는 부모 취소까지 계속 실행한다.
            int[] results = await Task.WhenAll(devices.Select(device => device.RunAsync(token)));
            int code = results.Any(r => r != 0) ? 1 : 0;
            await output.WriteLineAsync($"FleetEnded ExitCode={code}");
            return code;
        }
        finally
        {
            foreach (var device in devices) device.RequestStop();
            await Task.WhenAll(devices.Select(device => device.DisposeAsync().AsTask()));
        }
    }
}
