param(
    [Parameter(Mandatory)][ValidatePattern('^http://127\.0\.0\.1:[0-9]+$')][string]$BaseUrl,
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunId
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$root=Join-Path $repo "evidence/AMR/$RunId"
if (Test-Path $root) { throw 'Use a new RunId.' }
$null=New-Item -ItemType Directory $root
$headers=@{'X-Simulator-Map'='1'}
$script:samples=[Collections.Generic.List[object]]::new()
function Read-State {
    $s=Invoke-RestMethod "$BaseUrl/api/state" -TimeoutSec 3
    $script:samples.Add([pscustomobject]@{receivedAt=[DateTimeOffset]::Now.ToString('o');runId=$s.runId;sequence=$s.sequence;phase=$s.phase;x=$s.position.x;y=$s.position.y;speed=$s.speed;state=$s.amrState;elapsed=$s.elapsedSeconds;exitCode=$s.exitCode;line=$s.lastSnapshotLine})
    if ($s.lastSnapshotLine) {
        if ($s.lastSnapshotLine -notmatch ' X=(\S+) Y=(\S+) State=(\S+) CurrentSpeedMps=(\S+)') { throw 'Snapshot log shape mismatch.' }
        $culture=[Globalization.CultureInfo]::InvariantCulture
        if ([double]::Parse($Matches[1],$culture) -ne $s.position.x -or [double]::Parse($Matches[2],$culture) -ne $s.position.y -or $Matches[3] -ne $s.amrState -or [double]::Parse($Matches[4],$culture) -ne $s.speed) { throw 'API position/state/speed do not match the actual console line.' }
    }
    return $s
}
function Wait-Phase([string]$Expected) {
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(10)
    do {
        $s=Read-State
        if ($s.phase -eq $Expected) { return $s }
        if ($s.phase -eq 'Failed') { throw 'Map execution failed.' }
        Start-Sleep -Milliseconds 80
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Did not reach $Expected."
}
try {
    $initial=Read-State
    if ($initial.phase -in @('Running','Canceling')) { throw 'A user simulation is active; finish it before verification.' }
    $blocked=Invoke-WebRequest "$BaseUrl/api/start" -Method Post -SkipHttpErrorCheck
    if ([int]$blocked.StatusCode -ne 403) { throw 'Requests without the control header must be rejected.' }
    $null=Invoke-WebRequest "$BaseUrl/api/start" -Method Post -Headers $headers
    $first=Read-State
    $overlap=Invoke-WebRequest "$BaseUrl/api/start" -Method Post -Headers $headers -SkipHttpErrorCheck
    if ([int]$overlap.StatusCode -ne 409) { throw 'Concurrent start was not rejected.' }
    $complete=Wait-Phase 'Completed'
    $complete | ConvertTo-Json -Depth 12 | Set-Content "$root/completed.json"
    $target=$complete.settings.waypoints[-1]
    if ($complete.position.x -ne $target.x -or $complete.position.y -ne $target.y -or $complete.speed -ne 0 -or $complete.amrState -ne 'IDLE' -or $complete.exitCode -ne 0) { throw 'Arrival state mismatch.' }
    if (@($script:samples | Where-Object { $_.runId -eq $complete.runId -and $_.state -eq 'BUSY' -and $_.speed -gt 0 }).Count -lt 2) { throw 'No live movement samples.' }
    Start-Sleep -Milliseconds 120
    $null=Invoke-WebRequest "$BaseUrl/api/start" -Method Post -Headers $headers
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(3)
    do { $moving=Read-State; if ($moving.amrState -eq 'BUSY') { break }; Start-Sleep -Milliseconds 20 } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($moving.amrState -ne 'BUSY') { throw 'No moving state before cancellation.' }
    $null=Invoke-WebRequest "$BaseUrl/api/cancel" -Method Post -Headers $headers
    $canceled=Wait-Phase 'Canceled'
    $canceled | ConvertTo-Json -Depth 12 | Set-Content "$root/canceled.json"
    if ($canceled.exitCode -ne 0 -or @($canceled.logs | Where-Object { $_ -like 'SimulationCompleted*' }).Count) { throw 'Canceled execution was reported as completion.' }
    Start-Sleep -Milliseconds 120
    $null=Invoke-WebRequest "$BaseUrl/api/start" -Method Post -Headers $headers
    $restarted=Wait-Phase 'Completed'
    if ($restarted.runId -in @($complete.runId,$canceled.runId) -or $restarted.exitCode -ne 0) { throw 'Restart did not create a new successful run.' }
    $restarted | ConvertTo-Json -Depth 12 | Set-Content "$root/restarted.json"
    @{Passed=$true;BaseUrl=$BaseUrl;SamplesComparedToConsole=$script:samples.Count;OverlapStatus=[int]$overlap.StatusCode;MissingHeaderStatus=[int]$blocked.StatusCode;CompletedRun=$complete.runId;CompletedSeconds=$complete.elapsedSeconds;CanceledRun=$canceled.runId;RestartedRun=$restarted.runId;ProductEvidence=$complete.evidenceDirectory;Finished=[DateTimeOffset]::Now.ToString('o')} | ConvertTo-Json | Set-Content "$root/verification.json"
    Write-Output "PASS: live coordinates match console output, arrival/stop, concurrent-start rejection, cancel, restart. $root"
}
catch { $_ | Out-String | Set-Content "$root/failure.log"; throw }
finally { $script:samples | ForEach-Object { $_ | ConvertTo-Json -Compress } | Set-Content "$root/samples.jsonl" }
