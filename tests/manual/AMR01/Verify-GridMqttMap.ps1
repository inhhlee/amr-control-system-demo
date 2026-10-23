param(
 [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,48}$')][string]$SessionRunId,
 [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,48}$')][string]$RunId
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$root = Join-Path $repo "evidence/AMR/$RunId"
if (Test-Path -LiteralPath $root) { throw 'Use a new RunId.' }
New-Item -ItemType Directory -Path $root | Out-Null
$session = Get-Content -LiteralPath (Join-Path $repo "evidence/AMR/$SessionRunId/session.json") -Raw | ConvertFrom-Json
$url = $session.Url
$docker = (Get-Command docker -ErrorAction Stop).Source
$headers = @{ 'X-Simulator-Map' = '1' }
$results = [Collections.Generic.List[object]]::new()
$samples = [Collections.Generic.List[object]]::new()
function Require([bool]$value, [string]$message) { if (!$value) { throw $message } }
function State {
 $state = Invoke-RestMethod "$url/api/grid/state" -TimeoutSec 5
 Require ($state.source -eq 'mqtt-grid-control-receiver') 'Grid is not using control-received MQTT coordinates.'
 if ($null -ne $state.position) {
  $t = $state.receivedTelemetry
  Require ($null -ne $t -and $state.position.row -eq -$t.position.y -and $state.position.col -eq $t.position.x) 'Displayed cell does not match received Telemetry.'
 } else { Require ($null -eq $state.receivedTelemetry) 'Received model was not projected.' }
 $samples.Add(@{phase=$state.navigation.phase;position=$state.position;telemetry=$state.receivedTelemetry;moves=$state.navigation.moves})
 return $state
}
function Post([string]$action, $body = $null) {
 $parameters = @{Uri="$url/api/grid/$action";Method='Post';Headers=$headers;TimeoutSec=5}
 if ($null -ne $body) { $parameters.ContentType='application/json';$parameters.Body=ConvertTo-Json $body -Compress }
 Invoke-RestMethod @parameters
}
function Wait-State([scriptblock]$condition, [int]$seconds = 15) {
 $deadline = [DateTimeOffset]::UtcNow.AddSeconds($seconds)
 do {
  $state = State
  if (& $condition $state) { return $state }
  Start-Sleep -Milliseconds 50
 } while ([DateTimeOffset]::UtcNow -lt $deadline)
 throw "Timeout: $($state.navigation.phase), $($state.failure)"
}
function Save([string]$name, $actual) {
 $actual | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath (Join-Path $root "$name.json") -Encoding utf8
 $results.Add(@{name=$name;verdict='passed'})
}
function Reset-Grid {
 Post reset | Out-Null
 $state = Wait-State {param($s) $s.navigation.phase -eq 'Ready'}
 Require ($null -eq $state.position -and $state.telemetryCount -eq 0) 'Reset leaked old received coordinates.'
}
function Complete([string]$name, [int]$moves) {
 $state = Wait-State {param($s) $s.navigation.phase -eq 'Completed'}
 Require ($state.position.row -eq 4 -and $state.position.col -eq 5 -and $state.navigation.moves -eq $moves) 'Wrong arrival or movement count.'
 Require ($state.receivedTelemetry.state -eq 'IDLE' -and $state.receivedTelemetry.speed -eq 0 -and $state.receivedTelemetry.timestamp -eq $state.lastPublishedTimestamp) 'Arrival is missing matching final stopped Telemetry.'
 Require ($state.navigation.nextWaypointIndex -eq 3 -and $state.navigation.reachedWaypoints.Count -eq 3) 'Not every ordered waypoint was visited.'
 Require (($state.navigation.reachedWaypoints.index -join ',') -eq '0,1,2') 'Waypoint order changed.'
 Require ($state.receivedTelemetry.id -eq $state.amrId) 'A different AMR supplied the final position.'
 Save $name $state
 return $state
}
function Inject([string]$topic, [string]$payload) {
 & $docker exec $session.ContainerId mosquitto_pub -h 127.0.0.1 -p 1883 -t $topic -m $payload
 if ($LASTEXITCODE -ne 0) { throw 'Fixture publication failed.' }
}
try {
 $owner = & $docker inspect $session.ContainerId --format '{{index .Config.Labels "amr.map.run"}}'
 Require ($LASTEXITCODE -eq 0 -and $owner -eq $SessionRunId) 'Only an owned isolated Broker may be tested/stopped.'
 $initial = State
 Require ($initial.navigation.phase -eq 'Ready' -and $null -eq $initial.position) 'Initial map must have no received AMR marker.'
 Save 'initial-no-position' $initial
 $alias = Invoke-RestMethod "$url/api/state" -TimeoutSec 5
 Require ($alias.amrId -eq $initial.amrId -and $alias.source -eq $initial.source) 'Old API and grid API expose different AMRs.'
 $html = (Invoke-WebRequest "$url/" -TimeoutSec 5).Content
 Require ($html -match 'grid-waypoints' -and $html -notmatch 'waypoint-demo|src="/app.js"|mode-nav') 'The server still exposes two separate movement screens.'
 Post start | Out-Null
 $aliasOverlap = Invoke-WebRequest "$url/api/start" -Method Post -Headers $headers -SkipHttpErrorCheck
 Require ($aliasOverlap.StatusCode -eq 409) 'Old API started a second AMR.'
 Save 'single-session-api-and-screen' @{amrId=$alias.amrId;overlapStatus=409;unifiedPage=$true}
 $overlap = Invoke-WebRequest "$url/api/grid/start" -Method Post -Headers $headers -SkipHttpErrorCheck
 Require ($overlap.StatusCode -eq 409) 'Overlapping run was accepted.'
 $normal = Complete 'normal-arrival' 9
 Require ($normal.receivedTrail.Count -eq 10) 'A moved cell was not received.'

 Reset-Grid
 Post start | Out-Null
 $moving = Wait-State {param($s) $s.navigation.moves -ge 1}
 Require ($moving.navigation.moves -lt 4) 'Fixture was too late to block (2,2).'
 Post obstacle @{row=2;col=2;blocked=$true} | Out-Null
 $crossedLater = Wait-State {param($s) $s.navigation.current.row -eq 4 -and $s.navigation.current.col -eq 3 -and $s.navigation.nextWaypointIndex -eq 0}
 Require ($crossedLater.navigation.reachedWaypoints.Count -eq 0) 'Crossing W2 skipped W1.'
 Save 'later-waypoint-crossed-not-credited' $crossedLater
 $detour = Complete 'moving-detour' 15
 Require (@($detour.receivedTrail | Where-Object {$_.row -eq 2 -and $_.col -eq 2}).Count -eq 0) 'Control received a path through the obstacle.'
 Require ($detour.receivedTrail.Count -eq 16) 'Detour cell reception missing.'
 Require (($detour.navigation.reachedWaypoints.moves -join ',') -eq '11,13,15') 'Detour did not revisit W2 after W1.'

 Reset-Grid
 Post obstacle @{row=4;col=3;blocked=$true} | Out-Null
 Post start | Out-Null
 $targetBlocked = Wait-State {param($s) $s.navigation.phase -eq 'Waiting' -and $s.navigation.nextWaypointIndex -eq 1}
 Require ($targetBlocked.navigation.current.row -eq 2 -and $targetBlocked.navigation.current.col -eq 3 -and $targetBlocked.navigation.moves -eq 5) 'Future blocked waypoint prevented W1 or was skipped.'
 Start-Sleep -Milliseconds 800
 $targetStill = State
 Require ($targetStill.navigation.moves -eq 5 -and $targetStill.navigation.nextWaypointIndex -eq 1) 'Blocked W2 was bypassed in the mission order.'
 Require ($targetStill.receivedTelemetry.state -eq 'IDLE' -and $targetStill.receivedTelemetry.speed -eq 0) 'Waiting at a blocked waypoint was not sent as a stopped state.'
 Save 'blocked-next-waypoint-keeps-order' $targetStill
 Post obstacle @{row=4;col=3;blocked=$false} | Out-Null
 $targetResumed = Complete 'waypoint-unblocked-resume' 9

 Reset-Grid
 Post obstacle @{row=1;col=1;blocked=$true} | Out-Null
 Post start | Out-Null
 $waiting = Wait-State {param($s) $s.navigation.phase -eq 'Waiting' -and $s.heartbeatCount -ge 2}
 Require ($waiting.navigation.moves -eq 0 -and $waiting.telemetryCount -ge 2 -and $waiting.receivedTelemetry.speed -eq 0) 'Blocked robot did not stop or keep sending.'
 Save 'blocked-heartbeat' $waiting
 $id = $waiting.amrId
 Inject "amr/$id/telemetry" ('{"id":"'+$id+'","ts":100,"pos":{"x":999,"y":999},"sp":1,"state":"INVALID"}')
 Inject "amr/$id/telemetry" ('{"id":"'+$id+'","ts":101,"pos":{"x":0.5,"y":0},"sp":0,"state":"IDLE"}')
 Inject 'amr/OTHER_GRID_AMR/telemetry' '{"id":"OTHER_GRID_AMR","ts":100,"pos":{"x":999,"y":999},"sp":1,"state":"BUSY"}'
 $checked = Wait-State {param($s) $s.rejectedCount -eq 2}
 Require ($checked.position.row -eq 0 -and $checked.position.col -eq 0 -and $checked.navigation.phase -eq 'Waiting') 'Bad or other AMR data changed the marker/run.'
 Save 'invalid-isolation' $checked
 Post obstacle @{row=1;col=1;blocked=$false} | Out-Null
 $resumed = Complete 'automatic-resume' 9

 Reset-Grid
 Post start | Out-Null
 $null = Wait-State {param($s) $s.telemetryCount -ge 2}
 Post cancel | Out-Null
 $canceled = Wait-State {param($s) $s.navigation.phase -eq 'Canceled'}
 Start-Sleep -Milliseconds 800
 $after = State
 Require ($after.navigation.moves -eq $canceled.navigation.moves -and $after.publishedCount -eq $canceled.publishedCount) 'Canceled session kept moving or publishing.'
 Save 'cancel-cleanup' $after

 Reset-Grid
 Post start | Out-Null
 $null = Wait-State {param($s) $s.telemetryCount -ge 2}
 Reset-Grid
 Save 'active-reset-no-stale-position' (State)

 Post start | Out-Null
 $null = Wait-State {param($s) $s.navigation.moves -ge 1 -and $s.position.col -eq 1}
 & $docker stop --time 3 $session.ContainerId | Out-Null
 Require ($LASTEXITCODE -eq 0) 'Owned Broker stop failed.'
 $lost = Wait-State {param($s) $s.navigation.phase -eq 'Failed'}
 Require ($null -ne $lost.position -and $lost.failure -match 'Disconnected') 'Connection loss did not preserve the last received position.'
 Start-Sleep -Milliseconds 800
 $afterLoss = State
 Require ($afterLoss.navigation.moves -eq $lost.navigation.moves) 'Robot moved after disconnection.'
 Save 'broker-loss' $afterLoss

 Post start | Out-Null
 $initialFail = Wait-State {param($s) $s.navigation.phase -eq 'Failed'}
 Require ($initialFail.failure -like 'ConnectionFailed:*' -and $null -eq $initialFail.position -and $initialFail.navigation.moves -eq 0) 'Initial connection failure moved the robot or used fallback coordinates.'
 Save 'initial-connect-failure' $initialFail

 $rows = @(Get-Content -LiteralPath $normal.evidencePath | ForEach-Object { $_ | ConvertFrom-Json })
 $received = @($rows | Where-Object { $_.event -eq 'Received' -and $_.value.kind -eq 'telemetry' })
 $published = @($rows | Where-Object { $_.event -eq 'GridPublished' -and $_.kind -eq 'Telemetry' } | ForEach-Object {$_.payload | ConvertFrom-Json})
 foreach ($sample in @($samples | Where-Object {$null -ne $_.telemetry})) {
  $t = $sample.telemetry
  Require (@($received | Where-Object {$_.value.model.timestamp -eq $t.timestamp -and $_.value.model.position.x -eq $t.position.x -and $_.value.model.position.y -eq $t.position.y -and $_.value.model.state -eq $t.state -and $_.value.model.speed -eq $t.speed}).Count -gt 0) 'API coordinate missing from actual control receiver log.'
  Require (@($published | Where-Object {$_.ts -eq $t.timestamp -and $_.pos.x -eq $t.position.x -and $_.pos.y -eq $t.position.y -and $_.state -eq $t.state -and $_.sp -eq $t.speed}).Count -gt 0) 'Control coordinate missing from actual sender log.'
 }
 Save 'sender-receiver-api-equality' @{samples=$samples.Count;receivedTelemetry=$received.Count;publications=$published.Count}
}
catch { $results.Add(@{name='failure';verdict='failed';reason=$_.Exception.Message}); throw }
finally {
 $samples | ForEach-Object {$_ | ConvertTo-Json -Depth 8 -Compress} | Set-Content -LiteralPath (Join-Path $root 'samples.jsonl') -Encoding utf8
 $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'verification.json') -Encoding utf8
}
Write-Output "PASS: $($results.Count) real grid -> Broker -> validated control -> map scenarios. $root"
