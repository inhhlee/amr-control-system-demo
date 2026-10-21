using System.Collections.Concurrent;
using System.Text.Json;
using Amr.Simulator.Tests;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

// 동일한 xUnit 엔진을 현재 프로세스에서 실행한다. VSTest 소켓/어댑터는 사용하지 않는다.
if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: RunSimulatorTests <result.json>");
    return 1;
}
var sink = new ResultsSink();
using var framework = new XunitTestFramework(sink);
using var executor = framework.GetExecutor(typeof(ConfigurationTests).Assembly.GetName());
var options = new LocalOptions();
executor.RunAll(sink, options, options);
var summary = await sink.Completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
bool passed = summary.TestsRun > 0 && summary.TestsFailed == 0 && summary.TestsSkipped == 0
    && sink.Tests.Count == summary.TestsRun && sink.Errors.IsEmpty;
var result = new
{
    Runner = "xUnit 2.9.3 in-process engine (not VSTest)",
    TestsRun = summary.TestsRun,
    TestsFailed = summary.TestsFailed,
    TestsSkipped = summary.TestsSkipped,
    Passed = passed,
    Tests = sink.Tests.ToArray(),
    Errors = sink.Errors.ToArray()
};
await File.WriteAllTextAsync(args[0], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"xUnitInProcess Total={summary.TestsRun} Failed={summary.TestsFailed} Skipped={summary.TestsSkipped} Passed={passed}");
return passed ? 0 : 1;

sealed class LocalOptions : ITestFrameworkDiscoveryOptions, ITestFrameworkExecutionOptions
{
    private readonly Dictionary<string, object?> _values = new();
    public T GetValue<T>(string name) => _values.TryGetValue(name, out var value) ? (T)value! : default!;
    public void SetValue<T>(string name, T value) => _values[name] = value;
}

sealed class ResultsSink : LongLivedMarshalByRefObject, IMessageSink
{
    public TaskCompletionSource<ITestAssemblyFinished> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ConcurrentQueue<object> Tests { get; } = new();
    public ConcurrentQueue<string> Errors { get; } = new();

    public bool OnMessage(IMessageSinkMessage message)
    {
        if (message is ITestPassed passed)
            Tests.Enqueue(new { Name = passed.Test.DisplayName, Outcome = "Passed" });
        if (message is ITestFailed failed)
            Tests.Enqueue(new { Name = failed.Test.DisplayName, Outcome = "Failed", failed.Messages, failed.StackTraces });
        if (message is ITestSkipped skipped)
            Tests.Enqueue(new { Name = skipped.Test.DisplayName, Outcome = "Skipped", skipped.Reason });
        if (message is IErrorMessage error)
            foreach (var detail in error.Messages) Errors.Enqueue(detail);
        if (message is ITestAssemblyFinished finished)
            Completion.TrySetResult(finished);
        return true;
    }
}
