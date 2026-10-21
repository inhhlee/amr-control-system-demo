namespace Amr.Simulator.Configuration;

/// <summary>사용자 설정 오류의 위치와 이유. 정상 초기화 결과와 구분해 출력한다.</summary>
public sealed class ConfigurationException(string field, string reason) : Exception(reason)
{
    public string Field { get; } = field;
    public string Reason { get; } = reason;
}
