param([Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunId)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$root=Join-Path $repo "evidence/AMR/$RunId"
if (Test-Path $root) { throw 'Use a new RunId.' }
$null=New-Item -ItemType Directory $root
$exe=Join-Path $repo 'tools/Amr.Simulator.Map/bin/Debug/net10.0/Amr.Simulator.Map.exe'
$config=Get-Content (Join-Path $repo 'config/simulator.single.json') -Raw | ConvertFrom-Json
$config.initialPosition.x=5; $config.initialPosition.y=3; $config.speedMps=2
$config.waypoints=@(@{x=5;y=4},@{x=7;y=4})
$config | ConvertTo-Json -Depth 6 | Set-Content "$root/changed.json"
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MapHostControl {
    [DllImport("kernel32.dll")] public static extern bool FreeConsole();
    [DllImport("kernel32.dll")] public static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll")] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll")] public static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
    [DllImport("kernel32.dll")] public static extern uint GetConsoleProcessList([Out] uint[] list, uint count);
}
'@
$hostProcess=$null; $attached=$false
try {
    $null=[MapHostControl]::SetConsoleCtrlHandler([IntPtr]::Zero,$false)
    $hostProcess=Start-Process $exe -ArgumentList @('--port','0','--config',"`"$root/changed.json`"") -WorkingDirectory $repo -WindowStyle Hidden -RedirectStandardOutput "$root/server.stdout.log" -RedirectStandardError "$root/server.stderr.log" -PassThru
    $null=$hostProcess.Handle
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(10)
    do {
        $log=[string](Get-Content "$root/server.stdout.log" -Raw)
        if ($log -and $log -match 'MapReady Url=(http://127\.0\.0\.1:[0-9]+)') { $url=$Matches[1]; break }
        if ($hostProcess.HasExited) { throw 'Map host exited before ready.' }
        Start-Sleep -Milliseconds 30
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if (!$url) { throw 'Map host startup timeout.' }
    $state=Invoke-RestMethod "$url/api/state"
    if ($state.position.x -ne 5 -or $state.position.y -ne 3 -or $state.settings.speedMps -ne 2) { throw 'Changed settings not applied.' }
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Verify-Map.ps1') -BaseUrl $url -RunId "$RunId-api"
    if ($LASTEXITCODE -ne 0) { throw 'Changed-settings integration verification failed.' }
    $headers=@{'X-Simulator-Map'='1'}
    $null=Invoke-WebRequest "$url/api/start" -Method Post -Headers $headers
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(3)
    do { $state=Invoke-RestMethod "$url/api/state"; if ($state.amrState -eq 'BUSY') { break }; Start-Sleep -Milliseconds 20 } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($state.amrState -ne 'BUSY') { throw 'No movement before host shutdown.' }
    $null=[MapHostControl]::FreeConsole()
    if (![MapHostControl]::AttachConsole([uint32]$hostProcess.Id)) { throw 'AttachConsole failed.' }
    $attached=$true
    $members=[uint32[]]::new(16)
    $count=[MapHostControl]::GetConsoleProcessList($members,16)
    if ($count -eq 0 -or $count -gt 16) { throw 'Cannot verify owned console.' }
    $members=@($members[0..($count-1)])
    if (@($members | Where-Object { $_ -ne $PID -and $_ -ne $hostProcess.Id }).Count) { throw 'Other console process found; no signal sent.' }
    if (![MapHostControl]::SetConsoleCtrlHandler([IntPtr]::Zero,$true)) { throw 'Controller protection failed.' }
    $watch=[Diagnostics.Stopwatch]::StartNew()
    if (![MapHostControl]::GenerateConsoleCtrlEvent(0,0)) { throw 'Ctrl+C signal failed.' }
    if (!$hostProcess.WaitForExit(5000)) { throw 'Host did not exit within 5 seconds.' }
    $hostProcess.WaitForExit(); $watch.Stop()
    $resultPath=Join-Path $state.evidenceDirectory ($state.runId+'.result.json')
    $result=Get-Content $resultPath -Raw | ConvertFrom-Json
    if ($hostProcess.ExitCode -ne 0 -or $result.phase -ne 'Canceled' -or $result.exitCode -ne 0) { throw 'Host exited before clean simulation cancellation.' }
    $refused=$false
    try { $null=Invoke-WebRequest "$url/api/state" -TimeoutSec 2 } catch { $refused=$true }
    if (!$refused) { throw 'Listener still active after shutdown.' }
    @{Passed=$true;ChangedConfig='changed.json';ProductPid=$hostProcess.Id;ConsolePids=$members;HostExitCode=$hostProcess.ExitCode;SimulationExitCode=$result.exitCode;SimulationPhase=$result.phase;ShutdownMilliseconds=$watch.Elapsed.TotalMilliseconds;PortClosed=$refused;ResultFile=$resultPath;Finished=[DateTimeOffset]::Now.ToString('o')} | ConvertTo-Json | Set-Content "$root/verification.json"
    Write-Output "PASS: changed route and host Ctrl+C during motion; simulator joined and port closed. $root"
}
catch { $_ | Out-String | Set-Content "$root/failure.log"; throw }
finally {
    if ($attached) { $null=[MapHostControl]::FreeConsole(); $null=[MapHostControl]::SetConsoleCtrlHandler([IntPtr]::Zero,$false) }
    if ($null -ne $hostProcess) { if (!$hostProcess.HasExited) { $hostProcess.Kill(); $hostProcess.WaitForExit() }; $hostProcess.Dispose() }
}
