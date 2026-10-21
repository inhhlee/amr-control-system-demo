param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,80}$')][string]$RunId,
    [switch]$MeasureTimeout,
    [ValidateRange(0,2)][int]$BlockDelaySeconds = 0,
    [switch]$Controller,
    [int]$ProxyPort
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$run = Join-Path $repository "evidence/COM-03/$RunId"
Set-Location -LiteralPath $repository

if ($Controller) {
    # C1's isolated-console control pattern; only this new child receives CTRL_C_EVENT.
    $env:AMR_SEQ_URL = $null
    $env:AMR_SEQ_API_KEY = $null
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Com03C3Control {
    [DllImport("kernel32.dll")] public static extern bool FreeConsole();
    [DllImport("kernel32.dll")] public static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll")] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll")] public static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
    [DllImport("kernel32.dll")] public static extern uint GetConsoleProcessList([Out] uint[] list, uint count);
}
'@
    $product = $null
    $attached = $false
    try {
        $product = Start-Process -FilePath "$repository/src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.exe" -ArgumentList @('--host','127.0.0.1','--port',"$ProxyPort") -WindowStyle Hidden -RedirectStandardOutput "$run/console.stdout.log" -RedirectStandardError "$run/console.stderr.log" -PassThru
        $null = $product.Handle
        [ordered]@{productPid=$product.Id} | ConvertTo-Json | Set-Content -LiteralPath "$run/console.process.json" -Encoding utf8
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        do {
            $text = [string](Get-Content -LiteralPath "$run/console.stdout.log" -Raw)
            if ($text -and $text.Contains('WaitingForShutdown')) { break }
            if ($product.HasExited) { throw 'Console exited before subscription.' }
            Start-Sleep -Milliseconds 25
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if (!$text -or !$text.Contains('WaitingForShutdown') -or $text -notmatch 'ClientId=(\S+).*KeepAliveSeconds=([0-9.]+)') { throw 'Approved/subscribed session not confirmed.' }
        [ordered]@{clientId=$Matches[1];keepAliveSeconds=$Matches[2];productPid=$product.Id;proxyPort=$ProxyPort;readyAtUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath "$run/console.ready.json" -Encoding utf8
        if ($MeasureTimeout) {
            # No Ctrl+C or response resume: the product must detect and exit itself.
            if (!$product.WaitForExit(18000)) { throw 'C3b product exceeded the observation watchdog.' }
            $product.WaitForExit()
            $exitObserved=[DateTimeOffset]::UtcNow
            $text=[string](Get-Content -LiteralPath "$run/console.stdout.log" -Raw)
            $lost=[regex]::Matches($text,'(?m)^(?<at>\S+) ConnectionLost Reason=(?<reason>\S+) ClientWasConnected=(?<was>\S+) ExceptionType=(?<exception>\S+) SocketError=(?<socket>\S+) ClientId=(?<id>\S+)\r?$')
            if ($lost.Count -ne 1 -or $lost[0].Groups['was'].Value -ne 'True' -or $product.ExitCode -ne 1 -or [string](Get-Content -LiteralPath "$run/console.stderr.log" -Raw) -or $text -notmatch '(?s) Connected.*Subscribed.*ConnectionLost.*ShutdownRequested Reason=ConnectionLost.*QueueStopped.*SubscriptionStopped.*Disposed.*ExitCode=1') { throw 'C3b detection, reason, cleanup or exit code mismatch.' }
            [ordered]@{clientId=$lost[0].Groups['id'].Value;productPid=$product.Id;exitCode=$product.ExitCode;detectedAtUtc=$lost[0].Groups['at'].Value;reason=$lost[0].Groups['reason'].Value;exceptionType=$lost[0].Groups['exception'].Value;socketError=$lost[0].Groups['socket'].Value;exitObservedAtUtc=$exitObserved.ToString('O');cleanupCompleted=$true;ctrlCSent=$false} | ConvertTo-Json | Set-Content -LiteralPath "$run/console.session.json" -Encoding utf8
            return
        }
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(25)
        while (!(Test-Path -LiteralPath "$run/console.stop")) {
            if ($product.HasExited) { throw 'Console exited before C3a environment observation completed.' }
            if ([DateTimeOffset]::UtcNow -gt $deadline) { throw 'C3a controller stop signal missing.' }
            Start-Sleep -Milliseconds 25
        }
        $null = [Com03C3Control]::FreeConsole()
        if (![Com03C3Control]::AttachConsole([uint32]$product.Id)) { throw 'AttachConsole failed.' }
        $attached = $true
        $members = [uint32[]]::new(16)
        $count = [Com03C3Control]::GetConsoleProcessList($members,16)
        if ($count -eq 0 -or $count -gt 16 -or @($members[0..($count-1)] | Where-Object { $_ -ne $PID -and $_ -ne $product.Id }).Count) { throw 'Console isolation not confirmed; no Ctrl+C sent.' }
        if (![Com03C3Control]::SetConsoleCtrlHandler([IntPtr]::Zero,$true)) { throw 'Controller protection failed.' }
        if (![Com03C3Control]::GenerateConsoleCtrlEvent(0,0)) { throw 'CTRL_C_EVENT failed.' }
        if (!$product.WaitForExit(5000)) { throw 'C3a normal cleanup exceeded 5s.' }
        $product.WaitForExit()
        $text = [string](Get-Content -LiteralPath "$run/console.stdout.log" -Raw)
        if ($product.ExitCode -ne 0 -or [string](Get-Content -LiteralPath "$run/console.stderr.log" -Raw) -or $text -notmatch '(?s) Connected.*Subscribed.*ShutdownRequested Reason=UserCanceled.*SubscriptionStopped.*Disconnected.*Disposed.*ExitCode=0' -or $text.Contains('ConnectionLost')) { throw 'Unexpected C3a cleanup; see console logs.' }
        [ordered]@{productPid=$product.Id;exitCode=$product.ExitCode;normalCleanup=$true;completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath "$run/console.session.json" -Encoding utf8
    }
    catch { $_ | Out-String | Set-Content -LiteralPath "$run/controller-failure.log" -Encoding utf8; throw }
    finally {
        if ($attached) { $null=[Com03C3Control]::SetConsoleCtrlHandler([IntPtr]::Zero,$false); $null=[Com03C3Control]::FreeConsole() }
        if ($null -ne $product) { if (!$product.HasExited) { $product.Kill(); $product.WaitForExit() }; $product.Dispose() }
    }
    exit 0
}

if ((git branch --show-current) -ne 'feature/6-mqtt-keepalive') { throw 'Use the existing Issue #6 branch.' }
if (!$MeasureTimeout -and $BlockDelaySeconds -ne 0) { throw 'BlockDelaySeconds is only for C3b measurement.' }
if (Test-Path -LiteralPath $run) { throw 'RunId exists; choose a new execution ID.' }
New-Item -ItemType Directory -Path $run | Out-Null
$docker = (Get-Command docker -ErrorAction SilentlyContinue).Source
if (!$docker) { $docker=Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe' }
$container=$null; $proxy=$null; $console=$null; $failure=$null; $observation=$null
$stage=if ($MeasureTimeout) { 'c3b' } else { 'c3a' }
$name=('amr-'+$stage+'-'+$RunId).ToLowerInvariant()
$started=[DateTimeOffset]::UtcNow
function Invoke-Docker([string[]]$Arguments) {
    $at=[DateTimeOffset]::UtcNow
    $result=@(& $docker @Arguments 2>&1); $code=$LASTEXITCODE
    [ordered]@{program='docker';arguments=$Arguments;atUtc=$at.ToString('O');exitCode=$code} | ConvertTo-Json -Compress | Add-Content -LiteralPath "$run/commands.jsonl" -Encoding utf8
    if ($code -ne 0) { throw "Docker failed ($code): $($Arguments -join ' ')" }
    return $result
}
function Read-Json([string]$Path) {
    if (Test-Path -LiteralPath $Path) { try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -DateKind String } catch { return $null } }
    return $null
}
function Read-ProxyEvents {
    return @(Get-Content -LiteralPath "$run/proxy.jsonl" -ErrorAction SilentlyContinue | ForEach-Object { try { $_ | ConvertFrom-Json -DateKind String } catch { } })
}
function Apply-Proxy([int]$Sequence,[string]$Action) {
    "$Sequence $Action" | Set-Content -LiteralPath "$run/proxy.control.tmp" -Encoding ascii
    Move-Item -LiteralPath "$run/proxy.control.tmp" -Destination "$run/proxy.control" -Force
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(2)
    do {
        $ack=@(Read-ProxyEvents | Where-Object { $_.eventType -eq 'ControlApplied' -and $_.data.sequence -eq $Sequence })
        if ($ack.Count -eq 1) { return $ack[0] }
        if ($proxy.HasExited) { throw 'Proxy exited before control acknowledgement.' }
        Start-Sleep -Milliseconds 20
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Proxy did not acknowledge $Action."
}
try {
    if ($MeasureTimeout) {
        # Persist before starting a fault; never adjust these limits to a measured result.
        [ordered]@{recordedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');mqttnet='5.2.0.1603';sourceCommit='14463d1ef9f1f119514185c1832905b91bff827d';sourceUrl='https://github.com/dotnet/MQTTnet/blob/14463d1ef9f1f119514185c1832905b91bff827d/Source/MQTTnet/MqttClient.cs#L935';keepAliveSeconds=5;pingResponseTimeoutSeconds=2.5;keepAlivePollingSeconds=0.25;earlyToleranceSeconds=0.1;schedulingAndCallbackAllowanceSeconds=1.0;lastOutgoingToDetectMinimumSeconds=7.4;lastOutgoingToDetectMaximumSeconds=8.75;firstDiscardToDetectMinimumSeconds=2.4;firstDiscardToDetectMaximumSeconds=3.5;blockDelaySeconds=$BlockDelaySeconds;maximumBlockPhaseSeconds=3;faultWatchdogSeconds=15;exitAfterDetectionMaximumSeconds=5;wallClockDriftMaximumMilliseconds=100;origin='proxy ControlApplied(block), not command-file creation';businessPublisher='none';responseResumeAllowed=$false;ctrlCAllowed=$false;expectedExitCode=1;scope='one Windows loopback test, not an operational SLA'} | ConvertTo-Json | Set-Content -LiteralPath "$run/criteria.json" -Encoding utf8
    }
    $dockerVersion=Invoke-Docker -Arguments @('version','--format','{{.Client.Version}} / {{.Server.Version}}')
    Invoke-Docker -Arguments @('ps','-a','--format','{{json .}}') | Set-Content -LiteralPath "$run/containers-before.jsonl" -Encoding utf8
    $image='eclipse-mosquitto:2.1.2-alpine'
    $imageId=[string](Invoke-Docker -Arguments @('image','inspect',$image,'--format','{{.Id}}'))
    # Preload OS socket inspection before the brief blocked observation.
    Import-Module NetTCPIP
    $null=Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue
    $files=@(git ls-files src tests global.json AmrControlSystem.slnx) + @('tests/manual/COM03-C/Verify-ResponseGate.ps1','tools/COM-03/ResponseGateProxy.cs','tools/COM-03/Start-ResponseGateProxy.ps1')
    @($files | Select-Object -Unique | ForEach-Object { [ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash} }) | ConvertTo-Json | Set-Content -LiteralPath "$run/source-version.json" -Encoding utf8
    & dotnet build src/Amr.Communication.Console/Amr.Communication.Console.csproj --no-restore --verbosity minimal *> "$run/build.log"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    @('Amr.Communication.Console.exe','Amr.Communication.Console.dll','Amr.Communication.dll','MQTTnet.dll') | ForEach-Object { [ordered]@{file=$_;sha256=(Get-FileHash -LiteralPath "$repository/src/Amr.Communication.Console/bin/Debug/net10.0/$_").Hash} } | ConvertTo-Json | Set-Content -LiteralPath "$run/binary-version.json" -Encoding utf8
    @'
listener 1883 0.0.0.0
allow_anonymous true
persistence false
log_dest stdout
log_type all
connection_messages true
log_timestamp true
'@ | Set-Content -LiteralPath "$run/broker.conf" -Encoding ascii
    $container=[string](Invoke-Docker -Arguments @('run','--detach','--pull','never','--name',$name,'--label',"amr.$stage.run=$RunId",'--publish','127.0.0.1::1883','--mount',"type=bind,source=$run/broker.conf,target=/mosquitto/config/mosquitto.conf,readonly",$image))
    if ($container -notmatch '^[a-f0-9]{64}$') { throw 'Test container ID not confirmed.' }
    $mapping=[string](Invoke-Docker -Arguments @('port',$container,'1883/tcp'))
    if ($mapping -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Loopback dynamic Broker port not confirmed.' }
    $brokerPort=[int]$Matches[1]
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(10)
    do {
        $brokerReady=(Invoke-Docker -Arguments @('logs','--timestamps',$container)) -join "`n"
        if ($brokerReady -match 'mosquitto version .* running') { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($brokerReady -notmatch 'mosquitto version .* running') { throw 'Broker did not become ready.' }
    $proxy=Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList @('-NoProfile','-File',"$repository/tools/COM-03/Start-ResponseGateProxy.ps1",'-BrokerPort',"$brokerPort",'-RunDirectory',$run) -WindowStyle Hidden -RedirectStandardOutput "$run/proxy.stdout.log" -RedirectStandardError "$run/proxy.stderr.log" -PassThru
    $null=$proxy.Handle
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        $proxyReady=Read-Json "$run/proxy.ready.json"
        if ($proxyReady.port) { break }
        if ($proxy.HasExited) { throw 'Proxy startup failed.' }
        Start-Sleep -Milliseconds 25
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if (!$proxyReady.port -or $proxyReady.processId -ne $proxy.Id) { throw 'Proxy listener/PID not confirmed.' }
    $controllerArguments=@('-NoProfile','-File',$PSCommandPath,'-RunId',$RunId,'-Controller','-ProxyPort',"$($proxyReady.port)")
    if ($MeasureTimeout) { $controllerArguments+='-MeasureTimeout' }
    $console=Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList $controllerArguments -WindowStyle Hidden -RedirectStandardOutput "$run/controller.stdout.log" -RedirectStandardError "$run/controller.stderr.log" -PassThru
    $null=$console.Handle
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(12)
    do {
        $ready=Read-Json "$run/console.ready.json"
        if ($ready.clientId) { break }
        if ($console.HasExited) { throw 'Console controller startup failed.' }
        Start-Sleep -Milliseconds 25
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if (!$ready.clientId) { throw 'Proxy-mediated subscription not confirmed.' }
    if ([double]$ready.keepAliveSeconds -ne 5) { throw 'The applied KeepAlive differs from this test contract.' }
    $before=Read-ProxyEvents
    if (@($before | Where-Object { $_.eventType -eq 'Forwarded' -and $_.data.direction -eq 'broker-to-client' }).Count -eq 0 -or @($before | Where-Object eventType -eq SessionOpened).Count -ne 1) { throw 'Normal response forwarding not confirmed.' }
    [ordered]@{runId=$RunId;stage=$stage;startedAtUtc=$started.ToString('O');branch=(git branch --show-current);baseCommit=(git rev-parse HEAD);sdk=(dotnet --version);powershell=$PSVersionTable.PSVersion.ToString();os=[Runtime.InteropServices.RuntimeInformation]::OSDescription;docker=$dockerVersion;image=$image;imageId=$imageId;containerId=$container;containerName=$name;brokerEndpoint="127.0.0.1:$brokerPort";proxyEndpoint="127.0.0.1:$($proxyReady.port)";proxyPid=$proxy.Id;mqttnet='5.2.0.1603';keepAliveSeconds=$ready.keepAliveSeconds;businessPublisher='none';seqEnabled=$false;automaticReconnect=$false;blockDelaySeconds=$BlockDelaySeconds;responseDiscardObservationUpperBoundSeconds=9;criteriaFile=if($MeasureTimeout){'criteria.json'}else{$null}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$run/environment.json" -Encoding utf8
    if ($BlockDelaySeconds) { Start-Sleep -Seconds $BlockDelaySeconds }
    $blockRequestedAt=[DateTimeOffset]::UtcNow
    $faultWatch=[Diagnostics.Stopwatch]::StartNew()
    $block=Apply-Proxy 1 'block'
    $blockAckObservedMs=$faultWatch.Elapsed.TotalMilliseconds
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(9)
    do {
        $events=Read-ProxyEvents
        $drops=@($events | Where-Object eventType -eq Dropped)
        if ($drops.Count) { break }
        if ($proxy.HasExited -or $console.HasExited) { throw 'Session ended before response discard observation.' }
        Start-Sleep -Milliseconds 20
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if (!$drops.Count) { throw 'No actual Broker response discarded.' }
    $socketAt=[DateTimeOffset]::UtcNow
    $sockets=@(Get-NetTCPConnection -State Established -OwningProcess @($proxy.Id,$ready.productPid) -ErrorAction SilentlyContinue | Select-Object LocalAddress,LocalPort,RemoteAddress,RemotePort,State,OwningProcess)
    [ordered]@{atUtc=$socketAt.ToString('O');connections=$sockets} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$run/sockets-blocked.json" -Encoding utf8
    if (@($sockets | Where-Object { $_.OwningProcess -eq $proxy.Id -and $_.LocalPort -eq $proxyReady.port }).Count -ne 1 -or @($sockets | Where-Object { $_.OwningProcess -eq $proxy.Id -and $_.RemotePort -eq $brokerPort }).Count -ne 1 -or @($sockets | Where-Object { $_.OwningProcess -eq $ready.productPid -and $_.RemotePort -eq $proxyReady.port }).Count -ne 1) { throw 'Both proxy TCP legs and product socket must remain Established while blocked.' }
    if ($MeasureTimeout) {
        do {
            $text=[string](Get-Content -LiteralPath "$run/console.stdout.log" -Raw)
            if ($text -match '(?m)^\S+ ConnectionLost Reason=') { break }
            Start-Sleep -Milliseconds 20
        } while ($faultWatch.Elapsed.TotalSeconds -lt 15)
        $detectObservedMs=$faultWatch.Elapsed.TotalMilliseconds
        $detectObservedAt=[DateTimeOffset]::UtcNow
        if (!$text -or $text -notmatch '(?m)^\S+ ConnectionLost Reason=') { throw 'No autonomous C3b detection within 15s.' }
    }
    else {
        $resume=Apply-Proxy 2 'resume'
        # C3a ends before a timeout; C3b never resumes or injects Ctrl+C.
        New-Item -ItemType File -Path "$run/console.stop" | Out-Null
    }
    if (!$console.WaitForExit(10000)) { throw 'Console controller did not finish.' }
    $console.WaitForExit()
    if ($console.ExitCode -ne 0) { throw 'Console scenario failed; see controller and product logs.' }
    if (!$proxy.WaitForExit(5000)) { if ($MeasureTimeout) { throw 'Proxy did not exit after client EOF.' }; $null=Apply-Proxy 3 'stop'; if (!$proxy.WaitForExit(5000)) { throw 'Proxy did not stop.' } }
    $proxy.WaitForExit()
    if ($proxy.ExitCode -ne 0 -or [string](Get-Content -LiteralPath "$run/proxy.stderr.log" -Raw)) { throw 'Proxy failed.' }
    $events=Read-ProxyEvents
    $endElapsed=if ($MeasureTimeout) { (@($events | Where-Object eventType -eq Eof)[0]).elapsedMs } else { $resume.elapsedMs }
    $during=@($events | Where-Object { $_.elapsedMs -ge $block.elapsedMs -and $_.elapsedMs -lt $endElapsed })
    if (@($during | Where-Object { $_.eventType -eq 'Forwarded' -and $_.data.direction -eq 'broker-to-client' }).Count -or @($during | Where-Object eventType -eq Eof).Count -or @($during | Where-Object { $_.eventType -eq 'Forwarded' -and $_.data.direction -eq 'client-to-broker' }).Count -eq 0) { throw 'Block must preserve outgoing traffic and TCP, with no incoming forwarding.' }
    $brokerLogs=@(Invoke-Docker -Arguments @('logs','--timestamps',$container))
    $brokerLogs | Set-Content -LiteralPath "$run/broker.log" -Encoding utf8
    $cid=[regex]::Escape($ready.clientId)
    if (($brokerLogs -join "`n") -notmatch "Sending PINGRESP to $cid(?:\s|$)" -or @($brokerLogs | Where-Object { $_ -match 'Received PUBLISH|Sending PUBLISH' }).Count) { throw 'Same-client PINGRESP / no-business-message Broker evidence missing.' }
    if ($MeasureTimeout) {
        $criteria=Read-Json "$run/criteria.json"
        $session=Read-Json "$run/console.session.json"
        $lastOutgoing=@($events | Where-Object { $_.eventType -eq 'Forwarded' -and $_.data.direction -eq 'client-to-broker' -and $_.elapsedMs -lt $block.elapsedMs })[-1]
        $firstEof=@($events | Where-Object eventType -eq Eof)[0]
        $phase=($block.elapsedMs-$lastOutgoing.elapsedMs)/1000
        $detected=[DateTimeOffset]::Parse($session.detectedAtUtc)
        $blockToDetect=($detected-[DateTimeOffset]::Parse($block.atUtc)).TotalSeconds
        $lastSendToDetect=($detected-[DateTimeOffset]::Parse($lastOutgoing.atUtc)).TotalSeconds
        $discardToDetect=($detected-[DateTimeOffset]::Parse($drops[0].atUtc)).TotalSeconds
        $discardToEof=($firstEof.elapsedMs-$drops[0].elapsedMs)/1000
        $exitAfterDetection=([DateTimeOffset]::Parse($session.exitObservedAtUtc)-$detected).TotalSeconds
        $drift=($detectObservedAt-$blockRequestedAt).TotalMilliseconds-$detectObservedMs
        $brokerState=((Invoke-Docker -Arguments @('inspect',$container,'--format','{{json .State}}')) -join "`n") | ConvertFrom-Json
        [ordered]@{lastOutgoing=$lastOutgoing;block=$block;firstDiscard=$drops[0];firstEof=$firstEof;console=$session;phaseSeconds=$phase;blockToDetectSeconds=$blockToDetect;expectedBlockToDetectMinimumSeconds=7.4-$phase;expectedBlockToDetectMaximumSeconds=8.75-$phase;lastOutgoingToDetectSeconds=$lastSendToDetect;firstDiscardToDetectSeconds=$discardToDetect;firstDiscardToEofMonotonicSeconds=$discardToEof;exitAfterDetectionSeconds=$exitAfterDetection;blockRequestAtUtc=$blockRequestedAt.ToString('O');blockAckObservedMs=$blockAckObservedMs;detectionObservedMs=$detectObservedMs;monotonicBlockToObservationMinimumMs=$detectObservedMs-$blockAckObservedMs;monotonicBlockToObservationMaximumMs=$detectObservedMs;wallVsMonotonicDriftMs=$drift;brokerStillRunning=$brokerState.Running} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath "$run/measurement.json" -Encoding utf8
        if ($session.clientId -ne $ready.clientId -or $phase -lt 0 -or $phase -gt $criteria.maximumBlockPhaseSeconds -or $lastSendToDetect -lt $criteria.lastOutgoingToDetectMinimumSeconds -or $lastSendToDetect -gt $criteria.lastOutgoingToDetectMaximumSeconds -or $discardToDetect -lt $criteria.firstDiscardToDetectMinimumSeconds -or $discardToDetect -gt $criteria.firstDiscardToDetectMaximumSeconds -or $discardToEof -lt 2.4 -or $discardToEof -gt 3.5 -or $exitAfterDetection -lt 0 -or $exitAfterDetection -gt 5 -or [Math]::Abs($drift) -gt 100 -or $firstEof.data.direction -ne 'client-to-broker' -or !$brokerState.Running -or @($events | Where-Object { $_.eventType -eq 'ControlApplied' -and $_.data.action -ne 'block' }).Count) { throw 'C3b timing, clock, autonomous disconnect or Broker condition failed; see measurement.json.' }
        $observation=[ordered]@{result='passed';scope='C3b actual response suppression detection';session=$ready;measurement=(Read-Json "$run/measurement.json");blockedBytes=($during | Where-Object eventType -eq Dropped | ForEach-Object { $_.data.bytes } | Measure-Object -Sum).Sum;responseForwardsAfterBlock=0;ctrlCSent=$false;responseResumed=$false;brokerStoppedBeforeDetection=$false;proxyExitedOnClientEof=$true}
    }
    else { $observation=[ordered]@{result='passed';scope='C3a environment only; no timeout/detection verdict';session=$ready;block=$block;firstDiscard=$drops[0];resume=$resume;blockedBytes=($during | Where-Object eventType -eq Dropped | ForEach-Object { $_.data.bytes } | Measure-Object -Sum).Sum;proxyTcpLegsEstablished=2;productSocketEstablished=$true;blockedIncomingForwards=0;outgoingForwardingContinued=$true;console=(Read-Json "$run/console.session.json")} }
}
catch { $failure=$_; $_ | Out-String | Set-Content -LiteralPath "$run/runner-failure.log" -Encoding utf8 }
finally {
    foreach ($child in @($console,$proxy)) {
        if ($null -ne $child) { if (!$child.HasExited) { $child.Kill($true); $child.WaitForExit() }; $child.Dispose() }
    }
    $cleanupErrors = [Collections.Generic.List[object]]::new()
    # Diagnostic failure must not prevent cleanup of the confirmed owned ID.
    if ($container -match '^[a-f0-9]{64}$') {
        try { Invoke-Docker -Arguments @('logs','--timestamps',$container) | Set-Content -LiteralPath "$run/broker-final.log" -Encoding utf8 }
        catch { $cleanupErrors.Add($_); if (!$failure) { $failure=$_ } }
        try { Invoke-Docker -Arguments @('stop','--time','5',$container) | Set-Content -LiteralPath "$run/broker-stop.log" -Encoding utf8 }
        catch { $cleanupErrors.Add($_); if (!$failure) { $failure=$_ } }
        try { Invoke-Docker -Arguments @('rm',$container) | Set-Content -LiteralPath "$run/broker-remove.log" -Encoding utf8 }
        catch { $cleanupErrors.Add($_); if (!$failure) { $failure=$_ } }
    }
    try {
        $after=@(Invoke-Docker -Arguments @('ps','-a','--format','{{json .}}'))
        $after | Set-Content -LiteralPath "$run/containers-after.jsonl" -Encoding utf8
        $prior=@(Get-Content -LiteralPath "$run/containers-before.jsonl" | ForEach-Object { $_ | ConvertFrom-Json })
        $current=@($after | ForEach-Object { $_ | ConvertFrom-Json })
        foreach ($old in $prior) { $found=@($current | Where-Object ID -eq $old.ID); if ($found.Count -ne 1 -or $found[0].State -ne $old.State) { throw 'Existing container state changed.' } }
        if (@($current | Where-Object Names -eq $name).Count) { throw 'Test container remains.' }
        $owned=Read-Json "$run/console.process.json"
        if ($owned.productPid -and (Get-Process -Id $owned.productPid -ErrorAction SilentlyContinue)) { throw 'Owned console remains.' }
        [ordered]@{testContainerRemoved=$true;ownedProcessesExited=$true;existingContainerStatesPreserved=$true;existingContainers=$prior.Count;completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath "$run/cleanup.json" -Encoding utf8
    }
    catch { $cleanupErrors.Add($_); if (!$failure) { $failure=$_ } }
    if ($cleanupErrors.Count) {
        try { $cleanupErrors | Out-String | Set-Content -LiteralPath "$run/cleanup-failure.log" -Encoding utf8 }
        catch { Write-Warning "Cleanup error report could not be written: $_" }
    }
}
if ($failure) { throw $failure }
$observation | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath "$run/verification.json" -Encoding utf8
Write-Output "PASS: $stage; actual proxy/Broker conditions, expected console exit, timing criteria where enabled, isolated resource cleanup. $run"
