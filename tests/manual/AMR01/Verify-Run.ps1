param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunId,
    [switch]$CancelWorker,
    [ValidateSet('cancel', 'long-interval')][string]$CancelCase = 'cancel'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$run = Join-Path $repo "evidence/AMR/$RunId"
$exe = Join-Path $repo 'src/Amr.Simulator/bin/Debug/net10.0/Amr.Simulator.exe'
if ($CancelWorker) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class AmrRunControl {
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
        # 비대화형 부모의 Ctrl+C 무시 플래그를 자식에게 상속하지 않는다.
        $null = [AmrRunControl]::SetConsoleCtrlHandler([IntPtr]::Zero, $false)
        $product = Start-Process $exe -ArgumentList @('--config', "`"$run/$CancelCase.json`"") -WorkingDirectory $repo -WindowStyle Hidden -RedirectStandardOutput "$run/$CancelCase.stdout.log" -RedirectStandardError "$run/$CancelCase.stderr.log" -PassThru
        $null = $product.Handle
        $ready = if ($CancelCase -eq 'cancel') { 'AmrMoved' } else { 'SimulationStarted' }
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        do {
            $log = [string](Get-Content "$run/$CancelCase.stdout.log" -Raw)
            if ($log -and $log.Contains($ready)) { break }
            if ($product.HasExited) { throw 'Product exited before cancellation point.' }
            Start-Sleep -Milliseconds 25
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if (!$log -or !$log.Contains($ready)) { throw 'Cancellation point was not reached.' }
        $null = [AmrRunControl]::FreeConsole()
        if (![AmrRunControl]::AttachConsole([uint32]$product.Id)) { throw 'AttachConsole failed.' }
        $attached = $true
        $members = [uint32[]]::new(16)
        $count = [AmrRunControl]::GetConsoleProcessList($members, 16)
        if ($count -eq 0 -or $count -gt 16) { throw 'Console isolation not verified.' }
        $members = @($members[0..($count-1)])
        if (@($members | Where-Object { $_ -ne $PID -and $_ -ne $product.Id }).Count) { throw 'Unexpected console member; no signal sent.' }
        if (![AmrRunControl]::SetConsoleCtrlHandler([IntPtr]::Zero, $true)) { throw 'Controller protection failed.' }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        if (![AmrRunControl]::GenerateConsoleCtrlEvent(0, 0)) { throw 'CTRL_C_EVENT failed.' }
        if (!$product.WaitForExit(5000)) { throw 'Cancellation did not exit within 5 seconds.' }
        $product.WaitForExit()
        $watch.Stop()
        $log = [string](Get-Content "$run/$CancelCase.stdout.log" -Raw)
        $errors = [string](Get-Content "$run/$CancelCase.stderr.log" -Raw)
        if ($product.ExitCode -ne 0 -or $errors -or !$log.Contains('SimulationCanceled') -or $log.Contains('SimulationCompleted') -or $log.Contains('InitializationCanceled')) { throw 'Cancellation result mismatch.' }
        @{ Case=$CancelCase; ExitCode=$product.ExitCode; ProductPid=$product.Id; ConsolePids=$members; ShutdownMilliseconds=$watch.Elapsed.TotalMilliseconds; Passed=$true; Signal='Windows CTRL_C_EVENT'; Exited=$product.HasExited } | ConvertTo-Json | Set-Content "$run/$CancelCase.result.json"
    }
    catch { $_ | Out-String | Set-Content "$run/$CancelCase.failure.log"; throw }
    finally {
        if ($attached) { $null=[AmrRunControl]::FreeConsole(); $null=[AmrRunControl]::SetConsoleCtrlHandler([IntPtr]::Zero,$false) }
        if ($null -ne $product) { if (!$product.HasExited) { $product.Kill(); $product.WaitForExit() }; $product.Dispose() }
    }
    exit 0
}

if (Test-Path $run) { throw 'Use a new RunId; previous evidence is preserved.' }
$null = New-Item -ItemType Directory -Path $run
Set-Location $repo
$started = [DateTimeOffset]::Now
$normal = Get-Content config/simulator.single.json -Raw
$changed = $normal | ConvertFrom-Json
$changed.initialPosition.x=5; $changed.initialPosition.y=3; $changed.speedMps=2
$changed.waypoints=@(@{x=5;y=4},@{x=7;y=4})
$invalid = $normal | ConvertFrom-Json
$invalid.speedMps=0
$cancel = $normal | ConvertFrom-Json
$cancel.waypoints=@(@{x=100;y=0})
$long = $normal | ConvertFrom-Json
$long.updateIntervalMs=[long]::MaxValue
$stationary = $normal | ConvertFrom-Json
$stationary.waypoints=@(@{x=-1;y=0},@{x=-1;y=0})
$normal | Set-Content "$run/normal.json"
$changed | ConvertTo-Json -Depth 8 | Set-Content "$run/changed.json"
$invalid | ConvertTo-Json -Depth 8 | Set-Content "$run/invalid.json"
$cancel | ConvertTo-Json -Depth 8 | Set-Content "$run/cancel.json"
$long | ConvertTo-Json -Depth 8 | Set-Content "$run/long-interval.json"
$stationary | ConvertTo-Json -Depth 8 | Set-Content "$run/stationary.json"
$results = @()
foreach ($case in @(
    @{Name='normal';Code=0;Expected='SimulationCompleted AmrId=AMR_001 X=1 Y=2 State=IDLE CurrentSpeedMps=0'},
    @{Name='changed';Code=0;Expected='SimulationCompleted AmrId=AMR_001 X=7 Y=4 State=IDLE CurrentSpeedMps=0'},
    @{Name='invalid';Code=1;Expected='ConfigurationRejected Field=$.speedMps'},
    @{Name='stationary';Code=0;Expected='SimulationCompleted AmrId=AMR_001 X=-1 Y=0 State=IDLE CurrentSpeedMps=0'}
)) {
    $name=$case.Name
    $info=[Diagnostics.ProcessStartInfo]::new($exe)
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true; $info.WorkingDirectory=$repo
    $info.ArgumentList.Add('--config'); $info.ArgumentList.Add("$run/$name.json")
    $product=[Diagnostics.Process]::new(); $product.StartInfo=$info
    try {
        $null=$product.Start()
        $outTask=$product.StandardOutput.ReadToEndAsync(); $errTask=$product.StandardError.ReadToEndAsync()
        if (!$product.WaitForExit(15000)) { throw "$name exceeded 15 seconds." }
        $stdout=$outTask.GetAwaiter().GetResult(); $stderr=$errTask.GetAwaiter().GetResult()
        $stdout | Set-Content "$run/$name.stdout.log"; $stderr | Set-Content "$run/$name.stderr.log"
        $passed=$product.ExitCode -eq $case.Code -and ($stdout+$stderr).Contains($case.Expected)
        if ($case.Code -eq 0) { $passed=$passed -and !$stderr }
        if ($name -eq 'invalid') { $passed=$passed -and !$stdout }
        if ($name -in @('normal','changed')) { $passed=$passed -and $stdout.Contains('State=BUSY CurrentSpeedMps=') -and $stdout.Contains('WaypointReached AmrId=AMR_001 Index=0') -and $stdout.Contains('WaypointReached AmrId=AMR_001 Index=1') }
        if ($name -eq 'stationary') { $passed=$passed -and !$stdout.Contains('BUSY') }
        $results+=@{Case=$name;Expected=$case.Expected;ExitCode=$product.ExitCode;Passed=$passed;ProductPid=$product.Id}
        $results | ConvertTo-Json -Depth 5 | Set-Content "$run/cases.json"
        if (!$passed) { throw "$name failed. See raw logs." }
    }
    finally { if (!$product.HasExited) { $product.Kill(); $product.WaitForExit() }; $product.Dispose() }
}
foreach ($name in @('cancel','long-interval')) {
    $worker = Start-Process pwsh -ArgumentList @('-NoProfile','-File',"`"$PSCommandPath`"",'-RunId',$RunId,'-CancelWorker','-CancelCase',$name) -WindowStyle Hidden -RedirectStandardOutput "$run/$name.worker.stdout.log" -RedirectStandardError "$run/$name.worker.stderr.log" -PassThru
    try {
        $null=$worker.Handle
        if (!$worker.WaitForExit(25000)) { throw "$name worker timeout." }
        $worker.WaitForExit()
        if ($worker.ExitCode -ne 0) { throw "$name worker failed. See worker logs." }
        $results+=Get-Content "$run/$name.result.json" -Raw | ConvertFrom-Json
    }
    finally { if (!$worker.HasExited) { $worker.Kill($true); $worker.WaitForExit() }; $worker.Dispose() }
}
@{ Started=$started.ToString('o'); Finished=[DateTimeOffset]::Now.ToString('o'); Head=(git rev-parse HEAD); Branch=(git branch --show-current); Sdk=(dotnet --version); OS=[Environment]::OSVersion.VersionString; DllHash=(Get-FileHash ($exe -replace '\.exe$','.dll')).Hash; Cases=$results; Passed=$true } | ConvertTo-Json -Depth 6 | Set-Content "$run/verification.json"
Get-ChildItem "$run/*.json" | Get-FileHash | Select-Object Path,Hash | ConvertTo-Json | Set-Content "$run/input-and-result-sha256.json"
Write-Output "PASS: six actual Simulator processes; arrival, changed settings, invalid settings, stationary route, moving Ctrl+C, long interval Ctrl+C. $run"
