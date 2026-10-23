$ErrorActionPreference = 'Stop'
$runDir = $PSScriptRoot
$checks = [System.Collections.Generic.List[string]]::new()
function Require([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
    $checks.Add($message)
}
function CellKey($cell) { return "$($cell.row),$($cell.col)" }
function Distance($map, $start, $goal) {
    $queue = [System.Collections.Generic.Queue[object]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new()
    $queue.Enqueue(@{r=[int]$start.row; c=[int]$start.col; d=0})
    [void]$seen.Add((CellKey $start))
    while ($queue.Count -gt 0) {
        $p = $queue.Dequeue()
        if ($p.r -eq $goal.row -and $p.c -eq $goal.col) { return $p.d }
        foreach ($delta in @(@(-1,0),@(1,0),@(0,-1),@(0,1))) {
            $r = $p.r + $delta[0]; $c = $p.c + $delta[1]
            if ($r -ge 0 -and $r -lt $map.Count -and $c -ge 0 -and $c -lt $map[0].Count -and $map[$r][$c] -eq 0 -and $seen.Add("$r,$c")) {
                $queue.Enqueue(@{r=$r; c=$c; d=$p.d+1})
            }
        }
    }
    throw 'No independent BFS path found.'
}
$final = Get-Content -Raw -LiteralPath "$runDir/final.json" | ConvertFrom-Json
$events = @(Get-Content -LiteralPath "$runDir/run-events.jsonl" | ForEach-Object { $_ | ConvertFrom-Json })
$samples = @(Get-Content -LiteralPath "$runDir/api-samples.jsonl" | ForEach-Object { $_ | ConvertFrom-Json })
$dom = @(Get-Content -Raw -LiteralPath "$runDir/dom-samples.json" | ConvertFrom-Json)
$nav = $final.navigation
Require ($nav.phase -eq 'Completed' -and $final.failure -eq $null -and $final.brokerState -eq 'Closed') 'Completed only after final receipt and connection cleanup.'
Require ($final.source -eq 'mqtt-grid-control-receiver' -and $final.amrId -eq 'AMR_001') 'Actual MQTT receiver source and same AMR ID.'
$edit = @($events | Where-Object { $_.name -eq 'GridMapChanged' })
Require ($edit.Count -eq 1 -and $edit[0].state.phase -eq 'Running' -and $edit[0].state.moves -eq 1 -and $edit[0].state.map[2][2] -eq 1) 'Obstacle (2,2) added during movement after the first step.'
$visits = @($nav.reachedWaypoints)
Require (($visits.index -join ',') -eq '0,1,2' -and ($visits.moves -join ',') -eq '11,13,15') 'Ordered visits W1/W2/G at moves 11/13/15.'
$crossing = @($events | Where-Object { $_.name -eq 'GridMoved' -and $_.state.moves -eq 9 })
Require ($crossing.Count -eq 1 -and (CellKey $crossing[0].state.current) -eq '4,3' -and $crossing[0].state.nextWaypointIndex -eq 0 -and $crossing[0].state.reachedWaypoints.Count -eq 0) 'Passing W2 at move 9 does not skip W1.'
$minimum = $edit[0].state.moves
$point = $edit[0].state.current
foreach ($target in $nav.waypoints) { $minimum += Distance $nav.map $point $target; $point = $target }
Require ($minimum -eq 15 -and $nav.moves -eq $minimum -and $nav.traveled.Count -eq 16) '15 moves equal the independent BFS minimum with ordered waypoints.'
$previous = $null
foreach ($cell in $nav.traveled) {
    Require ($nav.map[$cell.row][$cell.col] -eq 0) "Trace cell $(CellKey $cell) is open."
    if ($null -ne $previous) { Require (([Math]::Abs($cell.row-$previous.row)+[Math]::Abs($cell.col-$previous.col)) -eq 1) 'Movement uses one orthogonal grid step.' }
    $previous = $cell
}
$published = @{}
foreach ($event in @($events | Where-Object { $_.event -eq 'GridPublished' })) {
    $payload = $event.payload | ConvertFrom-Json
    $published["$($event.kind.ToLowerInvariant()):$($payload.ts)"] = $payload
}
$received = @($events | Where-Object { $_.event -eq 'Received' })
$telemetry = @($received | Where-Object { $_.value.kind -eq 'telemetry' })
$heartbeat = @($received | Where-Object { $_.value.kind -eq 'heartbeat' })
Require ($telemetry.Count -eq 47 -and $heartbeat.Count -eq 10 -and $published.Count -eq 57) '47 Telemetry and 10 Heartbeat messages were actually received.'
$telemetryByStamp = @{}
foreach ($event in $received) {
    $kind = $event.value.kind
    $model = $event.value.model
    $payload = $published["${kind}:$($model.timestamp)"]
    Require ($null -ne $payload -and $payload.id -eq $model.id -and $model.id -eq 'AMR_001') 'Received timestamp and ID match a published message.'
    if ($kind -eq 'telemetry') {
        Require ($payload.pos.x -eq $model.position.x -and $payload.pos.y -eq $model.position.y -and $payload.sp -eq $model.speed -and $payload.state -eq $model.state) 'Received Telemetry coordinates, speed and state match the sent payload.'
        $telemetryByStamp[[string]$model.timestamp] = $model
    } else { Require ($payload.status -eq $model.status) 'Received Heartbeat status matches the sent payload.' }
}
foreach ($sample in $samples) {
    $s = $sample.state
    if ($null -eq $s.receivedTelemetry) { Require ($null -eq $s.position) 'No API marker before first Telemetry.'; continue }
    $t = $s.receivedTelemetry
    Require ($s.position.row -eq -$t.position.y -and $s.position.col -eq $t.position.x) 'API position is projected from received MQTT coordinates.'
    Require ($telemetryByStamp.ContainsKey([string]$t.timestamp)) 'Sampled API Telemetry exists in the actual receiver log.'
}
foreach ($frame in $dom) {
    if ($null -eq $frame.received) { Require ($frame.markers.Count -eq 0 -and $frame.position -eq '수신 대기') 'No DOM AMR marker before first Telemetry.'; continue }
    $t = $frame.received
    $r = -$t.position.y; $c = $t.position.x
    Require ($frame.markers.Count -eq 1 -and $frame.markers[0].row -eq $r -and $frame.markers[0].col -eq $c -and $frame.position -eq "($r, $c)") 'DOM AMR marker and displayed coordinates match received Telemetry.'
    Require ($telemetryByStamp.ContainsKey([string]$t.timestamp)) 'Displayed Telemetry exists in the actual receiver log.'
    $expectedTrail = @($telemetry | Where-Object { $_.value.model.timestamp -le $t.timestamp } | ForEach-Object { $p=$_.value.model.position; "$(-$p.y),$($p.x)" } | Sort-Object -Unique)
    $actualTrail = @($frame.visited | ForEach-Object { CellKey $_ } | Sort-Object -Unique)
    Require (@(Compare-Object $expectedTrail $actualTrail).Count -eq 0) 'DOM trail equals positions received by that timestamp.'
}
$last = $final.receivedTelemetry
Require ($last.state -eq 'IDLE' -and $last.speed -eq 0 -and $last.position.x -eq 5 -and $last.position.y -eq -4 -and $last.timestamp -eq $final.lastPublishedTimestamp -and (CellKey $final.position) -eq '4,5' -and $final.rejectedCount -eq 0) 'Final sent/received timestamp and goal match; IDLE, speed 0, no rejection.'
Require ($dom[-1].phase -eq '도착 · 수신 확인' -and $dom[-1].progress -eq '3 / 3' -and $dom[-1].error -eq '') 'Final visible UI confirms all waypoints and received completion.'
$result = [ordered]@{
    verifiedAt=(Get-Date -Format o); result='passed'; scenario='UI restart then block (2,2) after move 1'
    moves=$nav.moves; independentMinimumMoves=$minimum; visits=$visits
    telemetryReceived=$telemetry.Count; heartbeatReceived=$heartbeat.Count; rejected=$final.rejectedCount
    apiSamples=$samples.Count; domFrames=$dom.Count; receivedDomFrames=@($dom | Where-Object { $null -ne $_.received }).Count
    finalReceived=$last; finalMapCell=$final.position
    checks=@($checks | Sort-Object -Unique)
    automationNote='A transient UI locator wait timed out before completion. The same run completed; the subsequent DOM snapshot and receiver logs confirmed it. No scenario rerun or product change.'
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$runDir/verification.json" -Encoding utf8
([pscustomobject]$result) | Select-Object result,moves,independentMinimumMoves,telemetryReceived,heartbeatReceived,apiSamples,domFrames,receivedDomFrames | ConvertTo-Json
