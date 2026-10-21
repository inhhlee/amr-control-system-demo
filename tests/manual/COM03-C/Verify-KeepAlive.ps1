param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunId,
    [ValidateRange(10,30)][int]$ObserveSeconds = 12,
    [switch]$Controller,
    [int]$BrokerPort
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$run = Join-Path $repository "evidence/COM-03/$RunId"
Set-Location -LiteralPath $repository

if ($Controller) {
    # These changes affect only this controller and its new product process.
    $env:AMR_SEQ_URL = $null
    $env:AMR_SEQ_API_KEY = $null
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Com03C1Control {
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern uint GetConsoleProcessList([Out] uint[] list, uint count);
}
'@
    $process = $null
    $attached = $false
    try {
        $exe = Join-Path $repository 'src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.exe'
        $stdout = Join-Path $run 'console.stdout.log'
        $stderr = Join-Path $run 'console.stderr.log'
        $process = Start-Process -FilePath $exe -ArgumentList @('--host','127.0.0.1','--port',"$BrokerPort") -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        $null = $process.Handle
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        do {
            $text = [string](Get-Content -LiteralPath $stdout -Raw)
            if ($text -and $text.Contains('WaitingForShutdown')) { break }
            if ($process.HasExited) { throw 'Product exited before subscription completed.' }
            Start-Sleep -Milliseconds 25
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if (!$text -or !$text.Contains('WaitingForShutdown')) { throw 'Product did not reach receive wait.' }
        if ($text -notmatch 'ClientId=(\S+).*KeepAliveSeconds=([0-9.]+)') { throw 'Settings evidence missing.' }
        $clientId = $Matches[1]
        $keepAlive = [double]::Parse($Matches[2], [Globalization.CultureInfo]::InvariantCulture)
        if ($keepAlive -le 0 -or $ObserveSeconds -lt 2 * $keepAlive) { throw 'Observation must cover twice the applied KeepAlive.' }
        $observedFrom = [DateTimeOffset]::UtcNow
        $watch = [Diagnostics.Stopwatch]::StartNew()
        while ($watch.Elapsed.TotalSeconds -lt $ObserveSeconds) {
            if ($process.HasExited) { throw 'Connection process exited during idle observation.' }
            Start-Sleep -Milliseconds 100
        }
        $watch.Stop()
        $observedUntil = [DateTimeOffset]::UtcNow
        $null = [Com03C1Control]::FreeConsole()
        if (![Com03C1Control]::AttachConsole([uint32]$process.Id)) { throw 'AttachConsole failed.' }
        $attached = $true
        $list = [uint32[]]::new(16)
        $count = [Com03C1Control]::GetConsoleProcessList($list,16)
        if ($count -eq 0 -or $count -gt 16) { throw 'Cannot verify isolated console.' }
        $members = @($list[0..($count-1)])
        if (@($members | Where-Object { $_ -ne $PID -and $_ -ne $process.Id }).Count) { throw 'Unexpected console member; Ctrl+C was not sent.' }
        if (![Com03C1Control]::SetConsoleCtrlHandler([IntPtr]::Zero,$true)) { throw 'Controller protection failed.' }
        $cancelAt = [DateTimeOffset]::UtcNow
        if (![Com03C1Control]::GenerateConsoleCtrlEvent(0,0)) { throw 'CTRL_C_EVENT failed.' }
        if (!$process.WaitForExit(5000)) { throw 'Normal shutdown exceeded 5s.' }
        $process.WaitForExit()
        $text = [string](Get-Content -LiteralPath $stdout -Raw)
        $errors = [string](Get-Content -LiteralPath $stderr -Raw)
        if ($process.ExitCode -ne 0 -or $errors -or $text -notmatch '(?s) Connected.*Subscribed.*ShutdownRequested.*SubscriptionStopped.*Disconnected.*Disposed.*ExitCode=0') { throw 'Normal lifecycle did not meet C1 expectations.' }
        if ($text -match 'ConnectionLost|ProcessingStarted|QueueAdmission|MessageDiagnostic|\sReceived\s|Rejected Code=') { throw 'Unexpected disconnect or business-message activity.' }
        [ordered]@{clientId=$clientId;keepAliveSeconds=$keepAlive;port=$BrokerPort;observedFromUtc=$observedFrom.ToString('O');observedUntilUtc=$observedUntil.ToString('O');observedSeconds=$watch.Elapsed.TotalSeconds;ctrlCAtUtc=$cancelAt.ToString('O');exitedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');exitCode=$process.ExitCode;consolePids=$members;productPid=$process.Id;businessMessagesInConsole=0;seqEnabled=$false} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$run/session.json" -Encoding utf8
    }
    catch { $_ | Out-String | Set-Content -LiteralPath "$run/controller-failure.log" -Encoding utf8; throw }
    finally {
        if ($attached) {
            $null = [Com03C1Control]::SetConsoleCtrlHandler([IntPtr]::Zero,$false)
            $null = [Com03C1Control]::FreeConsole()
        }
        if ($null -ne $process) {
            if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
            $process.Dispose()
        }
    }
    exit 0
}

if (Test-Path -LiteralPath $run) { throw 'Execution ID already exists; use a new RunId.' }
New-Item -ItemType Directory -Path $run | Out-Null
$docker = @(Get-Command docker -CommandType Application -ErrorAction Stop)[0].Source
$container = $null
$child = $null
$failure = $null
$cleanupPassed = $false
$started = [DateTimeOffset]::UtcNow
function Invoke-Docker([string[]]$Arguments) {
    $begin = [DateTimeOffset]::UtcNow
    $result = @(& $docker @Arguments 2>&1)
    $code = $LASTEXITCODE
    [ordered]@{program='docker';arguments=$Arguments;startedAtUtc=$begin.ToString('O');exitCode=$code} | ConvertTo-Json -Compress | Add-Content -LiteralPath "$run/commands.jsonl" -Encoding utf8
    if ($code -ne 0) { throw "Docker failed ($code): $($Arguments -join ' '): $($result -join ' ')" }
    return $result
}
try {
    $dockerVersion = Invoke-Docker -Arguments @('version','--format','{{.Client.Version}} / {{.Server.Version}}')
    Invoke-Docker -Arguments @('ps','-a','--format','{{json .}}') | Set-Content -LiteralPath "$run/containers-before.jsonl" -Encoding utf8
    $image = 'eclipse-mosquitto:2.1.2-alpine'
    $imageId = [string](Invoke-Docker -Arguments @('image','inspect',$image,'--format','{{.Id}}'))
    & dotnet build src/Amr.Communication.Console/Amr.Communication.Console.csproj --no-restore --verbosity minimal *> "$run/build.log"
    if ($LASTEXITCODE -ne 0) { throw 'Product build failed; see build.log.' }
    $files = @(git ls-files src global.json AmrControlSystem.slnx) + @('tests/manual/COM03-C/Verify-KeepAlive.ps1')
    @($files | ForEach-Object { [ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash} }) | ConvertTo-Json | Set-Content -LiteralPath "$run/source-version.json" -Encoding utf8
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
    $name = ('amr-c1-' + $RunId).ToLowerInvariant()
    $mount = "type=bind,source=$run/broker.conf,target=/mosquitto/config/mosquitto.conf,readonly"
    $container = [string](Invoke-Docker -Arguments @('run','--detach','--pull','never','--name',$name,'--label',"amr.c1.run=$RunId",'--publish','127.0.0.1::1883','--mount',$mount,$image))
    if ($container -notmatch '^[a-f0-9]{64}$') { throw 'Container ID not confirmed.' }
    $mapping = [string](Invoke-Docker -Arguments @('port',$container,'1883/tcp'))
    if ($mapping -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Loopback-only dynamic mapping not confirmed.' }
    $port = [int]$Matches[1]
    Invoke-Docker -Arguments @('inspect',$container,'--format','{{json .Mounts}}') | Set-Content -LiteralPath "$run/broker-mounts.json" -Encoding utf8
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    do {
        $brokerLog = (Invoke-Docker -Arguments @('logs','--timestamps',$container)) -join "`n"
        if ($brokerLog -match 'mosquitto version .* running') { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($brokerLog -notmatch 'mosquitto version .* running') { throw 'Broker did not become ready.' }
    [ordered]@{runId=$RunId;startedAtUtc=$started.ToString('O');os=[Runtime.InteropServices.RuntimeInformation]::OSDescription;sdk=(dotnet --version);mqttnet='5.2.0.1603';dockerVersion=$dockerVersion;image=$image;imageId=$imageId;containerId=$container;containerName=$name;endpoint="127.0.0.1:$port";branch=(git branch --show-current);baseCommit=(git rev-parse HEAD);sourceState='C1 script and execution evidence uncommitted; product source unchanged';businessPublisher='none';expectedKeepAliveSeconds=5;minimumObserveSeconds=10;requestedObserveSeconds=$ObserveSeconds;minimumPingPairs=2;normalExitCode=0;shutdownUpperBoundSeconds=5;seqEnabled=$false} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$run/environment.json" -Encoding utf8
    $child = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList @('-NoProfile','-File',"`"$PSCommandPath`"",'-RunId',$RunId,'-ObserveSeconds',"$ObserveSeconds",'-Controller','-BrokerPort',"$port") -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput "$run/controller.stdout.log" -RedirectStandardError "$run/controller.stderr.log" -PassThru
    $null = $child.Handle
    if (!$child.WaitForExit(45000)) { throw 'Controller exceeded 45s.' }
    $child.WaitForExit()
    if ($child.ExitCode -ne 0) { throw 'Controller failed; see controller logs.' }
    Invoke-Docker -Arguments @('logs','--timestamps',$container) | Set-Content -LiteralPath "$run/broker.log" -Encoding utf8
    $session = Get-Content -LiteralPath "$run/session.json" -Raw | ConvertFrom-Json
    $cid = [regex]::Escape($session.clientId)
    $lines = @(Get-Content -LiteralPath "$run/broker.log")
    $req = @($lines | Where-Object { $_ -match "Received PINGREQ from $cid(?:\s|$)" })
    $resp = @($lines | Where-Object { $_ -match "Sending PINGRESP to $cid(?:\s|$)" })
    $publish = @($lines | Where-Object { $_ -match 'Received PUBLISH|Sending PUBLISH' })
    $pings = @($lines | Where-Object { $_ -match "(Received PINGREQ from|Sending PINGRESP to) $cid(?:\s|$)" })
    $pings | Set-Content -LiteralPath "$run/ping.log" -Encoding utf8
    if ($req.Count -lt 2 -or $req.Count -ne $resp.Count) { throw 'Two matching PINGREQ/PINGRESP exchanges not confirmed.' }
    if ($publish.Count -ne 0) { throw 'Broker observed business PUBLISH traffic.' }
    if (($lines -join "`n") -notmatch "$cid(?: \[[^\]]+\])? disconnected\.") { throw 'Normal Broker disconnect evidence missing.' }
    [ordered]@{result='passed';session=$session;pingRequests=$req.Count;pingResponses=$resp.Count;businessPublishes=$publish.Count;brokerNormalDisconnect=$true;completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$run/verification.json" -Encoding utf8
}
catch { $failure = $_; $_ | Out-String | Set-Content -LiteralPath "$run/runner-failure.log" -Encoding utf8 }
finally {
    if ($null -ne $child) {
        if (!$child.HasExited) { $child.Kill($true); $child.WaitForExit() }
        $child.Dispose()
    }
    $cleanupErrors = [Collections.Generic.List[object]]::new()
    # Diagnostic failure must not prevent cleanup of the confirmed owned ID.
    if ($container -match '^[a-f0-9]{64}$') {
        try { Invoke-Docker -Arguments @('logs','--timestamps',$container) | Set-Content -LiteralPath "$run/broker.log" -Encoding utf8 }
        catch { $cleanupErrors.Add($_); if (!$failure) { $failure=$_ } }
        try { Invoke-Docker -Arguments @('stop','--time','5',$container) | Set-Content -LiteralPath "$run/broker-stop.log" -Encoding utf8 }
        catch { $cleanupErrors.Add($_); if (!$failure) { $failure=$_ } }
        try { Invoke-Docker -Arguments @('logs','--timestamps',$container) | Set-Content -LiteralPath "$run/broker-final.log" -Encoding utf8 }
        catch { $cleanupErrors.Add($_); if (!$failure) { $failure=$_ } }
        try { Invoke-Docker -Arguments @('rm',$container) | Set-Content -LiteralPath "$run/broker-remove.log" -Encoding utf8 }
        catch { $cleanupErrors.Add($_); if (!$failure) { $failure=$_ } }
    }
    try {
        $after = @(Invoke-Docker -Arguments @('ps','-a','--format','{{json .}}'))
        $after | Set-Content -LiteralPath "$run/containers-after.jsonl" -Encoding utf8
        $before = @(Get-Content -LiteralPath "$run/containers-before.jsonl" | ForEach-Object { $_ | ConvertFrom-Json })
        $afterObjects = @($after | ForEach-Object { $_ | ConvertFrom-Json })
        foreach ($prior in $before) {
            $current = @($afterObjects | Where-Object ID -eq $prior.ID)
            if ($current.Count -ne 1 -or $current[0].State -ne $prior.State) { throw 'Pre-existing container state changed.' }
        }
        if (@($afterObjects | Where-Object Names -eq $name).Count) { throw 'Test container remains.' }
        $cleanupPassed = $true
        [ordered]@{testContainerRemoved=$true;existingContainerStatesPreserved=$true;containersBefore=$before.Count;containersAfter=$afterObjects.Count;completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath "$run/cleanup.json" -Encoding utf8
    }
    catch { $cleanupErrors.Add($_); if (!$failure) { $failure=$_ } }
    if ($cleanupErrors.Count) {
        try { $cleanupErrors | Out-String | Set-Content -LiteralPath "$run/cleanup-failure.log" -Encoding utf8 }
        catch { Write-Warning "Cleanup error report could not be written: $_" }
    }
}
if ($failure) { throw $failure }
if (!$cleanupPassed) { throw 'Cleanup not verified.' }
Write-Output "PASS: C1 idle KeepAlive, PING pairs, normal Ctrl+C, isolated Broker cleanup. $run"

