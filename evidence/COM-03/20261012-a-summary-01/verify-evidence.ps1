param([string]$CodeCommit='c34f380728d914aed9d0fe5f3427d698493d2ab6',[string]$OutputPath=(Join-Path $PSScriptRoot 'audit-results.json'))
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$checks=[Collections.Generic.List[object]]::new()
$sourceChecks=[Collections.Generic.List[object]]::new()
$hashTool=[Security.Cryptography.SHA256]::Create()
function Check($Name,[bool]$Passed,$Evidence) { $checks.Add([pscustomobject]@{name=$Name;passed=$Passed;evidence=$Evidence}) }
function Text($Path) { [IO.File]::ReadAllText((Join-Path $repoRoot $Path)) }
function Json($Path) { Text $Path|ConvertFrom-Json -DateKind String }
function JsonLines($Path) { (Text $Path)-split '\r?\n'|Where-Object { $_.Trim() }|ForEach-Object { $_|ConvertFrom-Json -DateKind String } }
function Hash([byte[]]$Bytes) { [BitConverter]::ToString($hashTool.ComputeHash($Bytes)).Replace('-','') }
function Normalized([byte[]]$Bytes) { [Text.Encoding]::UTF8.GetString($Bytes).Replace(([string][char]13+[char]10),[string][char]10) }
function GitBytes($Path) {
    $info=[Diagnostics.ProcessStartInfo]::new('git')
    $info.WorkingDirectory=$repoRoot
    $info.UseShellExecute=$false
    $info.RedirectStandardOutput=$true
    $info.RedirectStandardError=$true
    $info.CreateNoWindow=$true
    foreach ($arg in @('show',($CodeCommit+':'+$Path))) { $info.ArgumentList.Add($arg) }
    $p=[Diagnostics.Process]::Start($info)
    $m=[IO.MemoryStream]::new()
    try { $p.StandardOutput.BaseStream.CopyTo($m);$err=$p.StandardError.ReadToEnd();$p.WaitForExit();if ($p.ExitCode -ne 0) {throw $err};return ,$m.ToArray() }
    finally {$m.Dispose();$p.Dispose()}
}
Push-Location $repoRoot
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $preservedBefore=(Get-FileHash 'CONTRIBUTING.md').Hash
    foreach ($pr in (Json 'evidence/COM-03/20261012-a-summary-01/remote-snapshot.json').pullRequests) {
        & git merge-base --is-ancestor $pr.mergeCommit $CodeCommit
        Check "PR #$($pr.number) develop merge included" ($LASTEXITCODE -eq 0 -and $pr.merged -and $pr.base -eq 'develop') $pr.url
    }
    $productDiff=@(& git diff --name-only $CodeCommit -- src tests docker global.json AmrControlSystem.slnx)
    if ($LASTEXITCODE -ne 0) {throw 'Git diff failed'}
    Check 'Product code/config unchanged' ($productDiff.Count -eq 0) 'git diff CodeCommit -- src tests docker global.json AmrControlSystem.slnx'
    foreach ($runId in @('20261011-a2-connect-02','20261011-a3-shutdown-03')) {
        $manifest=Json "evidence/COM-03/$runId/source-version.json"
        $zipPath=Join-Path $repoRoot "evidence/COM-03/$runId/$($manifest.archive)"
        Check "$runId ZIP hash" ((Get-FileHash $zipPath).Hash -eq $manifest.archiveSha256) "$runId/source-version.json"
        $zip=[IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            foreach ($file in $manifest.files) {
                $entry=$zip.GetEntry($file.path)
                if ($null -eq $entry) {throw "Missing archive entry $($file.path)"}
                $stream=$entry.Open();$m=[IO.MemoryStream]::new()
                try {$stream.CopyTo($m);$bytes=$m.ToArray()} finally {$stream.Dispose();$m.Dispose()}
                Check "$runId snapshot $($file.path)" ((Hash $bytes) -eq $file.sha256) "$runId/source-version.json"
                # Compare merged code to final A3, not intermediate A2 Program.cs.
                if ($runId -eq '20261011-a3-shutdown-03') {
                    $gitBytes=GitBytes $file.path
                    $workingBytes=[IO.File]::ReadAllBytes((Join-Path $repoRoot $file.path))
                    $item=[pscustomobject]@{path=$file.path;gitBlobMatchesManifest=((Hash $gitBytes) -eq $file.sha256);normalizedGitMatches=((Normalized $gitBytes) -eq (Normalized $bytes));normalizedWorkingMatches=((Normalized $workingBytes) -eq (Normalized $bytes))}
                    $sourceChecks.Add($item)
                    Check "Merged/working source $($file.path)" ($item.normalizedGitMatches -and $item.normalizedWorkingMatches) $file.path
                }
            }
        } finally {$zip.Dispose()}
    }
    $a1Commands=@(JsonLines 'evidence/COM-03/20261008-a1-broker-01/commands.jsonl')
    foreach ($log in @('12-up.log','17-stop.log','22-up-again.log')) {
        $rows=@($a1Commands|Where-Object log -eq $log)
        Check "A1 recorded $log exit 0" ($rows.Count -eq 1 -and $rows[0].exitCode -eq 0) 'A1 commands.jsonl'
    }
    $a1Log=Text 'evidence/COM-03/20261008-a1-broker-01/24-logs-restarted.log'
    Check 'A1 recorded config loaded/running' ($a1Log.Contains('Config loaded') -and $a1Log.Contains('mosquitto version 2.1.2 running')) 'A1 24-logs-restarted.log'
    $compose=@(JsonLines 'evidence/COM-03/20261011-a2-connect-02/commands.jsonl'|Where-Object name -eq 'compose-config')
    Check 'A2 Compose recorded exit 0' ($compose.Count -eq 1 -and $compose[0].exitCode -eq 0) 'A2 commands.jsonl'
    $a2=Json 'evidence/COM-03/20261011-a2-connect-02/verification.json'
    $a2Broker=Text 'evidence/COM-03/20261011-a2-connect-02/broker-logs.log'
    Check 'A2 recorded distinct IDs' ($a2.clientIds.Count -eq 2 -and $a2.clientIds[0] -ne $a2.clientIds[1]) 'A2 verification.json'
    for ($i=0;$i -lt 2;$i++) {
        $id=[regex]::Escape($a2.clientIds[$i])
        $stdout=Text ('evidence/COM-03/20261011-a2-connect-02/console-{0:D2}.log' -f ($i+1))
        Check "A2 recorded run $($i+1)" ($a2.normalExitCodes[$i] -eq 0 -and $stdout -match $id -and $stdout -match ' Connected\r?\n' -and $stdout -match ' Disconnected\r?\n' -and $stdout -match ' Disposed\r?\n' -and $a2Broker -match "as $id " -and $a2Broker -match "Client $id .* disconnected\.") 'A2 console/Broker logs'
    }
    $program=Text 'src/Amr.Communication.Console/Program.cs'
    $connection=Text 'src/Amr.Communication/Mqtt/MqttConnection.cs'
    Check 'Static Connected report after awaited approval' ($program.IndexOf('await connection.ConnectAsync') -ge 0 -and $program.IndexOf('Report("Connected")') -gt $program.IndexOf('await connection.ConnectAsync') -and $connection.Contains('result.ResultCode != MqttClientConnectResultCode.Success')) 'Program.cs / MqttConnection.cs'
    $runs=@(Json 'evidence/COM-03/20261011-a3-shutdown-03/native-console-results.json')
    $broker=Text 'evidence/COM-03/20261011-a3-shutdown-03/broker.log'
    Check 'A3 recorded distinct IDs/PIDs' ($runs.Count -eq 2 -and $runs[0].clientId -ne $runs[1].clientId -and $runs[0].processId -ne $runs[1].processId) 'A3 native-console-results.json'
    foreach ($run in $runs) {
        $stdout=Text "evidence/COM-03/20261011-a3-shutdown-03/native-$($run.run).stdout.log"
        $stderr=Text "evidence/COM-03/20261011-a3-shutdown-03/native-$($run.run).stderr.log"
        $ordered=$true;$position=-1
        foreach ($state in @('Connecting','Connected','WaitingForShutdown','ShutdownRequested','Disconnecting','Disconnected','Disposed','ExitCode=0')) {
            $match=[regex]::Match($stdout,'(?m)^\S+ '+[regex]::Escape($state)+'(?:[:\r\n]|$)')
            if (-not $match.Success -or $match.Index -le $position) {$ordered=$false}
            $position=$match.Index
        }
        $settings=$stdout.Contains("Host=127.0.0.1 Port=1884 ClientId=$($run.clientId) MQTT=5 CleanStart=true SessionExpiry=0 KeepAliveSeconds=5 ConnectTimeoutSeconds=3 DisconnectTimeoutSeconds=3")
        Check "A3 recorded run $($run.run) lifecycle/settings/exit" ($ordered -and $settings -and $run.exitCode -eq 0 -and $run.processExited -and $run.heldAfterWaitStateSeconds -ge 3 -and [string]::IsNullOrWhiteSpace($stderr)) "A3 native-$($run.run) logs/results"
        $id=[regex]::Escape($run.clientId)
        Check "A3 recorded run $($run.run) Broker pairing" ($broker -match "as $id " -and $broker -match "Client $id .* disconnected\.") 'A3 broker.log'
    }
    $v=Json 'evidence/COM-03/20261011-a3-shutdown-03/verification.json'
    Check 'Recorded process termination/log matching' ($v.testProcessesExited -and $v.brokerConnectAndDisconnectMatched) 'A3 verification.json'
    $before=Json 'evidence/COM-03/20261011-a3-shutdown-02/broker-before.json'
    $after=Json 'evidence/COM-03/20261011-a3-shutdown-03/broker-after.json'
    Check 'Recorded broker identity/loopback unchanged' ($before.ID -eq $after.ID -and $after.State -eq 'running' -and $after.Publishers[0].URL -eq '127.0.0.1' -and $after.Publishers[0].PublishedPort -eq 1884) 'A3 broker-before/after.json'
    [xml]$trx=Text 'evidence/COM-03/20261011-a3-shutdown-02/tests.trx';$c=$trx.TestRun.ResultSummary.Counters
    Check 'Recorded TRX 6 passed without failures/skips' ($c.total -eq '6' -and $c.executed -eq '6' -and $c.passed -eq '6' -and $c.failed -eq '0' -and $c.notExecuted -eq '0' -and @($trx.TestRun.Results.UnitTestResult|Where-Object outcome -eq 'Passed').Count -eq 6) 'A3 tests.trx'
    $restore=@(JsonLines 'evidence/COM-03/20261011-a3-shutdown-01/commands.jsonl'|Where-Object name -eq 'restore')
    $buildTest=@(JsonLines 'evidence/COM-03/20261011-a3-shutdown-02/commands.jsonl')
    Check 'Recorded restore/build/test exit 0' ($restore.Count -eq 1 -and $restore[0].exitCode -eq 0 -and @($buildTest|Where-Object {$_.name -in @('build','tests') -and $_.exitCode -eq 0}).Count -eq 2) 'A3 01/02 commands.jsonl'
    Check 'Recorded minimal failure not Connected' ($v.failureExitCode -eq 1 -and -not $v.failureConnectedReported) 'A3 verification.json'
    $preservedAfter=(Get-FileHash 'CONTRIBUTING.md').Hash
    Check 'CONTRIBUTING.md untouched by audit' ($preservedBefore -eq $preservedAfter) 'SHA-256 before/after'
    $failed=@($checks|Where-Object {-not $_.passed})
    $result=[ordered]@{runId='20261012-a-summary-01';performedUtc=[DateTimeOffset]::UtcNow.ToString('O');kind='existing-evidence-and-merged-code-audit';productTestsRerun=$false;currentBrokerStateQueried=$false;codeCommit=$CodeCommit;head=(& git rev-parse HEAD);branch=(& git branch --show-current);powershell=$PSVersionTable.PSVersion.ToString();recordedProductEnvironment=(Json 'evidence/COM-03/20261011-a3-shutdown-02/environment.json');passed=($failed.Count -eq 0);checks=$checks.ToArray();sourceChecks=$sourceChecks.ToArray();preservedContributingSha256=$preservedAfter;limitations=@('User Git Bash echo exit status not captured','Broker session DB not inspected','Detailed failure/cancellation is #5','Abnormal disconnect/KeepAlive monitoring is #6')}
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath),($result|ConvertTo-Json -Depth 10)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
    Write-Output "$($checks.Count) evidence checks, $($failed.Count) failures. Product tests rerun: false."
    if ($failed.Count -gt 0) {$failed|Format-Table;throw 'Evidence audit failed'}
} finally {$hashTool.Dispose();Pop-Location}
