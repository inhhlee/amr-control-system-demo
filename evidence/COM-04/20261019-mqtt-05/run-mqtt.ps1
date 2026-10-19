$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repository
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'controller.stdout.log')) {
    throw 'Existing evidence must be preserved. Use a new execution ID.'
}
$controller = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList @('-NoProfile','-File',(Join-Path $PSScriptRoot 'verify-mqtt.ps1')) `
    -WorkingDirectory $repository -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $PSScriptRoot 'controller.stdout.log') `
    -RedirectStandardError (Join-Path $PSScriptRoot 'controller.stderr.log') -PassThru
$null = $controller.Handle
try {
    if (!$controller.WaitForExit(55000)) { throw 'COM04 MQTT test controller timed out.' }
    $controller.WaitForExit()
    [ordered]@{ command='pwsh -NoProfile -File verify-mqtt.ps1'; exitCode=$controller.ExitCode;
        completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O') } | ConvertTo-Json |
        Set-Content -Encoding utf8 (Join-Path $PSScriptRoot 'controller-result.json')
    if ($controller.ExitCode -ne 0) { throw 'MQTT scenario failed; inspect controller logs.' }
    $scenario = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'scenario-result.json') | ConvertFrom-Json
    if ($scenario.result -ne 'passed' -or $scenario.exitCode -ne 0) { throw 'Scenario result did not pass.' }
    $dockerCli = (Get-Command docker).Source
    & $dockerCli compose -f docker/compose.yaml logs --no-color --timestamps --since $scenario.startedAtUtc mosquitto *> (Join-Path $PSScriptRoot 'broker.log')
    if ($LASTEXITCODE -ne 0) { throw 'Broker log collection failed.' }
    $brokerLog = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'broker.log')
    if ($brokerLog -notmatch ([regex]::Escape($scenario.clientId) + '(?: \[[^\]]+\])? disconnected\.')) { throw 'Broker disconnect record missing.' }
    $scenario | Add-Member -NotePropertyName brokerDisconnectConfirmed -NotePropertyValue $true
    $scenario | Add-Member -NotePropertyName verificationCompletedAtUtc -NotePropertyValue ([DateTimeOffset]::UtcNow.ToString('O'))
    $scenario | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $PSScriptRoot 'verification.json')
    Write-Output 'PASS: MQTT scenario, actual console exit 0 and matching Broker disconnect log.'
}
finally {
    if (!$controller.HasExited) { $controller.Kill(); $controller.WaitForExit() }
    $controller.Dispose()
}
