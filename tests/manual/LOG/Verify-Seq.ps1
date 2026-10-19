param([Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunId)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repository
$run = Join-Path $repository "evidence/LOG/$RunId"
if (Test-Path -LiteralPath $run) { throw 'Execution ID exists.' }
New-Item -ItemType Directory -Path $run | Out-Null
$previousUrl = $env:AMR_SEQ_URL
$previousRun = $env:AMR_LOG_RUN_ID
$socket = $null
try {
    $env:AMR_SEQ_URL = 'http://127.0.0.1:5341'
    $env:AMR_LOG_RUN_ID = "$RunId-online"
    $started = [DateTimeOffset]::UtcNow
    $api = Invoke-RestMethod "$env:AMR_SEQ_URL/api" -TimeoutSec 5
    [ordered]@{baseCommit=(git rev-parse HEAD);branch=(git branch --show-current);
        startedAtUtc=$started.ToString('O');sdk=(dotnet --version);seqVersion=$api.Version;
        seqUrl=$env:AMR_SEQ_URL;sourceState='uncommitted #26; source hashes in MQTT evidence';
        serviceStartedOrStoppedByThisScript=$false} | ConvertTo-Json | Set-Content "$run/environment.json" -Encoding utf8
    & pwsh -NoProfile -File tests/manual/COM01-B/Verify-Mqtt.ps1 -RunId "$RunId-online" *> "$run/mqtt-online.log"
    if ($LASTEXITCODE -ne 0) { throw 'Online MQTT regression failed.' }
    $filter = [uri]::EscapeDataString("RunId = '$env:AMR_LOG_RUN_ID'")
    $events = Invoke-RestMethod "$env:AMR_SEQ_URL/api/events?filter=$filter&count=1000&render=true" -TimeoutSec 5
    $events | ConvertTo-Json -Depth 12 | Set-Content "$run/seq-events.json" -Encoding utf8
    $properties = @($events | ForEach-Object {
        $row = @{}; foreach ($property in $_.Properties) { $row[$property.Name] = $property.Value }; $row
    })
    $received = @($properties | Where-Object { $_.EventType -eq 'Received' })
    $rejected = @($properties | Where-Object { $_.EventType -eq 'Rejected' })
    $exits = @($properties | Where-Object { $_.EventType -eq 'ExitCode' -and $_.ExitCode -eq 0 })
    if ($received.Count -ne 20 -or $rejected.Count -ne 4 -or $exits.Count -ne 2) { throw 'Seq count or final flush mismatch.' }
    if (@($received | Where-Object { !$_.AmrId -or !$_.Kind }).Count) { throw 'Missing structured AMR properties.' }
    $amrFilter = [uri]::EscapeDataString("RunId = '$env:AMR_LOG_RUN_ID' and AmrId = 'AMR_001' and EventType = 'Received'")
    $amrEvents = Invoke-RestMethod "$env:AMR_SEQ_URL/api/events?filter=$amrFilter&count=1000&render=true" -TimeoutSec 5
    $amrEvents | ConvertTo-Json -Depth 12 | Set-Content "$run/seq-amr-query.json" -Encoding utf8
    if (@($amrEvents).Count -ne @($received | Where-Object { $_.AmrId -eq 'AMR_001' }).Count) {
        throw 'Server-side AMR/event property filter mismatch.'
    }
    foreach ($row in $properties) {
        foreach ($forbidden in @('Payload','PayloadPreview','Model','ApiKey','Exception')) {
            if ($row.ContainsKey($forbidden)) { throw 'Unexpected raw or secret property.' }
        }
    }
    # Reserve an unused local port without listening. This never stops the shared Seq server.
    $socket = [Net.Sockets.Socket]::new([Net.Sockets.AddressFamily]::InterNetwork,
        [Net.Sockets.SocketType]::Stream,[Net.Sockets.ProtocolType]::Tcp)
    $socket.ExclusiveAddressUse = $true
    $socket.Bind([Net.IPEndPoint]::new([Net.IPAddress]::Loopback,0))
    $env:AMR_SEQ_URL = "http://127.0.0.1:$($socket.LocalEndPoint.Port)"
    $env:AMR_LOG_RUN_ID = "$RunId-offline"
    & pwsh -NoProfile -File tests/manual/COM01-B/Verify-Mqtt.ps1 -RunId "$RunId-offline" -SeqUnavailable *> "$run/mqtt-offline.log"
    if ($LASTEXITCODE -ne 0) { throw 'Unavailable Seq MQTT regression failed.' }
    [ordered]@{result='passed';completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');seqEvents=$events.Count;
        seqReceived=$received.Count;seqRejected=$rejected.Count;seqExitCodeZero=$exits.Count;
        amrIds=@($received.AmrId | Sort-Object -Unique);kinds=@($received.Kind | Sort-Object -Unique);
        actualMqttOnline="evidence/COM-01/$RunId-online/verification.json";
        actualMqttUnavailableSeq="evidence/COM-01/$RunId-offline/verification.json";
        unavailableEndpoint=$env:AMR_SEQ_URL;sharedSeqStopped=$false} |
        ConvertTo-Json -Depth 5 | Set-Content "$run/verification.json" -Encoding utf8
    Write-Output "PASS: real Seq property query/final flush and real MQTT with Seq available/unavailable. $run"
} catch {
    # Failure text only concerns the synthetic test; API keys are never written by this script.
    $_ | Out-String | Set-Content "$run/failure.log" -Encoding utf8
    throw
} finally {
    if ($null -ne $socket) { $socket.Dispose() }
    $env:AMR_SEQ_URL = $previousUrl
    $env:AMR_LOG_RUN_ID = $previousRun
}
