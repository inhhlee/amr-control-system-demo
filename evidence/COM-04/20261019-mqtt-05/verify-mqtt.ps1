# COM04-A의 이번 실제 실행 전용. 별도 숨김 PowerShell에서 실행한다.
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repository
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'console.stdout.log')) {
    throw 'Existing evidence must be preserved. Use a new execution ID for another run.'
}
$dockerCli = (Get-Command docker -ErrorAction Stop).Source
$started = [DateTimeOffset]::UtcNow
$stdout = Join-Path $PSScriptRoot 'console.stdout.log'
$stderr = Join-Path $PSScriptRoot 'console.stderr.log'
$process = $null
$attached = $false
$publications = [System.Collections.Generic.List[object]]::new()

function Wait-Log([string]$pattern) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    do {
        $content = if (Test-Path -LiteralPath $stdout) { Get-Content -Raw -LiteralPath $stdout } else { '' }
        if ($content -match $pattern) { return $content }
        if ($process.HasExited) { throw "Console exited before expected event: $pattern" }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Expected event timed out: $pattern"
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Com04ConsoleControl
{
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GenerateConsoleCtrlEvent(uint signal, uint groupId);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern uint GetConsoleProcessList([Out] uint[] processList, uint count);
}
'@

try {
    & $dockerCli compose -f docker/compose.yaml ps -a *> (Join-Path $PSScriptRoot 'broker-state.log')
    if ($LASTEXITCODE -ne 0) { throw 'Broker state query failed.' }
    & $dockerCli compose -f docker/compose.yaml config --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Compose configuration check failed.' }
    $containerId = & $dockerCli compose -f docker/compose.yaml ps -q mosquitto
    if ($LASTEXITCODE -ne 0 -or !$containerId) { throw 'Project Broker is not running.' }
    & $dockerCli inspect $containerId --format '{{json .Mounts}}' > (Join-Path $PSScriptRoot 'broker-mounts.json')
    if ($LASTEXITCODE -ne 0) { throw 'Broker mount query failed.' }
    $dockerVersion = & $dockerCli version --format '{{.Client.Version}} / {{.Server.Version}}'
    if ($LASTEXITCODE -ne 0) { throw 'Docker version query failed.' }
    $brokerVersion = & $dockerCli compose -f docker/compose.yaml exec -T mosquitto mosquitto -h
    if ($LASTEXITCODE -ne 0) { throw 'Mosquitto version query failed.' }
    [ordered]@{ startedAtUtc=$started.ToString('O'); os=[System.Runtime.InteropServices.RuntimeInformation]::OSDescription;
        sdk=(dotnet --version); dockerCli=$dockerCli; dockerVersion=$dockerVersion; brokerVersion=$brokerVersion[0];
        mqttnet='5.2.0.1603'; endpoint='127.0.0.1:1884'; qos=0; retain=$false; brokerStartedOrRestartedByThisTest=$false;
        workingDirectory=$repository; baseCommit=(git rev-parse HEAD); branch=(git branch --show-current) } |
        ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $PSScriptRoot 'environment.json')
    $sourceFiles = @(rg --files src tests -g '*.cs' -g '*.csproj') + @('global.json','AmrControlSystem.slnx')
    $hashes = foreach ($sourceFile in $sourceFiles) {
        [ordered]@{ path=$sourceFile; sha256=(Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash }
    }
    [ordered]@{ baseCommit=(git rev-parse HEAD); sourceState='uncommitted COM04-A including preserved user comment deletion'; files=$hashes } |
        ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $PSScriptRoot 'source-version.json')

    $executable = Join-Path $repository 'src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.exe'
    $consoleArguments = @('--host','127.0.0.1','--port','1884','--show-payload')
    $process = Start-Process -FilePath $executable -ArgumentList $consoleArguments -WorkingDirectory $repository `
        -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $null = $process.Handle
    $null = Wait-Log 'Subscribed TopicFilter=amr/\+/heartbeat QoS=0'
    $inputs = @(
        @{name='missing-ts'; payload='{"id":"AMR_001","status":"OK"}'; expected='Rejected Code=MissingField Field=\$\.ts'},
        @{name='valid-after-missing'; payload='{"id":"AMR_001","ts":1791195767000,"status":"OK"}'; expected='Received Heartbeat .*ts=1791195767000 '},
        @{name='id-mismatch'; payload='{"id":"AMR_002","ts":1791195767001,"status":"OK"}'; expected='Rejected Code=IdMismatch Field=\$\.id'},
        @{name='long-valid-after-mismatch'; payload=(' ' * 300 + '{"id":"AMR_001","ts":1791195767002,"status":"OK"}'); expected='Received Heartbeat .*ts=1791195767002 '}
    )
    foreach ($inputCase in $inputs) {
        $publisherId = 'com04-pub-' + [Guid]::NewGuid().ToString('N')
        $publishArgs = @('compose','-f','docker/compose.yaml','exec','-T','mosquitto','mosquitto_pub',
            '-h','127.0.0.1','-p','1883','-V','mqttv5','-i',$publisherId,'-q','0',
            '-t','amr/AMR_001/heartbeat','-m',$inputCase.payload)
        $sentAt = [DateTimeOffset]::UtcNow
        $publishOutput = & $dockerCli @publishArgs 2>&1 | Out-String
        $publishExit = $LASTEXITCODE
        $publications.Add([ordered]@{name=$inputCase.name; sentAtUtc=$sentAt.ToString('O'); topic='amr/AMR_001/heartbeat';
            payload=$inputCase.payload; payloadBytes=[Text.Encoding]::UTF8.GetByteCount($inputCase.payload);
            expected=$inputCase.expected; clientId=$publisherId; qos=0; retain=$false; exitCode=$publishExit; output=$publishOutput})
        $publications | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $PSScriptRoot 'publications.json')
        if ($publishExit -ne 0) { throw 'Publication failed.' }
        $null = Wait-Log $inputCase.expected
    }
    Start-Sleep -Milliseconds 500
    $receivedLog = Get-Content -Raw -LiteralPath $stdout
    $lines = $receivedLog -split '\r?\n'
    $diagnostics = @($lines | Where-Object { $_ -match ' HeartbeatDiagnostic ' })
    $received = @($lines | Where-Object { $_ -match ' Received Heartbeat ' })
    $rejected = @($lines | Where-Object { $_ -match ' Rejected ' })
    if ($diagnostics.Count -ne 4 -or $received.Count -ne 2 -or $rejected.Count -ne 2) { throw 'Unexpected delivery/rejection counts.' }
    if (@($diagnostics | Where-Object { $_ -match 'Validation=Rejected' -and $_ -notmatch 'Payload=<hidden>' }).Count) { throw 'Rejected input preview leaked.' }
    if ($diagnostics[3] -notmatch 'PayloadBytes=349 .*PayloadTruncated=True Validation=Accepted') { throw 'Full long payload was not accepted.' }
    for ($index = 0; $index -lt 2; $index++) {
        $callbackTime = ($diagnostics[1 + 2 * $index] -split ' ')[0]
        if ($received[$index] -notmatch ('ReceivedAtUtc=' + [regex]::Escape($callbackTime))) { throw 'Callback reception time was not preserved.' }
        if ($received[$index] -notmatch 'Id="AMR_001" .*status="OK"') { throw 'Model fields were not preserved.' }
    }

    $null = [Com04ConsoleControl]::FreeConsole()
    if (![Com04ConsoleControl]::AttachConsole([uint32]$process.Id)) { throw 'AttachConsole failed.' }
    $attached = $true
    $consolePids = [uint32[]]::new(16)
    $count = [Com04ConsoleControl]::GetConsoleProcessList($consolePids, 16)
    if ($count -eq 0 -or $count -gt 16) { throw 'Cannot verify isolated console.' }
    $members = @($consolePids[0..($count - 1)])
    if (@($members | Where-Object { $_ -ne $PID -and $_ -ne $process.Id }).Count) { throw 'Unexpected console member; no Ctrl+C sent.' }
    if (![Com04ConsoleControl]::SetConsoleCtrlHandler([IntPtr]::Zero, $true)) { throw 'Cannot protect controller.' }
    $signalAt = [DateTimeOffset]::UtcNow
    if (![Com04ConsoleControl]::GenerateConsoleCtrlEvent(0, 0)) { throw 'Ctrl+C generation failed.' }
    if (!$process.WaitForExit(10000)) { throw 'Normal shutdown timed out.' }
    $process.WaitForExit()
    $finalLog = Get-Content -Raw -LiteralPath $stdout
    $errors = Get-Content -Raw -LiteralPath $stderr
    if ($process.ExitCode -ne 0 -or $errors) { throw 'Console did not exit cleanly.' }
    if ($finalLog -notmatch '(?s)ShutdownRequested.*Unsubscribing.*SubscriptionStopped.*Disconnecting.*Disconnected.*Disposed.*ExitCode=0') { throw 'Cleanup order mismatch.' }
    $clientId = [regex]::Match($finalLog, 'ClientId=(\S+)').Groups[1].Value
    # Broker 로그 수집은 Ctrl+C 콘솔에 연결하지 않은 부모 프로세스에서 수행한다.
    [ordered]@{ result='passed'; startedAtUtc=$started.ToString('O'); finishedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');
        publications=4; diagnostics=4; rejections=2; deliveredModels=2; rejectionCodes=@('MissingField','IdMismatch');
        receivedTimestamps=@(1791195767000L,1791195767002L); receptionTimePreserved=$true;
        rejectedPayloadHidden=$true; longPayloadBytes=349; signal='Windows CTRL_C_EVENT'; signalAtUtc=$signalAt.ToString('O');
        consoleMembers=$members; processId=$process.Id; clientId=$clientId; exitCode=$process.ExitCode; stderr=$errors } |
        ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $PSScriptRoot 'scenario-result.json')
    Write-Output 'PASS: 2 rejected inputs, 2 subsequent normal deliveries, original fields/reception times, Ctrl+C exit 0.'
}
catch {
    $_ | Out-String | Set-Content -Encoding utf8 (Join-Path $PSScriptRoot 'failure.log')
    throw
}
finally {
    if ($attached) {
        $null = [Com04ConsoleControl]::SetConsoleCtrlHandler([IntPtr]::Zero, $false)
        $null = [Com04ConsoleControl]::FreeConsole()
    }
    if ($null -ne $process) {
        if (!$process.HasExited) {
            $process.Kill()
            $process.WaitForExit()
            'Test failure cleanup: terminated only the owned console process.' |
                Add-Content -LiteralPath (Join-Path $PSScriptRoot 'failure.log')
        }
        $process.Dispose()
    }
}

