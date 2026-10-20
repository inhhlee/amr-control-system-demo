param([Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,80}$')][string]$RunId)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$directory = Join-Path $repository "evidence/COM-03/$RunId"
if (Test-Path -LiteralPath $directory) { throw 'Choose a new execution ID.' }
New-Item -ItemType Directory -Path $directory | Out-Null
[ordered]@{recordedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');head=(git -C $repository rev-parse HEAD);stopLimitMilliseconds=2000;expected='Pending block/resume cannot prevent stop; diagnostic failure cannot prevent cleanup; original error preserved';dockerActuallyInvoked=$false} | ConvertTo-Json | Set-Content "$directory/criteria.json"
$paths = @('tools/COM-03/ResponseGateProxy.cs','tests/manual/COM03-C/ToolRecoveryProbe.cs','tests/manual/COM03-C/Verify-ToolRecovery.ps1','tests/manual/COM03-C/Verify-KeepAlive.ps1','tests/manual/COM03-C/Verify-BrokerShutdown.ps1','tests/manual/COM03-C/Verify-ResponseGate.ps1')
@($paths | ForEach-Object { [ordered]@{path=$_;sha256=(Get-FileHash (Join-Path $repository $_)).Hash} }) | ConvertTo-Json | Set-Content "$directory/source-version.json"
Add-Type -TypeDefinition ((Get-Content -Raw (Join-Path $repository $paths[0])) + "`n" + (Get-Content -Raw (Join-Path $repository $paths[1])))
$proxyResults = foreach ($action in @('block','resume','before-accept')) {
    $caseDirectory = Join-Path $directory $action
    New-Item -ItemType Directory -Path $caseDirectory | Out-Null
    [ToolRecoveryProbe]::RunAsync($caseDirectory,$action).GetAwaiter().GetResult() | ConvertFrom-Json
}
$proxyResults | ConvertTo-Json -Depth 5 | Set-Content "$directory/proxy-results.json"
$cleanupResults = foreach ($tool in @('Verify-KeepAlive.ps1','Verify-BrokerShutdown.ps1','Verify-ResponseGate.ps1')) {
    $source = Join-Path $PSScriptRoot $tool
    $tokens=$null; $parseErrors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$parseErrors)
    if ($parseErrors.Count) { throw 'Tool parse failure.' }
    $main=@($ast.EndBlock.Statements | Where-Object { $_ -is [Management.Automation.Language.TryStatementAst] -and $_.Finally })
    if ($main.Count -ne 1) { throw 'Main finally not uniquely identified.' }
    foreach ($scenario in @('normal','logs-fail','log-write-fail','stop-fail','rm-fail','ps-fail','primary-and-logs-fail','error-report-write-fail','invalid-id')) {
        & {
            $run=Join-Path $directory (([IO.Path]::GetFileNameWithoutExtension($tool))+'-'+$scenario)
            New-Item -ItemType Directory -Path $run | Out-Null
            $child=$null; $console=$null; $proxy=$null; $failure=$null; $cleanupPassed=$false
            $container='a'*64; $name='owned-test-broker'
            if ($scenario -eq 'invalid-id') { $container='not-a-confirmed-id' }
            $existing='{"ID":"existing","Names":"user-broker","State":"exited"}'
            $existing | Set-Content "$run/containers-before.jsonl"
            $primary=[Management.Automation.ErrorRecord]::new([IO.IOException]::new('Original runner failure'),'Primary',[Management.Automation.ErrorCategory]::NotSpecified,$null)
            if ($scenario -in @('primary-and-logs-fail','error-report-write-fail')) { $failure=$primary }
            if ($scenario -eq 'log-write-fail') {
                $log=if ($tool -eq 'Verify-KeepAlive.ps1') { 'broker.log' } else { 'broker-final.log' }
                New-Item -ItemType Directory -Path (Join-Path $run $log) | Out-Null
            }
            if ($scenario -eq 'error-report-write-fail') { New-Item -ItemType Directory -Path "$run/cleanup-failure.log" | Out-Null }
            $calls=[Collections.Generic.List[object]]::new()
            function Invoke-Docker([string[]]$Arguments) {
                $calls.Add(@($Arguments))
                if (($Arguments[0] -eq 'logs' -and $scenario -in @('logs-fail','primary-and-logs-fail','error-report-write-fail')) -or $scenario -eq ($Arguments[0]+'-fail')) { throw [IO.IOException]::new("Injected $($Arguments[0]) failure") }
                if ($Arguments[0] -eq 'ps') {
                    $existing
                    if ($scenario -in @('rm-fail','stop-fail')) { '{"ID":"aaaaaaaaaaaa","Names":"owned-test-broker","State":"running"}' }
                }
                elseif ($Arguments[0] -eq 'rm' -and $scenario -eq 'stop-fail') { throw [IO.IOException]::new('Cannot remove a running container') }
                else { 'stub diagnostic output' }
            }
            function Read-Json([string]$Path) { return $null }
            $escaped=$null
            try {
                # Execute the actual complete finally, replacing only the Docker boundary.
                $body=$main[0].Finally.Extent.Text
                . ([ScriptBlock]::Create($body.Substring(1,$body.Length-2))) 3> "$run/warnings.log"
            } catch { $escaped=$_ }
            $verbs=@($calls | ForEach-Object { $_[0] })
            $destructive=@($calls | Where-Object { $_[0] -in @('stop','rm') })
            $targetSafe=@($destructive | Where-Object { $_[-1] -ne ('a'*64) }).Count -eq 0
            $attempts=if ($scenario -eq 'invalid-id') { $destructive.Count -eq 0 -and 'ps' -in $verbs } else { 'stop' -in $verbs -and 'rm' -in $verbs -and 'ps' -in $verbs }
            $expectedFailure=$scenario -notin @('normal','invalid-id')
            $primaryPreserved=$scenario -notin @('primary-and-logs-fail','error-report-write-fail') -or [Object]::ReferenceEquals($failure,$primary)
            $errors=if (Test-Path "$run/cleanup-failure.log" -PathType Leaf) { Get-Content "$run/cleanup-failure.log" -Raw } else { '' }
            $aggregated=$scenario -ne 'stop-fail' -or ($errors -match 'Injected stop failure' -and $errors -match 'Cannot remove a running container' -and $errors -match 'Test container remains')
            [ordered]@{tool=$tool;scenario=$scenario;calls=@($calls);stopAttempted=('stop' -in $verbs);removeAttempted=('rm' -in $verbs);stateCheckAttempted=('ps' -in $verbs);ownedIdOnly=$targetSafe;primaryPreserved=$primaryPreserved;failureRecorded=($null -ne $failure);escapedError=[string]$escaped;errorsAggregated=$aggregated;passed=($attempts -and $targetSafe -and $primaryPreserved -and !$escaped -and $aggregated -and (($null -ne $failure) -eq $expectedFailure));dockerActuallyInvoked=$false}
        }
    }
}
$cleanupResults | ConvertTo-Json -Depth 6 | Set-Content "$directory/cleanup-results.json"
$failed=@($proxyResults | Where-Object { !$_.passed }).Count + @($cleanupResults | Where-Object { !$_.passed }).Count
[ordered]@{proxyCases=@($proxyResults).Count;cleanupCases=@($cleanupResults).Count;failed=$failed;passed=($failed -eq 0);completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content "$directory/verification.json"
Get-Content "$directory/verification.json"
if ($failed) { throw "$failed tool recovery cases failed; see $directory" }
