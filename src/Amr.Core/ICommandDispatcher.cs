namespace Amr.Core;

// 화면의 명령 입력 접점. 현재 Mock 실행창의 시작/Stop은 각각 MOVE/STOP 요청으로 전달된다.
public interface ICommandDispatcher
{
    // 접수 결과만 반환한다. 접수 후의 결과는 DataProvider에서 같은 대상/ID로 조회한다.
    Task<CommandSubmission> DispatchAsync(CommandRequest request, CancellationToken cancellationToken = default);
}
