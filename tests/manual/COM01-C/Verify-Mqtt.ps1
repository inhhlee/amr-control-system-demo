param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunId,
    [switch]$Controller
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
        docker compose -f docker/compose.yaml config --quiet
        if ($LASTEXITCODE -ne 0) { throw 'Compose configuration failed.' }
        docker compose -f docker/compose.yaml ps -a > "$run/broker-state.log"
        $container = docker compose -f docker/compose.yaml ps -q mosquitto
        if ($LASTEXITCODE -ne 0 -or !$container) { throw 'Project Broker must already be running.' }
        docker inspect $container --format '{{json .Mounts}}' > "$run/broker-mounts.json"
        if ($LASTEXITCODE -ne 0) { throw 'Broker mount query failed.' }
        $dockerVersion = docker version --format '{{.Client.Version}} / {{.Server.Version}}'
        if ($LASTEXITCODE -ne 0) { throw 'Docker version query failed.' }
        $brokerVersion = docker compose -f docker/compose.yaml exec -T mosquitto mosquitto -h
        if ($LASTEXITCODE -ne 0) { throw 'Broker version query failed.' }
        [ordered]@{ startedAtUtc=$started.ToString('O'); os=[Runtime.InteropServices.RuntimeInformation]::OSDescription;
            sdk=(dotnet --version); dockerVersion=$dockerVersion; brokerVersion=$brokerVersion[0];
            endpoint='127.0.0.1:1884'; mqttnet='5.2.0.1603'; qos=0; retain=$false;
            brokerStartedOrRestartedByThisTest=$false; baseCommit=(git rev-parse HEAD); branch=(git branch --show-current) } |
            ConvertTo-Json | Set-Content -Encoding utf8 "$run/environment.json"
        $files = @(rg --files src tests -g '*.cs' -g '*.csproj' -g '*.ps1' -g '*.json') + @('global.json','AmrControlSystem.slnx')
        [ordered]@{ baseCommit=(git rev-parse HEAD); sourceState='uncommitted COM01-C console queues'; files=@($files | ForEach-Object {
            [ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}
        }) } | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 "$run/source-version.json"
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'inputs.json') -Destination "$run/inputs.json"
        $child = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList @('-NoProfile','-File', $PSCommandPath,'-RunId',$RunId,'-Controller') `
            -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput "$run/controller.stdout.log" `
            -RedirectStandardError "$run/controller.stderr.log" -PassThru
        $null = $child.Handle
        if (!$child.WaitForExit(90000)) { throw 'MQTT controller timed out.' }
        $child.WaitForExit()
        [ordered]@{exitCode=$child.ExitCode;completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')} |
            ConvertTo-Json | Set-Content -Encoding utf8 "$run/controller-result.json"
        if ($child.ExitCode -ne 0) { throw 'MQTT scenario failed; see controller/failure logs.' }
        docker compose -f docker/compose.yaml logs --no-color --timestamps --since $started.ToString('O') mosquitto *> "$run/broker.log"
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
        Write-Output "PASS: per-AMR order, independent processing, cancellation/discard and fresh session over real MQTT. Evidence: $run"
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
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Com01CConsoleControl
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
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
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
        $consoleArguments = @('--host','127.0.0.1','--port','1884')
        if($index -lt 2){$consoleArguments += @('--test-delay-amr','AMR_001','--test-delay-ms','5000')}
        $process = Start-Process -FilePath $executable -ArgumentList $consoleArguments `
            -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        $null = $process.Handle
        $null = Wait-Log { param($text) $text -match 'Subscribed TopicFilters=amr/\+/heartbeat,amr/\+/telemetry,amr/\+/alarm,amr/\+/ack QoS=0' }
        $accepted = 0; $rejected = 0; $ignored = 0
        foreach ($case in $inputData.sessions[$index]) {
            $publisher = 'com01c-pub-' + [Guid]::NewGuid().ToString('N')
            $sentAt = [DateTimeOffset]::UtcNow.ToString('O')
            # Ctrl+C 확인 뒤 콘솔에서 분리된 PowerShell에서도 네이티브 명령을 실행할 수 있게
            # 표준 입출력을 명시적으로 연결한다. ArgumentList는 JSON 따옴표도 그대로 전달한다.
            $publishInfo = [Diagnostics.ProcessStartInfo]::new('docker')
            $publishInfo.UseShellExecute = $false
            $publishInfo.CreateNoWindow = $true
            $publishInfo.RedirectStandardInput = $true
            $publishInfo.RedirectStandardOutput = $true
            $publishInfo.RedirectStandardError = $true
            foreach ($argument in @('compose','-f','docker/compose.yaml','exec','-T','mosquitto','mosquitto_pub',
                    '-h','127.0.0.1','-p','1883','-V','mqttv5','-i',$publisher,'-q','0','-t',$case.topic,'-m',$case.payload)) {
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
            if ($case.name -eq 'heartbeat-AMR_001' -and $index -lt 2) {
                $null = Wait-Log { param($t) $t.Contains('ProcessingStarted AmrId="AMR_001" Kind=heartbeat') }
            }
        }
        $acceptedInputs = @($inputData.sessions[$index] | Where-Object outcome -eq 'accepted')
        $rejected = @($inputData.sessions[$index] | Where-Object outcome -eq 'rejected').Count
        $accepted = if($index -eq 1){1}else{$acceptedInputs.Count}
        if($index -eq 1) {
            $text = Wait-Log { param($t) (Lines $t ' Received ').Count -ge 1 }
        } else {
            $text = Wait-Log { param($t) (Lines $t ' Received ').Count -ge $accepted }
        }
        $receivedLines = Lines $text ' Received '
        $received = @($receivedLines | ForEach-Object { $_.Substring($_.IndexOf(' Received ')+10) | ConvertFrom-Json })
        if($received.Count -ne $accepted){throw 'Unexpected delivered count'}
        if((Lines $text ' Rejected ').Count -ne $rejected){throw 'Unexpected validation rejection count'}
        if($index -lt 2) {
            if($received[0].AmrId -cne 'AMR_002'){throw 'AMR_002 did not finish before delayed AMR_001'}
            if($index -eq 1 -and @($received | Where-Object AmrId -eq 'AMR_001').Count){throw 'Delayed handler finished before shutdown test'}
        }
        foreach($id in @('AMR_001','AMR_002')){
            $expected = @($acceptedInputs | Where-Object amrId -eq $id)
            if($index -eq 1 -and $id -eq 'AMR_001'){$expected=@()}
            $actual = @($received | Where-Object AmrId -eq $id)
            if($actual.Count -ne $expected.Count){throw "AMR count mismatch: $id"}
            for($m=0;$m -lt $actual.Count;$m++){
                if($actual[$m].Kind -cne $expected[$m].kind){throw "Per-AMR order mismatch: $id"}
                foreach($field in $expected[$m].model.PSObject.Properties){
                    if((ConvertTo-Json -InputObject $actual[$m].Model.($field.Name) -Depth 5 -Compress) -cne
                       (ConvertTo-Json -InputObject $field.Value -Depth 5 -Compress)){throw "Field mismatch: $($field.Name)"}
                }
                $topic = 'amr/'+$id+'/'+$actual[$m].Kind
                $diagnostic = @((Lines $text ' MessageDiagnostic ') | Where-Object { $_.Contains('Topic="'+$topic+'"') -and $_.Contains('Validation=Accepted') })
                if($diagnostic.Count -ne 1){throw 'Expected one accepted diagnostic'}
                $receivedAt = [DateTimeOffset](($diagnostic[0] -split ' ')[0])
                if([DateTimeOffset]$actual[$m].ReceivedAtUtc -ne $receivedAt -or [DateTimeOffset]$actual[$m].Model.ReceivedAtUtc -ne $receivedAt){throw 'Reception timestamp changed'}
            }
        }
        if($text.Contains('PRIVATE_COM01C_INPUT')){throw 'Rejected payload leaked'}
        $null = [Com01CConsoleControl]::FreeConsole()
        if (![Com01CConsoleControl]::AttachConsole([uint32]$process.Id)) { throw 'AttachConsole failed.' }
        $attached = $true
        $pids = [uint32[]]::new(16)
        $count = [Com01CConsoleControl]::GetConsoleProcessList($pids,16)
        if ($count -eq 0 -or $count -gt 16) { throw 'Cannot verify isolated console.' }
        $members = @($pids[0..($count-1)])
        if (@($members | Where-Object { $_ -ne $PID -and $_ -ne $process.Id }).Count) { throw 'Unexpected console member.' }
        if (![Com01CConsoleControl]::SetConsoleCtrlHandler([IntPtr]::Zero,$true)) { throw 'Controller protection failed.' }
        if (![Com01CConsoleControl]::GenerateConsoleCtrlEvent(0,0)) { throw 'CTRL_C_EVENT failed.' }
        if (!$process.WaitForExit(8000)) { throw 'Normal shutdown timed out.' }
        $process.WaitForExit()
        $final = Read-Log
        if ($process.ExitCode -ne 0 -or (Get-Content $stderr -Raw)) { throw 'Console exit/stderr failure.' }
        if ($final -notmatch '(?s)ShutdownRequested.*Unsubscribing.*SubscriptionStopped.*Disconnecting.*Disconnected.*Disposed.*ExitCode=0') { throw 'Cleanup order mismatch.' }
        if ((Lines $final ' Received ').Count -ne $accepted) { throw 'Unexpected delivery at shutdown.' }
        if($final -notmatch 'QueueStopped'){throw 'Queue stop missing'}
        $queueEvents = @((Lines $final ' QueueEvent ') | ForEach-Object { $_.Substring($_.IndexOf(' QueueEvent ')+12) | ConvertFrom-Json })
        if($index -eq 1){
            $discarded = @($queueEvents | Where-Object Code -eq 'ShutdownDiscarded')
            if($discarded.Count -ne 1 -or $discarded[0].AmrId -cne 'AMR_001' -or $discarded[0].Count -ne 3){throw 'Expected three pending messages discarded'}
            if(@($queueEvents | Where-Object Code -eq 'HandlerCancelled').Count -ne 1){throw 'Expected one active handler cancelled'}
        } elseif(@($queueEvents | Where-Object { $_.Code -in @('ShutdownDiscarded','HandlerCancelled','StopTimeout','HandlerFailed') }).Count){throw 'Unexpected queue failure/discard'}
        $clientId = [regex]::Match($final,'ClientId=(\S+)').Groups[1].Value
        $sessions.Add([ordered]@{session=$index;clientId=$clientId;publications=$inputData.sessions[$index].Count;
            delivered=$accepted;rejected=$rejected;ignored=$ignored;signal='Windows CTRL_C_EVENT';exitCode=$process.ExitCode;
            modelFieldsPreserved=$true;receptionTimePreserved=$true;rejectedPayloadHidden=$true;consoleMembers=$members;queueEvents=$queueEvents;perAmrOrder=$true;secondAmrFirst=($index -lt 2)})
        $null = [Com01CConsoleControl]::SetConsoleCtrlHandler([IntPtr]::Zero,$false)
        $null = [Com01CConsoleControl]::FreeConsole()
        $attached = $false
        $process.Dispose(); $process = $null
    }
    if (@($sessions.clientId | Select-Object -Unique).Count -ne 3) { throw 'Client ID reused.' }
    [ordered]@{result='passed';completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');sessions=$sessions} |
        ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 "$run/scenario-result.json"
}
catch { $_ | Out-String | Set-Content -Encoding utf8 "$run/failure.log"; throw }
finally {
    if ($attached) {
        $null = [Com01CConsoleControl]::SetConsoleCtrlHandler([IntPtr]::Zero,$false)
        $null = [Com01CConsoleControl]::FreeConsole()
    }
    if ($null -ne $process) {
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}


