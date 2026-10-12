# 별도 PowerShell 프로세스로 실행한다. 이 스크립트가 만든 숨김 콘솔에만 Ctrl+C를 보낸다.
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repository
$executable = Join-Path $repository 'src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.exe'
$docker = (Get-Command docker).Source
$started = [DateTimeOffset]::UtcNow
$records = [System.Collections.Generic.List[object]]::new()
$publications = [System.Collections.Generic.List[object]]::new()

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class HeartbeatConsoleControl
{
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GenerateConsoleCtrlEvent(uint signal, uint groupId);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern uint GetConsoleProcessList([Out] uint[] processList, uint count);
}
'@

function Wait-Log($process, $path, $pattern) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    do {
        $content = if (Test-Path -LiteralPath $path) { (Get-Content -LiteralPath $path -Raw) } else { '' }
        if ($content -match $pattern) { return $content }
        if ($process.HasExited) { throw "Process exited while waiting for $pattern" }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Timed out waiting for $pattern"
}

& $docker compose -f docker/compose.yaml ps -a *> (Join-Path $PSScriptRoot 'broker-state.log')
if ($LASTEXITCODE -ne 0) { throw 'Broker state query failed.' }
& $docker compose -f docker/compose.yaml config --quiet
if ($LASTEXITCODE -ne 0) { throw 'Compose configuration check failed.' }

