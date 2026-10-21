param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunId,
    [switch]$Controller,
    [switch]$SeqUnavailable,
    [ValidatePattern('^$|^[a-f0-9]{64}$')][string]$BrokerContainer,
    [ValidateRange(1,65535)][int]$BrokerPort = 1884
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repository
$run = Join-Path $repository "evidence/COM-01/$RunId"

if (!$Controller) {
    # 기존 증거를 덮어쓰지 않는다. 새 실행에는 반드시 새 ID를 사용한다.
    if (Test-Path -LiteralPath $run) { throw 'Execution ID exists. Choose a new RunId.' }
    New-Item -ItemType Directory -Path $run | Out-Null
    $started = [DateTimeOffset]::UtcNow
    $child = $null
    try {
        if ($BrokerContainer) {
            $brokerInfo = (docker inspect $BrokerContainer | ConvertFrom-Json)[0]
            if ($LASTEXITCODE -ne 0 -or !$brokerInfo.State.Running) { throw 'Supplied isolated Broker is not running.' }
            $binding = @($brokerInfo.NetworkSettings.Ports.'1883/tcp')
            if ($binding.Count -ne 1 -or $binding[0].HostIp -ne '127.0.0.1' -or [int]$binding[0].HostPort -ne $BrokerPort) { throw 'Isolated Broker endpoint mismatch.' }
            $container = $BrokerContainer
            $brokerInfo.State | ConvertTo-Json | Set-Content "$run/broker-state.log"
        } else {
            docker compose -f docker/compose.yaml config --quiet
            if ($LASTEXITCODE -ne 0) { throw 'Compose configuration failed.' }
            docker compose -f docker/compose.yaml ps -a > "$run/broker-state.log"
            $container = docker compose -f docker/compose.yaml ps -q mosquitto
            if ($LASTEXITCODE -ne 0 -or !$container) { throw 'Project Broker must already be running.' }
        }
        docker inspect $container --format '{{json .Mounts}}' > "$run/broker-mounts.json"
        if ($LASTEXITCODE -ne 0) { throw 'Broker mount query failed.' }
        $dockerVersion = docker version --format '{{.Client.Version}} / {{.Server.Version}}'
        if ($LASTEXITCODE -ne 0) { throw 'Docker version query failed.' }
        $brokerVersion = docker exec $container mosquitto -h
        if ($LASTEXITCODE -ne 0) { throw 'Broker version query failed.' }
        [ordered]@{ startedAtUtc=$started.ToString('O'); os=[Runtime.InteropServices.RuntimeInformation]::OSDescription;
            sdk=(dotnet --version); dockerVersion=$dockerVersion; brokerVersion=$brokerVersion[0];
            endpoint="127.0.0.1:$BrokerPort"; brokerContainer=$container; mqttnet='5.2.0.1603'; qos=0; retain=$false;
            brokerStartedOrRestartedByThisTest=$false; baseCommit=(git rev-parse HEAD); branch=(git branch --show-current) } |
            ConvertTo-Json | Set-Content -Encoding utf8 "$run/environment.json"
        $files = @(rg --files src tests -g '*.cs' -g '*.csproj' -g '*.ps1' -g '*.json') + @('global.json','AmrControlSystem.slnx')
        [ordered]@{ baseCommit=(git rev-parse HEAD); sourceState='working tree; see file hashes'; files=@($files | ForEach-Object {
            [ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}
        }) } | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 "$run/source-version.json"
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'inputs.json') -Destination "$run/inputs.json"
        $controllerArguments = @('-NoProfile','-File', $PSCommandPath,'-RunId',$RunId,'-Controller')
        $controllerArguments += @('-BrokerPort',"$BrokerPort")
        if ($BrokerContainer) { $controllerArguments += @('-BrokerContainer',$BrokerContainer) }
        if ($SeqUnavailable) { $controllerArguments += '-SeqUnavailable' }
        $child = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList $controllerArguments `
            -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput "$run/controller.stdout.log" `
            -RedirectStandardError "$run/controller.stderr.log" -PassThru
        $null = $child.Handle
        # Docker exec startup per publication can exceed the old whole-scenario budget on a busy host.
        # Individual receive, publish and application shutdown limits (8s each) are unchanged.
        if (!$child.WaitForExit(180000)) { throw 'MQTT controller timed out.' }
        $child.WaitForExit()
        [ordered]@{exitCode=$child.ExitCode;completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')} |
            ConvertTo-Json | Set-Content -Encoding utf8 "$run/controller-result.json"
        if ($child.ExitCode -ne 0) { throw 'MQTT scenario failed; see controller/failure logs.' }
        docker logs --timestamps --since $started.ToString('O') $container *> "$run/broker.log"
        if ($LASTEXITCODE -ne 0) { throw 'Broker log collection failed.' }
        $results = Get-Content "$run/scenario-result.json" -Raw | ConvertFrom-Json
        $brokerLog = Get-Content "$run/broker.log" -Raw
        foreach ($session in $results.sessions) {
            if ($brokerLog -notmatch ([regex]::Escape($session.clientId) + '(?: \[[^\]]+\])? disconnected\.')) {
                throw 'Broker disconnect record missing.'
            }
        }
        $results | Add-Member -NotePropertyName brokerDisconnectConfirmed -NotePropertyValue $true
        $results | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 "$run/verification.json"
        Write-Output "PASS: two sessions, model fields, routing, rejection recovery and CTRL_C_EVENT exit 0. Evidence: $run"
    }
    catch { $_ | Out-String | Set-Content -Encoding utf8 "$run/runner-failure.log"; throw }
    finally {
        if ($null -ne $child) {
            if (!$child.HasExited) { $child.Kill($true); $child.WaitForExit() }
            $child.Dispose()
        }
    }
    exit 0
}

