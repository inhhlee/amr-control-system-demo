param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunId,
    [switch]$Controller,
    [int]$BrokerPort
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$run = Join-Path $repository "evidence/COM-03/$RunId"
Set-Location -LiteralPath $repository

if ($Controller) {
    $env:AMR_SEQ_URL = $null
    $env:AMR_SEQ_API_KEY = $null
    $process = $null
    try {
        $stdout = Join-Path $run 'lost.stdout.log'
        $stderr = Join-Path $run 'lost.stderr.log'
        $process = Start-Process -FilePath (Join-Path $repository 'src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.exe') -ArgumentList @('--host','127.0.0.1','--port',"$BrokerPort") -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        $null = $process.Handle
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        do {
            $text = [string](Get-Content -LiteralPath $stdout -Raw)
            if ($text -and $text.Contains('WaitingForShutdown')) { break }
            if ($process.HasExited) { throw 'Product exited before subscription.' }
            Start-Sleep -Milliseconds 25
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if (!$text -or !$text.Contains('WaitingForShutdown') -or $text -notmatch 'ClientId=(\S+)') { throw 'No approved/subscribed product session.' }
        $clientId = $Matches[1]
        [ordered]@{clientId=$clientId;productPid=$process.Id;port=$BrokerPort;readyAtUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath "$run/lost.ready.json" -Encoding utf8
        if (!$process.WaitForExit(25000)) { throw 'Product did not exit after Broker shutdown.' }
        $process.WaitForExit()
        $exitObserved = [DateTimeOffset]::UtcNow
        $text = [string](Get-Content -LiteralPath $stdout -Raw)
        $errors = [string](Get-Content -LiteralPath $stderr -Raw)
        $pattern = '(?m)^(?<at>\S+) ConnectionLost Reason=(?<reason>\S+) ClientWasConnected=(?<was>\S+) ExceptionType=(?<exception>\S+) SocketError=(?<socket>\S+) ClientId=(?<id>\S+)\r?$'
        $lost = [regex]::Matches($text,$pattern)
        if ($lost.Count -ne 1 -or $lost[0].Groups['id'].Value -ne $clientId -or $lost[0].Groups['was'].Value -ne 'True') { throw 'One correctly identified disconnect with diagnostic fields is required.' }
        if ($process.ExitCode -ne 1 -or $errors -or $text -notmatch '(?s) Connected.*Subscribed.*ConnectionLost.*ShutdownRequested Reason=ConnectionLost.*QueueStopped.*SubscriptionStopped.*Disposed.*ExitCode=1') { throw 'Unexpected shutdown cause, cleanup or exit code.' }
        [ordered]@{clientId=$clientId;productPid=$process.Id;port=$BrokerPort;detectedAtUtc=$lost[0].Groups['at'].Value;reason=$lost[0].Groups['reason'].Value;exceptionType=$lost[0].Groups['exception'].Value;socketError=$lost[0].Groups['socket'].Value;clientWasConnected=$true;connectionLostEvents=$lost.Count;exitCode=$process.ExitCode;exitedAtUtc=$exitObserved.ToString('O');queueStopped=$true;subscriptionStopped=$true;disposed=$true;seqEnabled=$false} | ConvertTo-Json | Set-Content -LiteralPath "$run/lost.session.json" -Encoding utf8
    }
    catch { $_ | Out-String | Set-Content -LiteralPath "$run/lost-controller-failure.log" -Encoding utf8; throw }
    finally {
        if ($null -ne $process) {
            if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
            $process.Dispose()
        }
    }
    exit 0
}

if (Test-Path -LiteralPath $run) { throw 'Execution ID exists; use a new RunId.' }
# Reusable after merge: the actual branch, source hashes and binaries are recorded below.
New-Item -ItemType Directory -Path $run | Out-Null
$docker = (Get-Command docker -ErrorAction SilentlyContinue).Source
if (!$docker) { $docker = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe' }
$container = $null
$child = $null
$failure = $null
$started = [DateTimeOffset]::UtcNow
$name = ('amr-c2-' + $RunId).ToLowerInvariant()
function Invoke-Docker([string[]]$Arguments) {
    $begin = [DateTimeOffset]::UtcNow
    $result = @(& $docker @Arguments 2>&1)
    $code = $LASTEXITCODE
    [ordered]@{program='docker';arguments=$Arguments;startedAtUtc=$begin.ToString('O');endedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');exitCode=$code} | ConvertTo-Json -Compress | Add-Content -LiteralPath "$run/commands.jsonl" -Encoding utf8
    if ($code -ne 0) { throw "Docker failed ($code): $($Arguments -join ' '): $($result -join ' ')" }
    return $result
}
function Get-BrokerPort {
    $mapping = [string](Invoke-Docker -Arguments @('port',$container,'1883/tcp'))
    if ($mapping -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Loopback-only port mapping is required.' }
    return [int]$Matches[1]
}
function Wait-Broker([int]$ExpectedStarts) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    do {
        $logText = (Invoke-Docker -Arguments @('logs','--timestamps',$container)) -join "`n"
        if ([regex]::Matches($logText,'mosquitto version [^\r\n]+ running').Count -ge $ExpectedStarts) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'Broker ready log missing.'
}
try {
    $version = Invoke-Docker -Arguments @('version','--format','{{.Client.Version}} / {{.Server.Version}}')
    Invoke-Docker -Arguments @('ps','-a','--no-trunc','--format','{{json .}}') | Set-Content -LiteralPath "$run/containers-before.jsonl" -Encoding utf8
    $image = 'eclipse-mosquitto:2.1.2-alpine'
    $imageId = [string](Invoke-Docker -Arguments @('image','inspect',$image,'--format','{{.Id}}'))
    $files = @(git ls-files src tests global.json AmrControlSystem.slnx) + @('tests/manual/COM03-C/Verify-KeepAlive.ps1','tests/manual/COM03-C/Verify-BrokerShutdown.ps1')
    @($files | Select-Object -Unique | ForEach-Object { [ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash} }) | ConvertTo-Json | Set-Content -LiteralPath "$run/source-version.json" -Encoding utf8
    $binaryFiles = @('Amr.Communication.Console.exe','Amr.Communication.Console.dll','Amr.Communication.dll','MQTTnet.dll')
    @($binaryFiles | ForEach-Object { [ordered]@{file=$_;sha256=(Get-FileHash -LiteralPath (Join-Path 'src/Amr.Communication.Console/bin/Debug/net10.0' $_)).Hash} }) | ConvertTo-Json | Set-Content -LiteralPath "$run/binary-version.json" -Encoding utf8
    @'
listener 1883 0.0.0.0
allow_anonymous true
persistence false
log_dest stdout
log_type error
log_type warning
log_type notice
log_type information
log_type debug
connection_messages true
log_timestamp true
'@ | Set-Content -LiteralPath "$run/broker.conf" -Encoding ascii
    $container = [string](Invoke-Docker -Arguments @('run','--detach','--pull','never','--name',$name,'--label',"amr.c2.run=$RunId",'--publish','127.0.0.1::1883','--mount',"type=bind,source=$run/broker.conf,target=/mosquitto/config/mosquitto.conf,readonly",$image))
    if ($container -notmatch '^[a-f0-9]{64}$') { throw 'Container ID not confirmed.' }
    Wait-Broker 1
    $port = Get-BrokerPort
    Invoke-Docker -Arguments @('inspect',$container,'--format','{{json .Mounts}}') | Set-Content -LiteralPath "$run/broker-mounts.json" -Encoding utf8
    # C2 is an orderly Broker stop: TCP closes / MQTT disconnect may be delivered.
    # Its bounds include Docker stop-command overhead; they do not define a KeepAlive/SLA timeout.
    [ordered]@{runId=$RunId;startedAtUtc=$started.ToString('O');branch=(git branch --show-current);baseCommit=(git rev-parse HEAD);sdk=(dotnet --version);os=[Runtime.InteropServices.RuntimeInformation]::OSDescription;mqttnet='5.2.0.1603';dockerVersion=$version;image=$image;imageId=$imageId;containerId=$container;containerName=$name;initialEndpoint="127.0.0.1:$port";keepAliveSeconds=5;detectUpperBoundSeconds=10;exitUpperBoundSeconds=15;clockOrigin='UTC timestamp immediately before docker stop request';brokerStopGraceSeconds=5;unitRun='20261020-c2-unit-01';recoveryObserveSeconds=12;businessPublisher='none';seqEnabled=$false;sourceState='C1 preserved; C2 product/test/manual changes uncommitted'} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$run/environment.json" -Encoding utf8
    $child = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList @('-NoProfile','-File',$PSCommandPath,'-RunId',$RunId,'-Controller','-BrokerPort',"$port") -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput "$run/lost-controller.stdout.log" -RedirectStandardError "$run/lost-controller.stderr.log" -PassThru
    $null = $child.Handle
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(12)
    $ready = $null
    do {
        if ($child.HasExited) { throw 'Lost controller exited before stop request.' }
        if (Test-Path -LiteralPath "$run/lost.ready.json") {
            try { $ready = Get-Content -LiteralPath "$run/lost.ready.json" -Raw | ConvertFrom-Json -DateKind String } catch { $ready=$null }
            if ($ready.clientId) { break }
        }
        Start-Sleep -Milliseconds 25
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if (!$ready.clientId) { throw 'Ready session not confirmed.' }
    $stopRequested = [DateTimeOffset]::UtcNow
    $stopWatch = [Diagnostics.Stopwatch]::StartNew()
    Invoke-Docker -Arguments @('stop','--time','5',$container) | Set-Content -LiteralPath "$run/broker-stop.log" -Encoding utf8
    $stopReturned = [DateTimeOffset]::UtcNow
    if (!$child.WaitForExit(15000)) { throw 'Lost controller did not complete.' }
    $child.WaitForExit()
    $stopWatch.Stop()
    if ($child.ExitCode -ne 0) { throw 'Disconnect scenario failed; see lost controller logs.' }
    $child.Dispose(); $child=$null
    $lost = Get-Content -LiteralPath "$run/lost.session.json" -Raw | ConvertFrom-Json -DateKind String
    $detectMs = ([DateTimeOffset]::Parse($lost.detectedAtUtc) - $stopRequested).TotalMilliseconds
    $exitMs = ([DateTimeOffset]::Parse($lost.exitedAtUtc) - $stopRequested).TotalMilliseconds
    if ($detectMs -lt 0 -or $detectMs -gt 10000 -or $exitMs -gt 15000) { throw 'Predeclared detection or exit bound exceeded.' }
    $brokerState = (Invoke-Docker -Arguments @('inspect',$container,'--format','{{json .State}}')) -join "`n" | ConvertFrom-Json -DateKind String
    if ($brokerState.Running -or $brokerState.ExitCode -ne 0) { throw 'Broker did not stop normally.' }
    if (Get-Process -Id $lost.productPid -ErrorAction SilentlyContinue) { throw 'Lost product process remains.' }
    $connections = @(Get-NetTCPConnection -RemoteAddress 127.0.0.1 -RemotePort $port -State Established -ErrorAction SilentlyContinue | Select-Object LocalAddress,LocalPort,RemoteAddress,RemotePort,State,OwningProcess)
    if ($connections.Count) { throw 'Established socket remains after product shutdown.' }
    [ordered]@{stopRequestedAtUtc=$stopRequested.ToString('O');stopReturnedAtUtc=$stopReturned.ToString('O');brokerFinishedAtUtc=$brokerState.FinishedAt;brokerExitCode=$brokerState.ExitCode;detectedAtUtc=$lost.detectedAtUtc;detectionMilliseconds=$detectMs;productExitMilliseconds=$exitMs;controllerCompletionMilliseconds=$stopWatch.Elapsed.TotalMilliseconds;productProcessGone=$true;establishedSocketsAfterExit=$connections.Count;session=$lost} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$run/shutdown.json" -Encoding utf8
    Invoke-Docker -Arguments @('logs','--timestamps',$container) | Set-Content -LiteralPath "$run/broker-after-stop.log" -Encoding utf8
    $restartRequested = [DateTimeOffset]::UtcNow
    Invoke-Docker -Arguments @('start',$container) | Set-Content -LiteralPath "$run/broker-start.log" -Encoding utf8
    Wait-Broker 2
    $recoveryPort = Get-BrokerPort
    [ordered]@{restartRequestedAtUtc=$restartRequested.ToString('O');sameContainerId=$container;recoveryEndpoint="127.0.0.1:$recoveryPort";method='Explicit new process via the existing C1 controller, not automatic reconnect'} | ConvertTo-Json | Set-Content -LiteralPath "$run/restart.json" -Encoding utf8
    # Reuse C1's already tested isolated CTRL_C_EVENT controller for the new normal session.
    $c1Script = Join-Path $PSScriptRoot 'Verify-KeepAlive.ps1'
    $child = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList @('-NoProfile','-File',$c1Script,'-RunId',$RunId,'-ObserveSeconds','12','-Controller','-BrokerPort',"$recoveryPort") -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput "$run/recovery-controller.stdout.log" -RedirectStandardError "$run/recovery-controller.stderr.log" -PassThru
    $null = $child.Handle
    if (!$child.WaitForExit(35000)) { throw 'Recovery controller exceeded 35s.' }
    $child.WaitForExit()
    if ($child.ExitCode -ne 0) { throw 'New normal session failed; see recovery controller logs.' }
    $child.Dispose(); $child=$null
    $recovery = Get-Content -LiteralPath "$run/session.json" -Raw | ConvertFrom-Json -DateKind String
    if ($recovery.clientId -eq $lost.clientId -or $recovery.productPid -eq $lost.productPid -or $recovery.exitCode -ne 0) { throw 'Recovery must use a new Client ID and process with exit 0.' }
    if (Get-Process -Id $recovery.productPid -ErrorAction SilentlyContinue) { throw 'Recovery process remains.' }
    $lines = @(Invoke-Docker -Arguments @('logs','--timestamps',$container))
    $lines | Set-Content -LiteralPath "$run/broker.log" -Encoding utf8
    $cid = [regex]::Escape($recovery.clientId)
    $req = @($lines | Where-Object { $_ -match "Received PINGREQ from $cid(?:\s|$)" })
    $resp = @($lines | Where-Object { $_ -match "Sending PINGRESP to $cid(?:\s|$)" })
    if ($req.Count -lt 2 -or $req.Count -ne $resp.Count -or $recovery.observedSeconds -lt 10) { throw 'C1 regression evidence missing in new session.' }
    $lines | Where-Object { $_ -match "(Received PINGREQ from|Sending PINGRESP to) $cid(?:\s|$)" } | Set-Content -LiteralPath "$run/recovery-ping.log" -Encoding utf8
    if (($lines -join "`n") -notmatch "$cid(?: \[[^\]]+\])? disconnected\.") { throw 'Normal recovery disconnect missing.' }
    if (@($lines | Where-Object { $_ -match 'Received PUBLISH|Sending PUBLISH' }).Count) { throw 'Unexpected business messages.' }
    [ordered]@{result='passed';completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');shutdown=(Get-Content -LiteralPath "$run/shutdown.json" -Raw | ConvertFrom-Json -DateKind String);restart=(Get-Content -LiteralPath "$run/restart.json" -Raw | ConvertFrom-Json -DateKind String);recovery=$recovery;recoveryPingRequests=$req.Count;recoveryPingResponses=$resp.Count;businessPublishes=0;automaticReconnectTested=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$run/verification.json" -Encoding utf8
}
catch { $failure=$_; $_ | Out-String | Set-Content -LiteralPath "$run/runner-failure.log" -Encoding utf8 }
finally {
    if ($null -ne $child) {
        if (!$child.HasExited) { $child.Kill($true); $child.WaitForExit() }
        $child.Dispose()
    }
    $cleanupErrors = [Collections.Generic.List[object]]::new()
    # Diagnostic failure must not prevent cleanup of the confirmed owned ID.
    if ($container -match '^[a-f0-9]{64}$') {
        try { Invoke-Docker -Arguments @('logs','--timestamps',$container) | Set-Content -LiteralPath "$run/broker-final.log" -Encoding utf8 }
        catch { $cleanupErrors.Add($_); if (!$failure) { $failure=$_ } }
        try { Invoke-Docker -Arguments @('stop','--time','5',$container) | Set-Content -LiteralPath "$run/broker-cleanup-stop.log" -Encoding utf8 }
        catch { $cleanupErrors.Add($_); if (!$failure) { $failure=$_ } }
        try { Invoke-Docker -Arguments @('rm','--volumes',$container) | Set-Content -LiteralPath "$run/broker-remove.log" -Encoding utf8 }
        catch { $cleanupErrors.Add($_); if (!$failure) { $failure=$_ } }
    }
    try {
        $after = @(Invoke-Docker -Arguments @('ps','-a','--no-trunc','--format','{{json .}}'))
        $after | Set-Content -LiteralPath "$run/containers-after.jsonl" -Encoding utf8
        $before = @(Get-Content -LiteralPath "$run/containers-before.jsonl" | ForEach-Object { $_ | ConvertFrom-Json })
        $afterObjects = @($after | ForEach-Object { $_ | ConvertFrom-Json })
        foreach ($prior in $before) {
            $current = @($afterObjects | Where-Object ID -eq $prior.ID)
            if ($current.Count -ne 1 -or $current[0].State -ne $prior.State) { throw 'Pre-existing container state changed.' }
        }
        if (@($afterObjects | Where-Object Names -eq $name).Count) { throw 'Test container remains.' }
        [ordered]@{testContainerRemoved=$true;existingContainerStatesPreserved=$true;containersBefore=$before.Count;containersAfter=$afterObjects.Count;completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath "$run/cleanup.json" -Encoding utf8
    }
    catch { $cleanupErrors.Add($_); if (!$failure) { $failure=$_ } }
    if ($cleanupErrors.Count) {
        try { $cleanupErrors | Out-String | Set-Content -LiteralPath "$run/cleanup-failure.log" -Encoding utf8 }
        catch { Write-Warning "Cleanup error report could not be written: $_" }
    }
}
if ($failure) { throw $failure }
Write-Output "PASS: C2 Broker stop detection, exit 1 and cleanup, same Broker restart, new normal console + C1 regression, isolated resource cleanup. $run"