foreach ($run in 1..2) {
    $stdout = Join-Path $PSScriptRoot "console-$run.stdout.log"
    $stderr = Join-Path $PSScriptRoot "console-$run.stderr.log"
    $arguments = @('--host','127.0.0.1','--port','1884','--connect-timeout-seconds','3')
    if ($run -eq 1) { $arguments += '--show-payload' }
    $process = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $null = $process.Handle
    $observer = $null
    $attached = $false
    try {
        $null = Wait-Log $process $stdout 'Subscribed TopicFilter=amr/\+/heartbeat QoS=0'
        $null = Wait-Log $process $stdout 'WaitingForShutdown'
        $inputs = if ($run -eq 1) {
            @(
                @{topic='amr/AMR_001/heartbeat';payload='{"id":"AMR_001","ts":1791195767000,"status":"OK"}'},
                @{topic='amr/AMR_001/telemetry';payload='{"probe":"not-subscribed"}'},
                @{topic='amr/AMR_001/heartbeat/extra';payload='{"probe":"not-subscribed"}'},
                @{topic='amr/area/AMR_001/heartbeat';payload='{"probe":"not-subscribed"}'},
                @{topic='amr/AMR_001/heartbeat';payload='{"id":"AMR_001","ts":1791195767001,"status":"OK"}'}
            )
        } else {
            @(@{topic='amr/AMR_001/heartbeat';payload='{"id":"AMR_001","ts":1791195767002,"status":"OK"}'})
        }
        # 독립 Mosquitto 관찰자가 비구독 Topic도 실제 Broker에 도착했음을 확인한다.
        $observerOut = Join-Path $PSScriptRoot "observer-$run.stdout.log"
        $observerErr = Join-Path $PSScriptRoot "observer-$run.stderr.log"
        $observerId = "hb-observer-$([Guid]::NewGuid().ToString('N'))"
        $observerArgs = @('exec','-t','amr-demo-mosquitto-1','mosquitto_sub','-h','127.0.0.1','-p','1883','-V','mqttv5','-i',$observerId,'-q','0','-t','amr/#','-C',"$($inputs.Count)",'-W','15','-v','-d')
        $observer = Start-Process -FilePath $docker -ArgumentList $observerArgs -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput $observerOut -RedirectStandardError $observerErr -PassThru
        $null = $observer.Handle
        $null = Wait-Log $observer $observerOut 'received SUBACK'
        foreach ($input in $inputs) {
            $publisherId = "hb-pub-$([Guid]::NewGuid().ToString('N'))"
            $publishArgs = @('compose','-f','docker/compose.yaml','exec','-T','mosquitto','mosquitto_pub','-h','127.0.0.1','-p','1883','-V','mqttv5','-i',$publisherId,'-q','0','-t',$input.topic,'-m',$input.payload)
            $sent = [DateTimeOffset]::UtcNow
            $output = & $docker @publishArgs 2>&1 | Out-String
            $code = $LASTEXITCODE
            $publications.Add([pscustomobject]@{run=$run;sentUtc=$sent;topic=$input.topic;payload=$input.payload;qos=0;retain=$false;clientId=$publisherId;arguments=$publishArgs;exitCode=$code;output=$output})
            $publications | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'publications.json')
            if ($code -ne 0) { throw 'Mosquitto publish failed.' }
        }
        if (!$observer.WaitForExit(10000)) { throw 'Observer did not receive all inputs.' }
        $observer.WaitForExit()
        if ($observer.ExitCode -ne 0) { throw 'Observer failed.' }
        $observed = (Get-Content -LiteralPath $observerOut -Raw)
        foreach ($input in $inputs) {
            if (!$observed.Contains("$($input.topic) $($input.payload)")) { throw 'An input was not observed on the Broker.' }
        }
        $null = Wait-Log $process $stdout 'HeartbeatDiagnostic'
        Start-Sleep -Milliseconds 700
        $receivedLog = (Get-Content -LiteralPath $stdout -Raw)
        $diagnostics = @($receivedLog -split '\r?\n' | Where-Object { $_ -match ' HeartbeatDiagnostic ' })
        $expectedCount = if ($run -eq 1) { 2 } else { 1 }
        if ($diagnostics.Count -ne $expectedCount) { throw "Unexpected diagnostic count: $($diagnostics.Count)" }
        foreach ($line in $diagnostics) {
            if ($line -notmatch 'Topic="amr/AMR_001/heartbeat"' -or $line -notmatch 'Validation=NotPerformed') { throw 'Unexpected diagnostic topic or validation claim.' }
        }
        if ($run -eq 1) {
            $expectedPayloads = @($inputs | Where-Object topic -eq 'amr/AMR_001/heartbeat' | ForEach-Object payload)
            for ($index = 0; $index -lt $diagnostics.Count; $index++) {
                $quoted = [regex]::Match($diagnostics[$index], ' Payload=(.*) PayloadTruncated=False').Groups[1].Value
                if (($quoted | ConvertFrom-Json) -cne $expectedPayloads[$index]) { throw 'Payload preview differs from the published input.' }
            }
        } elseif ($diagnostics[0] -notmatch 'Payload=<hidden>') { throw 'Default payload hiding failed.' }

        $null = [HeartbeatConsoleControl]::FreeConsole()
        if (![HeartbeatConsoleControl]::AttachConsole([uint32]$process.Id)) { throw 'AttachConsole failed.' }
        $attached = $true
        $consolePids = [uint32[]]::new(16)
        $count = [HeartbeatConsoleControl]::GetConsoleProcessList($consolePids, 16)
        if ($count -eq 0 -or $count -gt 16) { throw 'Cannot verify isolated console.' }
        $members = @($consolePids[0..($count - 1)])
        if (@($members | Where-Object { $_ -ne $PID -and $_ -ne $process.Id }).Count) { throw 'Unexpected console member; no signal sent.' }
        if (![HeartbeatConsoleControl]::SetConsoleCtrlHandler([IntPtr]::Zero, $true)) { throw 'Cannot protect controller.' }
        $signalUtc = [DateTimeOffset]::UtcNow
        if (![HeartbeatConsoleControl]::GenerateConsoleCtrlEvent(0, 0)) { throw 'Ctrl+C generation failed.' }
        if (!$process.WaitForExit(10000)) { throw 'Shutdown timed out.' }
        $process.WaitForExit()
        $log = (Get-Content -LiteralPath $stdout -Raw)
        $errors = (Get-Content -LiteralPath $stderr -Raw)
        if ($process.ExitCode -ne 0 -or $errors) { throw 'Console did not exit cleanly.' }
        if ($log -notmatch '(?s)ShutdownRequested.*Unsubscribing.*SubscriptionStopped.*Disconnecting.*Disconnected.*Disposed.*ExitCode=0') { throw 'Cleanup order mismatch.' }
        $clientId = [regex]::Match($log, 'ClientId=(\S+)').Groups[1].Value
        $records.Add([pscustomobject]@{run=$run;arguments=$arguments;clientId=$clientId;processId=$process.Id;signal='Windows CTRL_C_EVENT';signalUtc=$signalUtc;finishedUtc=[DateTimeOffset]::UtcNow;consoleMembers=$members;diagnosticCount=$diagnostics.Count;observedPublishCount=$inputs.Count;negativeTopicCount=($inputs.Count-$diagnostics.Count);exitCode=$process.ExitCode;stderr=$errors;observerExit=$observer.ExitCode})
        $records | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'mqtt-results.json')
    }
    finally {
        if ($attached) {
            $null = [HeartbeatConsoleControl]::SetConsoleCtrlHandler([IntPtr]::Zero, $false)
            $null = [HeartbeatConsoleControl]::FreeConsole()
        }
        foreach ($ownedProcess in @($observer, $process)) {
            if ($null -ne $ownedProcess) {
                if (!$ownedProcess.HasExited) {
                    $ownedProcess.Kill()
                    $ownedProcess.WaitForExit()
                    "Harness failure: terminated owned process $($ownedProcess.Id)." | Add-Content -LiteralPath (Join-Path $PSScriptRoot 'harness-failure.log')
                }
                $ownedProcess.Dispose()
            }
        }
    }
}
if ($records[0].clientId -eq $records[1].clientId) { throw 'Client ID reused.' }
& $docker compose -f docker/compose.yaml logs --no-color --timestamps --since $started.ToString('O') mosquitto *> (Join-Path $PSScriptRoot 'broker.log')
if ($LASTEXITCODE -ne 0) { throw 'Broker log collection failed.' }
$brokerLog = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'broker.log'))
foreach ($record in $records) {
    if ($brokerLog -notmatch ([regex]::Escape($record.clientId) + '(?: \[[^\]]+\])? disconnected\.')) { throw 'Broker disconnect evidence missing.' }
}
Write-Output 'PASS: Broker observed six publications; console received only three Heartbeats across two sessions; Ctrl+C exited 0 twice.'


