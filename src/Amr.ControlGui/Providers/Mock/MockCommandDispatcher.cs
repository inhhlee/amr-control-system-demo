using Amr.Core;

namespace Amr.ControlGui;

public sealed class MockCommandDispatcher(MockAmrDataProvider provider) : ICommandDispatcher
{
    private readonly MockAmrDataProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    public Task<CommandSubmission> DispatchAsync(CommandRequest request, CancellationToken cancellationToken = default) =>
        _provider.SubmitAsync(request, cancellationToken);
}
