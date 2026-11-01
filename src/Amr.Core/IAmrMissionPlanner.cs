namespace Amr.Core;

// 목적지 계획만 변경한다. AMR의 실제 좌표를 변경하는 명령이 아니다.
public interface IAmrMissionPlanner
{
    Task SetDestinationAsync(string amrId, MapStation destination, CancellationToken cancellationToken = default);
}
