$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-Location -LiteralPath (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path

function Invoke-DotnetCheck {
    param([string]$Name, [string[]]$Arguments)
    $started = [DateTimeOffset]::UtcNow
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $PSScriptRoot "$Name.log")
    $code = $LASTEXITCODE
    [pscustomobject]@{name=$Name;command='dotnet';arguments=$Arguments;startedUtc=$started;finishedUtc=[DateTimeOffset]::UtcNow;exitCode=$code} |
        ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $PSScriptRoot 'commands.jsonl')
    if ($code -ne 0) { throw "$Name failed: $code" }
}

# Restore succeeded in 20261011-a3-shutdown-01; project/package files are unchanged.
Invoke-DotnetCheck 'build' @('build', 'AmrControlSystem.slnx', '--no-restore', '--verbosity', 'minimal')
Invoke-DotnetCheck 'tests' @('test', 'tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj', '--no-build', '--logger', 'trx;LogFileName=tests.trx', '--results-directory', $PSScriptRoot)
