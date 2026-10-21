param([ValidatePattern('^[a-z0-9-]+$')][string]$Attempt = 'sandbox', [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location $repo
$project = 'tests/Amr.Simulator.Tests/Amr.Simulator.Tests.csproj'
function Invoke-Recorded([string]$Name, [string[]]$Arguments) {
    $log = Join-Path $PSScriptRoot "$Attempt-$Name.log"
    if (Test-Path -LiteralPath $log) { throw "이미 존재하는 실행 기록: $log" }
    $start = [DateTimeOffset]::Now.ToOffset([TimeSpan]::FromHours(9))
    & dotnet @Arguments *> $log
    $code = $LASTEXITCODE
    [ordered]@{
        Command = @('dotnet') + $Arguments
        StartedKst = $start.ToString('o')
        FinishedKst = [DateTimeOffset]::Now.ToOffset([TimeSpan]::FromHours(9)).ToString('o')
        ExitCode = $code
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot "$Attempt-$Name.json") -Encoding utf8
    Get-Content -LiteralPath $log -Tail 12
    return $code
}
$files = @(rg --files src/Amr.Simulator src/Amr.Communication tests/Amr.Simulator.Tests)
$files += @('global.json', 'config/simulator.single.json')
$manifest = foreach ($file in ($files | Sort-Object -Unique)) {
    [ordered]@{ Path = $file; SHA256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot "$Attempt-source-sha256.json") -Encoding utf8
[ordered]@{
    Branch = (git branch --show-current)
    Head = (git rev-parse HEAD)
    BaseDevelop = (git rev-parse origin/develop)
    PriorMovementCommit = '71160f412624f829f8b5673c409da5a4accf202c'
    MovementSourceChangesSincePriorCommit = @(git diff --name-only 71160f4 HEAD -- src/Amr.Simulator/Simulation/SimulatedAmr.cs tests/Amr.Simulator.Tests/MovementTests.cs)
    OS = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    Sdk = (& dotnet --version)
    Runtime = @(& dotnet --list-runtimes)
    InitialStatus = @(git status --short)
    Scope = '#33 movement verification; existing #34/MQTT code preserved; no broker, GUI, commit, push, PR mutation'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot "$Attempt-environment.json") -Encoding utf8
if (-not $SkipBuild) {
    $build = @(Invoke-Recorded 'build' @('build', $project, '--no-restore', '-m:1', '-nr:false', '-p:UseSharedCompilation=false'))
    $build | Write-Output
    if ($build[-1] -ne 0) { exit $build[-1] }
}
$tests = @(Invoke-Recorded 'tests' @('test', $project, '--no-build', '--no-restore', '--logger', "trx;LogFileName=$Attempt-tests.trx", '--results-directory', $PSScriptRoot))
$tests | Write-Output
if ($tests[-1] -ne 0) { exit $tests[-1] }
[xml]$trx = Get-Content -LiteralPath (Join-Path $PSScriptRoot "$Attempt-tests.trx") -Raw
$results = @($trx.TestRun.Results.UnitTestResult)
[ordered]@{
    Counters = $trx.TestRun.ResultSummary.Counters
    Groups = @($results | Group-Object { if ($_.testName -match '^Amr\.Simulator\.Tests\.([^.]+)\.') { $Matches[1] } else { $_.testName } } | ForEach-Object { [ordered]@{ Name=$_.Name; Count=$_.Count; Passed=@($_.Group | Where-Object outcome -eq 'Passed').Count } })
    Cases = @($results | ForEach-Object { [ordered]@{ Name=$_.testName; Outcome=$_.outcome } })
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $PSScriptRoot "$Attempt-test-summary.json") -Encoding utf8