# Ctrl+C는 이 도구가 시작한 콘솔에만 보낸다. 부모 PowerShell·다른 프로그램은 대상으로 삼지 않는다.
if ($BrokerContainer) {
    $env:AMR_SEQ_URL = $null
    $env:AMR_SEQ_API_KEY = $null
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Com01BConsoleControl
{
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GenerateConsoleCtrlEvent(uint signal, uint groupId);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern uint GetConsoleProcessList([Out] uint[] processList, uint count);
}
'@
$inputData = Get-Content "$run/inputs.json" -Raw | ConvertFrom-Json
$publications = [Collections.Generic.List[object]]::new()
$sessions = [Collections.Generic.List[object]]::new()
$process = $null
$attached = $false

function Read-Log { if (Test-Path -LiteralPath $stdout) { Get-Content -LiteralPath $stdout -Raw } else { '' } }
function Wait-Log([scriptblock]$condition) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(8)
    do {
        $text = Read-Log
        if (& $condition $text) { return $text }
        if ($process.HasExited) { throw 'Console exited before expected output.' }
        Start-Sleep -Milliseconds 50
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'Expected output timed out.'
}
function Lines([string]$text, [string]$marker) { ,@($text -split '\r?\n' | Where-Object { $_.Contains($marker) }) }

try {
    for ($index=0; $index -lt $inputData.sessions.Count; $index++) {
        $stdout = "$run/console-$index.stdout.log"
        $stderr = "$run/console-$index.stderr.log"
        $executable = Join-Path $repository 'src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.exe'
        $process = Start-Process -FilePath $executable -ArgumentList @('--host','127.0.0.1','--port',"$BrokerPort",'--show-payload') `
            -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        $null = $process.Handle
        $null = Wait-Log { param($text) $text -match 'Subscribed TopicFilters=amr/\+/heartbeat,amr/\+/telemetry,amr/\+/alarm,amr/\+/ack QoS=0' }
        $accepted = 0; $rejected = 0; $ignored = 0
        foreach ($case in $inputData.sessions[$index]) {
            $publisher = 'com01b-pub-' + [Guid]::NewGuid().ToString('N')
            $sentAt = [DateTimeOffset]::UtcNow.ToString('O')
            # Ctrl+C 확인 뒤 콘솔에서 분리된 PowerShell에서도 네이티브 명령을 실행할 수 있게
            # 표준 입출력을 명시적으로 연결한다. ArgumentList는 JSON 따옴표도 그대로 전달한다.
            $publishInfo = [Diagnostics.ProcessStartInfo]::new('docker')
            $publishInfo.UseShellExecute = $false
            $publishInfo.CreateNoWindow = $true
            $publishInfo.RedirectStandardInput = $true
            $publishInfo.RedirectStandardOutput = $true
            $publishInfo.RedirectStandardError = $true
            $publisherPrefix = if ($BrokerContainer) { @('exec',$BrokerContainer) } else { @('compose','-f','docker/compose.yaml','exec','-T','mosquitto') }
            foreach ($argument in ($publisherPrefix + @('mosquitto_pub',
                    '-h','127.0.0.1','-p','1883','-V','mqttv5','-i',$publisher,'-q','0','-t',$case.topic,'-m',$case.payload))) {
                $publishInfo.ArgumentList.Add($argument)
            }
            $publisherProcess = [Diagnostics.Process]::Start($publishInfo)
            try {
                $publisherProcess.StandardInput.Close()
                $publishStdout = $publisherProcess.StandardOutput.ReadToEndAsync()
                $publishStderr = $publisherProcess.StandardError.ReadToEndAsync()
                if (!$publisherProcess.WaitForExit(8000)) { $publisherProcess.Kill($true); throw 'Publisher timed out.' }
                $publishOutput = $publishStdout.GetAwaiter().GetResult() + $publishStderr.GetAwaiter().GetResult()
                $publishExit = $publisherProcess.ExitCode
            }
            finally { $publisherProcess.Dispose() }
            $publications.Add([ordered]@{session=$index;name=$case.name;topic=$case.topic;payload=$case.payload;
                expected=$case.outcome;sentAtUtc=$sentAt;exitCode=$publishExit;output=$publishOutput})
            $publications | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 "$run/publications.json"
            if ($publishExit -ne 0) { throw "Publication failed: $($case.name)" }
            switch ($case.outcome) {
                'accepted' {
                    $accepted++
                    $text = Wait-Log { param($t) (Lines $t ' Received ').Count -ge $accepted }
                    $line = (Lines $text ' Received ')[-1]
                    $actual = $line.Substring($line.IndexOf(' Received ') + 10) | ConvertFrom-Json
                    if ($actual.AmrId -cne $case.amrId -or $actual.Kind -cne $case.kind) { throw 'AMR/type routing mismatch.' }
                    foreach ($field in $case.model.PSObject.Properties) {
                        $actualJson = ConvertTo-Json -InputObject $actual.Model.($field.Name) -Depth 5 -Compress
                        $expectedJson = ConvertTo-Json -InputObject $field.Value -Depth 5 -Compress
                        if ($actualJson -cne $expectedJson) { throw "Model field mismatch: $($field.Name)" }
                    }
                    $diagnosticTime = ((Lines $text ' MessageDiagnostic ')[-1] -split ' ')[0]
                    if ([DateTimeOffset]$actual.ReceivedAtUtc -ne [DateTimeOffset]$diagnosticTime -or
                        [DateTimeOffset]$actual.Model.ReceivedAtUtc -ne [DateTimeOffset]$diagnosticTime) { throw 'Reception time mismatch.' }
                }
                'rejected' {
                    $rejected++
                    $text = Wait-Log { param($t) (Lines $t ' Rejected ').Count -ge $rejected }
                    if ((Lines $text ' Rejected ')[-1] -notmatch "Code=$($case.code) ") { throw 'Rejection code mismatch.' }
                }
                'ignored' { $ignored++; Start-Sleep -Milliseconds 100 }
            }
        }
        Start-Sleep -Milliseconds 300
        $text = Read-Log
        if ((Lines $text ' Received ').Count -ne $accepted -or (Lines $text ' Rejected ').Count -ne $rejected -or
            (Lines $text ' MessageDiagnostic ').Count -ne ($accepted+$rejected)) { throw 'Duplicate delivery or filter/count mismatch.' }
        if ($text.Contains('PRIVATE_COM01B_INPUT')) { throw 'Rejected or ignored payload leaked.' }
        foreach ($line in (Lines $text 'Validation=Rejected')) {
            if (!$line.Contains('Payload=<hidden>')) { throw 'Rejected preview leaked.' }
        }
        $null = [Com01BConsoleControl]::FreeConsole()
        if (![Com01BConsoleControl]::AttachConsole([uint32]$process.Id)) { throw 'AttachConsole failed.' }
        $attached = $true
        $pids = [uint32[]]::new(16)
        $count = [Com01BConsoleControl]::GetConsoleProcessList($pids,16)
        if ($count -eq 0 -or $count -gt 16) { throw 'Cannot verify isolated console.' }
        $members = @($pids[0..($count-1)])
        if (@($members | Where-Object { $_ -ne $PID -and $_ -ne $process.Id }).Count) { throw 'Unexpected console member.' }
        if (![Com01BConsoleControl]::SetConsoleCtrlHandler([IntPtr]::Zero,$true)) { throw 'Controller protection failed.' }
        if (![Com01BConsoleControl]::GenerateConsoleCtrlEvent(0,0)) { throw 'CTRL_C_EVENT failed.' }
        if (!$process.WaitForExit(8000)) { throw 'Normal shutdown timed out.' }
        $process.WaitForExit()
        $final = Read-Log
        $errorText = Get-Content $stderr -Raw
        if ($process.ExitCode -ne 0) { throw 'Console exit failure.' }
        if ($SeqUnavailable) {
            # Explicit additional scenario: only the bounded logging-loss summary is expected.
            if ($errorText -notmatch '^SeqDeliverySummary RunId=[a-zA-Z0-9_-]+ Sent=0 Failed=(\d+) Dropped=(\d+)\s*$' -or
                ([long]$Matches[1] + [long]$Matches[2]) -le 0) { throw 'Expected only a Seq loss summary.' }
        } elseif ($errorText) { throw 'Console stderr failure.' }
        if ($final -notmatch '(?s)ShutdownRequested.*Unsubscribing.*SubscriptionStopped.*Disconnecting.*Disconnected.*Disposed.*ExitCode=0') { throw 'Cleanup order mismatch.' }
        if ((Lines $final ' Received ').Count -ne $accepted) { throw 'Unexpected delivery at shutdown.' }
        $clientId = [regex]::Match($final,'ClientId=(\S+)').Groups[1].Value
        $sessions.Add([ordered]@{session=$index;clientId=$clientId;publications=$inputData.sessions[$index].Count;
            delivered=$accepted;rejected=$rejected;ignored=$ignored;signal='Windows CTRL_C_EVENT';exitCode=$process.ExitCode;
            modelFieldsPreserved=$true;receptionTimePreserved=$true;rejectedPayloadHidden=$true;consoleMembers=$members})
        $null = [Com01BConsoleControl]::SetConsoleCtrlHandler([IntPtr]::Zero,$false)
        $null = [Com01BConsoleControl]::FreeConsole()
        $attached = $false
        $process.Dispose(); $process = $null
    }
    if ($sessions[0].clientId -eq $sessions[1].clientId) { throw 'Client ID reused.' }
    [ordered]@{result='passed';completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');sessions=$sessions} |
        ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 "$run/scenario-result.json"
}
catch { $_ | Out-String | Set-Content -Encoding utf8 "$run/failure.log"; throw }
finally {
    if ($attached) {
        $null = [Com01BConsoleControl]::SetConsoleCtrlHandler([IntPtr]::Zero,$false)
        $null = [Com01BConsoleControl]::FreeConsole()
    }
    if ($null -ne $process) {
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}
