param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repository

function Invoke-Recorded {
    param([string]$Name, [string]$Executable, [string[]]$Arguments, [int]$ExpectedExitCode = 0)

    $started = [DateTimeOffset]::UtcNow
    & $Executable @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $PSScriptRoot "$Name.log")
    $code = $LASTEXITCODE
    [pscustomobject]@{
        name = $Name
        executable = $Executable
        arguments = $Arguments
        startedUtc = $started
        finishedUtc = [DateTimeOffset]::UtcNow
        expectedExitCode = $ExpectedExitCode
        exitCode = $code
    } | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $PSScriptRoot 'commands.jsonl')
    if ($code -ne $ExpectedExitCode) {
        throw "$Name returned $code; expected $ExpectedExitCode."
    }
}

$startedUtc = [DateTimeOffset]::UtcNow.ToString('O')
Invoke-Recorded 'dotnet-info' 'dotnet' @('--info')
Invoke-Recorded 'docker-version' 'docker' @('version')
Invoke-Recorded 'compose-version' 'docker' @('compose', 'version')
Invoke-Recorded 'compose-config' 'docker' @('compose', '-f', 'docker/compose.yaml', 'config', '--quiet')
Invoke-Recorded 'broker-status-before' 'docker' @('compose', '-f', 'docker/compose.yaml', 'ps', '-a', '--format', 'json')
Invoke-Recorded 'broker-loaded-file' 'docker' @('compose', '-f', 'docker/compose.yaml', 'exec', '-T', 'mosquitto', 'cat', '/mosquitto/config/mosquitto.conf')

$container = docker inspect amr-demo-mosquitto-1 | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Broker inspect failed.' }
[pscustomobject]@{
    id = $container[0].Id
    startedAt = $container[0].State.StartedAt
    restartCount = $container[0].RestartCount
    image = $container[0].Config.Image
    mounts = $container[0].Mounts
    ports = $container[0].NetworkSettings.Ports
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'broker-inspect.json')

$runArguments = @('run', '--project', 'src/Amr.Communication.Console', '--no-build', '--', '--host', '127.0.0.1', '--port', '1884', '--connect-timeout-seconds', '3')
Invoke-Recorded 'console-01' 'dotnet' $runArguments
Invoke-Recorded 'console-02' 'dotnet' $runArguments

# 연결 실패를 성공으로 출력하지 않는 최소 확인. 상세 실패·취소 시험은 #5에서 한다.
$listeners = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
if (@($listeners | Where-Object Port -EQ 1).Count -ne 0) { throw 'Port 1 is in use; do not use it as the negative test.' }
Invoke-Recorded 'console-refused' 'dotnet' @('run', '--project', 'src/Amr.Communication.Console', '--no-build', '--', '--host', '127.0.0.1', '--port', '1', '--connect-timeout-seconds', '3') 1

Invoke-Recorded 'broker-logs' 'docker' @('compose', '-f', 'docker/compose.yaml', 'logs', '--no-color', '--timestamps', '--since', $startedUtc, 'mosquitto')
Invoke-Recorded 'broker-status-after' 'docker' @('compose', '-f', 'docker/compose.yaml', 'ps', '-a', '--format', 'json')

$brokerLog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'broker-logs.log') -Raw
$clientIds = @()
foreach ($name in @('console-01', 'console-02')) {
    $log = Get-Content -LiteralPath (Join-Path $PSScriptRoot "$name.log") -Raw
    $clientId = [regex]::Match($log, 'ClientId=(\S+)').Groups[1].Value
    if (!$clientId) { throw "$name has no Client ID." }
    $clientIds += $clientId
    $states = [regex]::Matches($log, '(?m)^\S+ (Connecting|Connected|Disconnecting|Disconnected|Disposed|ExitCode=0)\r?$') | ForEach-Object { $_.Groups[1].Value }
    if (($states -join ',') -ne 'Connecting,Connected,Disconnecting,Disconnected,Disposed,ExitCode=0') { throw "$name state order mismatch." }
    if ($brokerLog -notmatch "as $([regex]::Escape($clientId)) \(p5, c1, k5\)" -or $brokerLog -notmatch "Client $([regex]::Escape($clientId)) disconnected\.") {
        throw "$name is missing corresponding Broker connection/disconnection evidence."
    }
}
if ($clientIds[0] -eq $clientIds[1]) { throw 'Client IDs were reused.' }
$failureLog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'console-refused.log') -Raw
if ($failureLog -match '(?m)^\S+ Connected\r?$' -or $failureLog -notmatch 'Disposed' -or $failureLog -notmatch 'ExitCode=1') { throw 'Failure cleanup/output mismatch.' }

[pscustomobject]@{
    startedUtc = $startedUtc
    finishedUtc = [DateTimeOffset]::UtcNow
    normalRuns = 2
    clientIds = $clientIds
    normalExitCodes = @(0, 0)
    brokerLogMatches = $true
    negativeInput = '127.0.0.1:1; no TCP listener before execution'
    negativeExitCode = 1
    negativeConnectedOutput = $false
    negativeDisposedOutput = $true
    result = 'passed'
    scope = 'A2 immediate disconnect; A3 and unit tests not executed'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json')
