$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repoRoot
$baseline = Get-Content "$PSScriptRoot/baseline.json" -Raw | ConvertFrom-Json
$backupRoot = Join-Path $repoRoot 'evidence/GIT/20261027-preservation-backup-01'
$snapshot = Get-Content "$backupRoot/source-snapshot.json" -Raw | ConvertFrom-Json
function Assert([bool]$condition, [string]$message) { if (!$condition) { throw $message } }
function Compare-Files($files, [string]$root) {
    @($files | ForEach-Object {
        $actual = (Get-FileHash -LiteralPath (Join-Path $root $_.path) -Algorithm SHA256).Hash
        Assert ($actual -eq $_.sha256) "Hash changed: $($_.path)"
        [ordered]@{ path=$_.path; sha256=$actual; unchanged=$true }
    })
}
$protected = Compare-Files $baseline.protectedFiles $repoRoot
$originalUntracked = Compare-Files $snapshot.files $repoRoot
$backup = Compare-Files (Get-Content "$backupRoot/artifact-hashes.json" -Raw | ConvertFrom-Json) $backupRoot
$head = (git rev-parse HEAD).Trim()
$branch = (git branch --show-current).Trim()
Assert ($head -eq $baseline.base) 'HEAD changed'
Assert ($branch -eq $baseline.branch) 'Branch changed'
Assert ((git rev-parse develop).Trim() -eq $baseline.base) 'Develop changed'
Assert ((git rev-parse origin/develop).Trim() -eq $baseline.base) 'Origin/develop changed'
Assert ((git rev-parse refs/stash).Trim() -eq $baseline.stash) 'Stash changed'
Assert ((git rev-parse codex/preserve-grid-map-20261023).Trim() -eq $baseline.source) 'Preservation branch changed'
Assert (@(git stash list).Count -eq 1) 'Stash count changed'
Assert (@(git diff --cached --name-only).Count -eq 0) 'Unexpected staged changes'
$trackedChanges = @(git diff --name-only)
Assert (@($trackedChanges | Where-Object {$_ -notin @('AmrControlSystem.slnx','README.md','BUILDING.md')}).Count -eq 0) 'Unexpected tracked code changes'
git diff --check
Assert ($LASTEXITCODE -eq 0) 'Whitespace error in tracked diff'
$excluded = 'tests/manual/AMR01/Verify-GridNavigation.ps1'
$imports = @($baseline.importedFiles | Where-Object {$_ -ne $excluded} | ForEach-Object {
    $source = (git rev-parse ($baseline.source + ':' + $_)).Trim()
    $actual = (git hash-object "--path=$_" -- $_).Trim()
    $same = $source -eq $actual
    $allowed = $_ -in @('tests/manual/AMR01/Start-MqttMap.ps1','tests/manual/AMR01/Stop-MqttMap.ps1','tools/Amr.Simulator.Map/README.md')
    Assert ($same -or $allowed) "Unexpected import edit: $_"
    [ordered]@{path=$_; sourceBlob=$source; normalizedBlob=$actual; sameAsSource=$same; sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}
})
Assert (!(Test-Path -LiteralPath $excluded)) 'Historical incompatible script was not excluded'
$automatic = @(Get-ChildItem "$PSScriptRoot/tests/*.trx" | ForEach-Object {
    [xml]$trx = Get-Content -LiteralPath $_.FullName -Raw
    $counts = $trx.TestRun.ResultSummary.Counters
    Assert ([int]$counts.failed -eq 0 -and [int]$counts.notExecuted -eq 0) 'Automatic tests failed/skipped'
    [ordered]@{project=$_.BaseName; total=[int]$counts.total; passed=[int]$counts.passed}
})
$automaticTotal = 0
foreach ($result in $automatic) { $automaticTotal += $result.passed }
Assert ($automaticTotal -eq 1063) 'Unexpected automatic test total'
$live = Get-Content 'evidence/AMR/20261027-grid-mqtt-01/verification.json' -Raw | ConvertFrom-Json
$settings = Get-Content 'evidence/AMR/20261027-grid-settings-01/verification.json' -Raw | ConvertFrom-Json
Assert ($live.Count -eq 15 -and @($live | Where-Object verdict -ne 'passed').Count -eq 0) 'MQTT verification failure'
Assert ($settings.Count -eq 6 -and @($settings | Where-Object verdict -ne 'passed').Count -eq 0) 'Settings verification failure'
$uiRoot = 'evidence/AMR/20261027-grid-ui-01'
$final = Get-Content "$uiRoot/final-state.json" -Raw | ConvertFrom-Json
Assert ($final.navigation.phase -eq 'Completed' -and $final.navigation.moves -eq 15) 'UI did not finish with 15 moves'
Assert (($final.navigation.reachedWaypoints.moves -join ',') -eq '11,13,15') 'Waypoint order incorrect'
Assert ($final.position.row -eq 4 -and $final.position.col -eq 5) 'Received map position incorrect'
Assert ($final.receivedTelemetry.position.x -eq 5 -and $final.receivedTelemetry.position.y -eq -4 -and $final.receivedTelemetry.speed -eq 0 -and $final.receivedTelemetry.state -eq 'IDLE') 'Final payload incorrect'
Assert ($final.receivedTelemetry.timestamp -eq $final.lastPublishedTimestamp) 'Final publication not received'
$ax = Get-Content "$uiRoot/final-ax.txt" -Raw
Assert ($ax.Contains('도착 · 수신 확인') -and $ax.Contains('15회') -and $ax.Contains('4행 5열 현재 AMR')) 'Final UI and API differ'
$moving = Get-Content "$uiRoot/moving-before-obstacle-ax.txt" -Raw
Assert ($moving -match 'container 실제 이동\r?\n\s+\d+ text 실제 이동\r?\n\s+\d+ text 1회') 'Obstacle not inserted after movement'
$cleanup = @('20261027-grid-host-01','20261027-grid-ui-host-01' | ForEach-Object {
    $item = Get-Content "evidence/AMR/$_/cleanup.json" -Raw | ConvertFrom-Json
    Assert ($item.OwnedBrokerRemoved -and $item.HostPortClosed -and $item.ExistingContainersPreserved -and $item.HostExitCode -eq 0) 'Cleanup incomplete'
    [ordered]@{runId=$_; result=$item}
})
$report = [ordered]@{
    verifiedAt=[DateTimeOffset]::Now.ToString('o'); branch=$branch; head=$head; stash=$baseline.stash;
    preservedBranch=$baseline.source; stagedFiles=0; trackedChanges=$trackedChanges;
    protectedFiles=$protected; originalUntrackedFiles=$originalUntracked; backupArtifacts=$backup;
    finalImportedFiles=$imports; excludedHistoricalScript=$excluded;
    automatic=$automatic; mqttScenarios=15; settingsScenarios=6;
    ui=[ordered]@{passed=$true; moves=15; waypointMoves=@(11,13,15); telemetryTimestamp=$final.receivedTelemetry.timestamp; receivedTelemetry=$final.receivedTelemetry};
    cleanup=$cleanup
}
$report | ConvertTo-Json -Depth 12 | Set-Content "$PSScriptRoot/final-audit.json" -Encoding utf8
Write-Output "PASS protected=$($protected.Count), original-untracked=$($originalUntracked.Count), imports=$($imports.Count), automated=1063, MQTT=15, settings=6, UI=15-move-arrival, staged=0"
