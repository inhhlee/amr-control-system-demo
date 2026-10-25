namespace Amr.Core;

public interface ICommandDispatcher
{
    // 접수 결과만 반환한다. 접수 후의 결과는 DataProvider에서 같은 대상/ID로 조회한다.
    Task<CommandSubmission> DispatchAsync(CommandRequest request, CancellationToken cancellationToken = default);
}
