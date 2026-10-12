$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repository
$started = [DateTimeOffset]::UtcNow
$results = @()
foreach ($number in 1..2) {
    $argsForChild = @('-NoProfile','-File',(Join-Path $PSScriptRoot 'verify-mqtt.ps1'),'-Run',"$number")
    $child = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList $argsForChild -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput (Join-Path $PSScriptRoot "harness-$number.stdout.log") -RedirectStandardError (Join-Path $PSScriptRoot "harness-$number.stderr.log") -PassThru
    $null = $child.Handle
    if (!$child.WaitForExit(45000)) { $child.Kill(); throw 'Test controller timed out.' }
    $child.WaitForExit()
    $childExit = $child.ExitCode
    $child.Dispose()
    if ($childExit -ne 0) { throw "Test controller $number failed: $childExit" }
    if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot "completed-$number.txt"))) { throw 'Completion marker absent.' }
    $result = Get-Content -LiteralPath (Join-Path $PSScriptRoot "mqtt-result-$number.json") -Raw | ConvertFrom-Json
    if ($result.exitCode -ne 0) { throw 'Console failed.' }
    $results += $result
}
if ($results[0].clientId -eq $results[1].clientId) { throw 'Client ID reused.' }
docker compose -f docker/compose.yaml logs --no-color --timestamps --since $started.ToString('O') mosquitto *> (Join-Path $PSScriptRoot 'broker.log')
if ($LASTEXITCODE -ne 0) { throw 'Broker log collection failed.' }
$brokerLog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'broker.log') -Raw
foreach ($result in $results) {
    if ($brokerLog -notmatch ([regex]::Escape($result.clientId) + '(?: \[[^\]]+\])? disconnected\.')) { throw 'Broker disconnect evidence missing.' }
}
[pscustomobject]@{startedUtc=$started;finishedUtc=[DateTimeOffset]::UtcNow;result='passed';runs=$results} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json')
Write-Output 'PASS: two independent sessions, three received Heartbeats, three excluded topics, Ctrl+C exit 0 twice.'
