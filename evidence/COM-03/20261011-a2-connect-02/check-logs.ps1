param()
$ErrorActionPreference = 'Stop'
$brokerLog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'broker-logs.log') -Raw
$clientIds = @()
foreach ($name in @('console-01', 'console-02')) {
    $log = Get-Content -LiteralPath (Join-Path $PSScriptRoot "$name.log") -Raw
    $clientId = [regex]::Match($log, 'ClientId=(\S+)').Groups[1].Value
    if (!$clientId) { throw "$name has no Client ID." }
    $clientIds += $clientId
    $states = [regex]::Matches($log, '(?m)^\S+ (Connecting|Connected|Disconnecting|Disconnected|Disposed|ExitCode=0)\r?$') | ForEach-Object { $_.Groups[1].Value }
    if (($states -join ',') -ne 'Connecting,Connected,Disconnecting,Disconnected,Disposed,ExitCode=0') { throw "$name state order mismatch." }
    $idPattern = [regex]::Escape($clientId)
    if ($brokerLog -notmatch "as $idPattern \(p5, c1, k5\)" -or $brokerLog -notmatch "Client $idPattern(?: \[[^\]]+\])? disconnected\.") {
        throw "$name is missing corresponding Broker connection/disconnection evidence."
    }
}
if ($clientIds[0] -eq $clientIds[1]) { throw 'Client IDs were reused.' }
$failureLog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'console-refused.log') -Raw
if ($failureLog -match '(?m)^\S+ Connected\r?$' -or $failureLog -notmatch 'Disposed' -or $failureLog -notmatch 'ExitCode=1') { throw 'Failure cleanup/output mismatch.' }
$commands = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'commands.jsonl') | ForEach-Object { $_ | ConvertFrom-Json }
foreach ($command in $commands) {
    $expected = if ($command.name -eq 'console-refused') { 1 } else { 0 }
    if ($command.exitCode -ne $expected) { throw 'Recorded process exit code mismatch.' }
}
[pscustomobject]@{
    checkedUtc = [DateTimeOffset]::UtcNow
    normalRuns = 2
    clientIds = $clientIds
    normalExitCodes = @(0, 0)
    brokerLogMatches = $true
    negativeInput = '127.0.0.1:1; no TCP listener before execution (verified by verify-initial.ps1)'
    negativeExitCode = 1
    negativeConnectedOutput = $false
    negativeDisposedOutput = $true
    result = 'passed'
    scope = 'Existing A2 logs rechecked; no new MQTT execution, no A3 or unit tests'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json')
Write-Output 'Saved A2 console/Broker logs and process exit codes match the expected results.'
