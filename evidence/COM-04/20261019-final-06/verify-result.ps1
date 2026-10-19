# 새 제품 시험이 아니라 실행 03/05의 증거 대조와 실패한 Broker 로그 수집을 완료한다.
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repository
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'verification.json')) { throw 'Preserve existing verification; use a new execution ID.' }
$mqttRun = Join-Path $PSScriptRoot '../20261019-mqtt-05'
$testRun = Join-Path $PSScriptRoot '../20261019-integration-03'
$scenario = Get-Content -Raw -LiteralPath (Join-Path $mqttRun 'scenario-result.json') | ConvertFrom-Json -DateKind String
$since = [DateTimeOffset]::Parse($scenario.startedAtUtc).ToUniversalTime().ToString('O')
$dockerCli = (Get-Command docker -ErrorAction Stop).Source
& $dockerCli compose -f docker/compose.yaml logs --no-color --timestamps --since $since mosquitto *> (Join-Path $PSScriptRoot 'broker.log')
if ($LASTEXITCODE -ne 0) { throw 'Broker log collection failed.' }
$brokerLog = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'broker.log')
if ($brokerLog -notmatch ([regex]::Escape($scenario.clientId) + '(?: \[[^\]]+\])? disconnected\.')) { throw 'Broker disconnect record missing.' }
$consoleLog = Get-Content -Raw -LiteralPath (Join-Path $mqttRun 'console.stdout.log')
if ($scenario.result -ne 'passed' -or $scenario.exitCode -ne 0) { throw 'MQTT scenario did not pass.' }
if ([regex]::Matches($consoleLog, ' Received Heartbeat ').Count -ne 2 -or [regex]::Matches($consoleLog, ' Rejected ').Count -ne 2) { throw 'Delivery counts differ.' }
if ($consoleLog -notmatch '(?s)MissingField.*Received Heartbeat.*IdMismatch.*Received Heartbeat.*ShutdownRequested.*ExitCode=0') { throw 'Processing order differs.' }
foreach ($manifestPath in @((Join-Path $mqttRun 'source-version.json'), (Join-Path $testRun 'source-version.json'))) {
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    foreach ($file in $manifest.files) {
        if ((Get-FileHash -LiteralPath $file.path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Tested source changed: $($file.path)" }
    }
}
[xml]$trx = Get-Content -Raw -LiteralPath (Join-Path $testRun 'tests.trx')
$counters = $trx.TestRun.ResultSummary.Counters
if ($counters.total -ne 113 -or $counters.passed -ne 113 -or $counters.failed -ne 0) { throw 'Automated test counts differ.' }
[ordered]@{ result='passed'; verificationCompletedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');
    kind='evidence verification and Broker log collection; no product test rerun';
    automatedTestRun='20261019-integration-03'; automatedPassed=113; mqttRun='20261019-mqtt-05';
    mqttPublications=4; rejections=2; deliveredModels=2; consoleExitCode=0;
    receiverTimeAndFieldsPreserved=$scenario.receptionTimePreserved; rejectedPayloadHidden=$scenario.rejectedPayloadHidden;
    brokerDisconnectConfirmed=$true; clientId=$scenario.clientId; currentSourceMatchesBothTestManifests=$true;
    brokerLogSinceUtc=$since } | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $PSScriptRoot 'verification.json')
Write-Output 'PASS: 113 automatic tests, MQTT 2 rejections/2 deliveries, actual exit 0, Broker disconnect, matching current source.'
