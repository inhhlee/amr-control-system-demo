param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunId,
    [switch]$Controller
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repository
$run = Join-Path $repository "evidence/COM-03/$RunId"

if (!$Controller) {
    if (Test-Path -LiteralPath $run) { throw 'Execution ID already exists.' }
    New-Item -ItemType Directory -Path $run | Out-Null
    $started = [DateTimeOffset]::UtcNow
    $child = $null
    try {
        $container = docker compose -f docker/compose.yaml ps -q mosquitto
        if ($LASTEXITCODE -ne 0 -or !$container) { throw 'Project Broker must already be running at 127.0.0.1:1884.' }
        docker inspect $container --format '{{json .Mounts}}' > "$run/broker-mounts.json"
        if ($LASTEXITCODE -ne 0) { throw 'Broker inspect failed.' }
        $dockerVersion = docker version --format '{{.Client.Version}} / {{.Server.Version}}'
        $brokerVersion = docker compose -f docker/compose.yaml exec -T mosquitto mosquitto -h
        if ($LASTEXITCODE -ne 0) { throw 'Broker version check failed.' }
        [ordered]@{ startedAtUtc=$started.ToString('O'); sdk=(dotnet --version);
            os=[Runtime.InteropServices.RuntimeInformation]::OSDescription; mqttnet='5.2.0.1603';
            dockerVersion=$dockerVersion; brokerVersion=$brokerVersion[0]; baseCommit=(git rev-parse HEAD);
            branch=(git branch --show-current); sourceState='uncommitted Issue #5'; brokerManagedByThisScript=$false;
            timeoutMeasurement='Connecting timestamp to ConnectFailed timestamp, configured 1s/2s, allowed -50ms/+2000ms';
            processExitUpperBoundSeconds=8; cancellationExitUpperBoundSeconds=3;
            scenarios=@('refused','timeout-1','timeout-2','recovery-1','cancel','recovery-2') } |
            ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 "$run/environment.json"
        $files = @(rg --files src tests -g '*.cs' -g '*.csproj' -g '*.ps1' -g '*.json') + @('global.json','AmrControlSystem.slnx')
        @($files | ForEach-Object { @{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash} }) |
            ConvertTo-Json | Set-Content -Encoding utf8 "$run/source-version.json"
        $child = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList @('-NoProfile','-File',$PSCommandPath,'-RunId',$RunId,'-Controller') `
            -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput "$run/controller.stdout.log" `
            -RedirectStandardError "$run/controller.stderr.log" -PassThru
        $null = $child.Handle
        if (!$child.WaitForExit(60000)) { throw 'Controller exceeded 60s.' }
        $child.WaitForExit()
        if ($child.ExitCode -ne 0) { throw 'Scenario failed. See controller stderr/failure log.' }
        docker compose -f docker/compose.yaml logs --no-color --timestamps --since $started.ToString('O') mosquitto *> "$run/broker.log"
        if ($LASTEXITCODE -ne 0) { throw 'Broker log collection failed.' }
        $result = Get-Content "$run/scenarios.json" -Raw | ConvertFrom-Json
        $brokerLog = Get-Content "$run/broker.log" -Raw
        foreach ($session in @($result | Where-Object name -like 'recovery-*')) {
            if ($brokerLog -notmatch ([regex]::Escape($session.clientId) + '(?: \[[^\]]+\])? disconnected\.')) { throw 'Broker disconnect evidence missing.' }
        }
        if (@($result.clientId | Select-Object -Unique).Count -ne 6) { throw 'Client ID reused.' }
        @{result='passed';completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');brokerDisconnectConfirmed=$true;scenarios=$result} |
            ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 "$run/verification.json"
        Write-Output "PASS: TCP refusal, MQTT handshake timeout 1s/2s, Ctrl+C during connect, two fresh MQTT sessions. $run"
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

# Windows CTRL_C_EVENT는 이 도구가 시작한 격리 콘솔에만 전달한다.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Com03BControl
{
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GenerateConsoleCtrlEvent(uint signal, uint groupId);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern uint GetConsoleProcessList([Out] uint[] processList, uint count);
}
'@
$results = [Collections.Generic.List[object]]::new()
$process = $null; $listener = $null; $peer = $null; $reserved = $null; $attached = $false
function Wait-Output([string]$marker) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(8)
    do {
        $text = [string](Get-Content -LiteralPath $stdout -Raw)
        if ($null -ne $text -and $text.Contains($marker)) { return }
        if ($process.HasExited) { throw "Console exited before $marker" }
        Start-Sleep -Milliseconds 20
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Missing output: $marker"
}
function Send-CtrlC {
    $null = [Com03BControl]::FreeConsole()
    if (![Com03BControl]::AttachConsole([uint32]$process.Id)) { throw 'AttachConsole failed.' }
    $script:attached = $true
    $pids = [uint32[]]::new(16)
    $count = [Com03BControl]::GetConsoleProcessList($pids,16)
    if ($count -eq 0 -or $count -gt 16) { throw 'Cannot verify isolated console.' }
    $members = @($pids[0..($count-1)])
    if (@($members | Where-Object { $_ -ne $PID -and $_ -ne $process.Id }).Count) { throw 'Unexpected console member.' }
    if (![Com03BControl]::SetConsoleCtrlHandler([IntPtr]::Zero,$true)) { throw 'Controller protection failed.' }
    if (![Com03BControl]::GenerateConsoleCtrlEvent(0,0)) { throw 'CTRL_C_EVENT failed.' }
    if (!$process.WaitForExit(3000)) { throw 'Ctrl+C shutdown exceeded 3s.' }
    $null = [Com03BControl]::SetConsoleCtrlHandler([IntPtr]::Zero,$false)
    $null = [Com03BControl]::FreeConsole()
    $script:attached = $false
    return $members
}
try {
    foreach ($name in @('refused','timeout-1','timeout-2','recovery-1','cancel','recovery-2')) {
        $port = 1884; $seconds = 3; $bytesReceived = 0; $members = @(); $closed = $null
        if ($name -eq 'refused') {
            # 포트만 독점 예약하고 Listen하지 않는다. 기존 서비스와 충돌하지 않는 실제 TCP 거절 조건.
            $reserved = [Net.Sockets.Socket]::new([Net.Sockets.AddressFamily]::InterNetwork,[Net.Sockets.SocketType]::Stream,[Net.Sockets.ProtocolType]::Tcp)
            $reserved.ExclusiveAddressUse = $true
            $reserved.Bind([Net.IPEndPoint]::new([Net.IPAddress]::Loopback,0))
            $port = $reserved.LocalEndPoint.Port; $seconds = 10
        } elseif ($name -like 'timeout-*' -or $name -eq 'cancel') {
            $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
            $listener.Start()
            $port = $listener.LocalEndpoint.Port
            $seconds = if ($name -eq 'cancel') {30} else {[int]($name.Split('-')[1])}
            $accept = $listener.AcceptTcpClientAsync()
        }
        $stdout = "$run/$name.stdout.log"; $stderr = "$run/$name.stderr.log"
        $process = Start-Process -FilePath (Join-Path $repository 'src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.exe') `
            -ArgumentList @('--host','127.0.0.1','--port',"$port",'--connect-timeout-seconds',"$seconds") `
            -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        $null = $process.Handle
        if ($null -ne $listener) {
            if (!$accept.Wait(5000)) { throw 'TCP accept timed out.' }
            $peer = $accept.GetAwaiter().GetResult()
            $buffer = [byte[]]::new(4096)
            $read = $peer.GetStream().ReadAsync($buffer,0,$buffer.Length)
            if (!$read.Wait(3000)) { throw 'CONNECT bytes missing.' }
            $bytesReceived = $read.GetAwaiter().GetResult()
            if ($bytesReceived -le 0 -or $buffer[0] -ne 16) { throw 'Expected MQTT CONNECT packet.' }
            # CONNACK를 보내지 않아 TCP 연결 후 MQTT 승인을 기다리게 한다.
        }
        if ($name -like 'recovery-*') { Wait-Output 'WaitingForShutdown'; $members = @(Send-CtrlC) }
        elseif ($name -eq 'cancel') { $members = @(Send-CtrlC) }
        if (!$process.WaitForExit(8000)) { throw 'Process did not terminate within 8s.' }
        $process.WaitForExit()
        $text = [string](Get-Content $stdout -Raw); $errors = [string](Get-Content $stderr -Raw)
        $expectedExit = if ($name -like 'recovery-*' -or $name -eq 'cancel') {0} else {1}
        if ($process.ExitCode -ne $expectedExit -or !$text.Contains("ExitCode=$expectedExit")) { throw 'Unexpected exit code.' }
        if (!$text.Contains('Disposed') -or $text.Contains('ConnectionLost')) { throw 'Cleanup or initial failure classification mismatch.' }
        if ($name -like 'recovery-*') {
            if ($errors -or $text -notmatch '(?s) Connected.*Subscribed.*ShutdownRequested.*SubscriptionStopped.*Disconnected.*Disposed') { throw 'Normal session failed.' }
        } else {
            if ($text -match ' Connected') { throw 'Failure/cancel reported Connected.' }
            if ($name -eq 'refused' -and !$errors.Contains('Reason=TransportFailure')) { throw 'Refusal misclassified.' }
            if ($name -like 'timeout-*' -and !$errors.Contains('Reason=Timeout')) { throw 'Timeout misclassified.' }
            if ($name -eq 'cancel' -and ($errors -or !$text.Contains('ConnectCanceled Reason=UserCanceled'))) { throw 'Cancellation misclassified.' }
        }
        $elapsedMs = $null
        if ($name -like 'timeout-*') {
            $startTime = [DateTimeOffset]([regex]::Match($text,'(?m)^(\S+) Connecting\r?$').Groups[1].Value)
            $endTime = [DateTimeOffset]([regex]::Match($errors,'(?m)^(\S+) ConnectFailed').Groups[1].Value)
            $elapsedMs = ($endTime - $startTime).TotalMilliseconds
            if ($elapsedMs -lt ($seconds*1000-50) -or $elapsedMs -gt ($seconds*1000+2000)) { throw 'Timeout outside declared tolerance.' }
        }
        if ($null -ne $peer) {
            # 이미 받은 CONNECT 외에 남은 바이트를 소비하고 EOF로 실제 소켓 정리를 확인한다.
            do {
                $read = $peer.GetStream().ReadAsync($buffer,0,$buffer.Length)
                if (!$read.Wait(2000)) { throw 'Client transport not closed.' }
                $remaining = $read.GetAwaiter().GetResult()
            } while ($remaining -gt 0)
            $closed = $true
            $peer.Dispose(); $peer = $null
            $listener.Stop(); $listener = $null
        }
        if ($null -ne $reserved) { $reserved.Dispose(); $reserved = $null }
        $results.Add([ordered]@{name=$name;port=$port;connectTimeoutSeconds=$seconds;elapsedMs=$elapsedMs;
            exitCode=$process.ExitCode;clientId=[regex]::Match($text,'ClientId=(\S+)').Groups[1].Value;
            connectBytesReceived=$bytesReceived;transportClosed=$closed;consoleMembers=$members;result='passed'})
        $results | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 "$run/scenarios.json"
        $process.Dispose(); $process = $null
    }
}
catch { $_ | Out-String | Set-Content -Encoding utf8 "$run/failure.log"; throw }
finally {
    if ($attached) {
        $null = [Com03BControl]::SetConsoleCtrlHandler([IntPtr]::Zero,$false)
        $null = [Com03BControl]::FreeConsole()
    }
    if ($null -ne $process) {
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
    if ($null -ne $peer) { $peer.Dispose() }
    if ($null -ne $listener) { $listener.Stop() }
    if ($null -ne $reserved) { $reserved.Dispose() }
}
