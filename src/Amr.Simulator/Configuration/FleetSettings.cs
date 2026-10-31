namespace Amr.Simulator.Configuration;

/// <summary>장비 수는 명시한 항목 수다. ID를 자동 생성하거나 설정을 복제하지 않는다.</summary>
public sealed class FleetSettings(IEnumerable<ConfiguredAmr> amrs)
{
    public IReadOnlyList<ConfiguredAmr> Amrs { get; } = Array.AsReadOnly(amrs.ToArray());
}

public sealed record ConfiguredAmr(SimulatorSettings Settings, IReadOnlyList<FaultScenario> Faults);

/// <summary>실행 후 경과 ms로 지정한 재현 입력. 원인 해소와 알람 해제는 별개다.</summary>
public sealed record FaultScenario(string Code, string Level, long ActivateAfterMs, long ResolveAfterMs,
    string? Message);
