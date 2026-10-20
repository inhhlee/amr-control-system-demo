$ErrorActionPreference = 'Stop'
$reviewDirectory = $PSScriptRoot
$results = @()
foreach ($tool in @('Verify-KeepAlive.ps1','Verify-BrokerShutdown.ps1','Verify-ResponseGate.ps1')) {
    $source = Join-Path $PSScriptRoot "../../../tests/manual/COM03-C/$tool"
    $tokens = $null; $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile((Resolve-Path $source), [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { throw 'Tool parse failure' }
    $main = @($ast.EndBlock.Statements | Where-Object {
        $_ -is [Management.Automation.Language.TryStatementAst] -and $_.Finally
    })
    if ($main.Count -ne 1) { throw 'Main try/finally not uniquely identified' }
    $cleanup = @($main[0].Finally.Statements | Where-Object {
        $_ -is [Management.Automation.Language.TryStatementAst]
    })
    if ($cleanup.Count -ne 1) { throw 'Cleanup try/catch not uniquely identified' }
    $run = Join-Path $reviewDirectory ([IO.Path]::GetFileNameWithoutExtension($tool) + '-cleanup')
    New-Item -ItemType Directory -Path $run | Out-Null
    $container = 'a' * 64
    $name = 'review-stub-only'
    $failure = $null
    $calls = [Collections.Generic.List[string]]::new()
    function Invoke-Docker([string[]]$Arguments) {
        $calls.Add(($Arguments -join ' '))
        if ($Arguments[0] -eq 'logs') { throw [IO.IOException]::new('Injected diagnostic log retrieval failure') }
        return @()
    }
    # Execute the exact PR cleanup try/catch with only the Docker boundary replaced.
    # No container, process or Docker service is created/stopped by this reproduction.
    & ([ScriptBlock]::Create($cleanup[0].Extent.Text))
    $results += [ordered]@{
        tool = $tool
        sourceCleanupStartLine = $cleanup[0].Extent.StartLineNumber
        calls = @($calls)
        stopAttempted = @($calls | Where-Object { $_ -like 'stop *' }).Count -gt 0
        removeAttempted = @($calls | Where-Object { $_ -like 'rm *' }).Count -gt 0
        cleanupFailureRecorded = Test-Path -LiteralPath (Join-Path $run 'cleanup-failure.log')
        DockerWasActuallyInvoked = $false
    }
}
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $reviewDirectory 'cleanup-reproduction.json') -Encoding utf8
Get-Content -Raw (Join-Path $reviewDirectory 'cleanup-reproduction.json')
