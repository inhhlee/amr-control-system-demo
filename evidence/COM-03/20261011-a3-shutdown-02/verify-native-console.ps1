# Run this as a separate PowerShell process: it attaches only to the hidden test console it creates.
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$executable = Join-Path $repository 'src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.exe'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class A3ConsoleControl
{
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GenerateConsoleCtrlEvent(uint signal, uint groupId);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern uint GetConsoleProcessList([Out] uint[] processList, uint count);
}
'@

$results = @()
foreach ($number in 1..2) {
    $stdout = Join-Path $PSScriptRoot "native-$number.stdout.log"
    $stderr = Join-Path $PSScriptRoot "native-$number.stderr.log"
    $started = [DateTimeOffset]::UtcNow
    $process = Start-Process -FilePath $executable -ArgumentList @('--host','127.0.0.1','--port','1884','--connect-timeout-seconds','3') -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $null = $process.Handle
    $attached = $false
    try {
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
        do {
            if ($process.HasExited) { throw 'Console exited before Ctrl+C.' }
            $log = if (Test-Path -LiteralPath $stdout) { Get-Content -LiteralPath $stdout -Raw } else { '' }
            if ($log -match 'WaitingForShutdown') { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if ($log -notmatch 'WaitingForShutdown') { throw 'Console did not reach the connected wait state.' }
        Start-Sleep -Seconds 3
        if ($process.HasExited) { throw 'Console did not remain running before Ctrl+C.' }

        $null = [A3ConsoleControl]::FreeConsole()
        if (![A3ConsoleControl]::AttachConsole([uint32]$process.Id)) { throw "AttachConsole failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
        $attached = $true
        $consolePids = [uint32[]]::new(16)
        $count = [A3ConsoleControl]::GetConsoleProcessList($consolePids, 16)
        if ($count -eq 0 -or $count -gt 16) { throw 'Could not verify isolated console membership.' }
        $members = @($consolePids[0..($count - 1)])
        if (@($members | Where-Object { $_ -ne $PID -and $_ -ne $process.Id }).Count -ne 0) { throw 'Unexpected process shares the test console; no signal sent.' }
        if (![A3ConsoleControl]::SetConsoleCtrlHandler([IntPtr]::Zero, $true)) { throw 'Could not protect the test controller from its own signal.' }
        $sent = [DateTimeOffset]::UtcNow
        if (![A3ConsoleControl]::GenerateConsoleCtrlEvent(0, 0)) { throw 'CTRL_C_EVENT generation failed.' }
        if (!$process.WaitForExit(10000)) { throw 'Console did not exit within the test deadline.' }
        $process.WaitForExit()
        $code = $process.ExitCode
        $finished = [DateTimeOffset]::UtcNow
        $log = Get-Content -LiteralPath $stdout -Raw
        $clientId = [regex]::Match($log, 'ClientId=(\S+)').Groups[1].Value
        $states = [regex]::Matches($log, '(?m)^\S+ (Connecting|Connected|WaitingForShutdown[^\r\n]*|ShutdownRequested|Disconnecting|Disconnected|Disposed|ExitCode=0)\r?$') | ForEach-Object { $_.Groups[1].Value.Split(':')[0] }
        $stateOrder = $states -join ','
        $record = [pscustomobject]@{
            run=$number;processId=$process.Id;clientId=$clientId;startedUtc=$started;signalUtc=$sent;finishedUtc=$finished
            signal='Windows CTRL_C_EVENT (0), isolated child console';consoleMembers=$members
            heldAfterWaitStateSeconds=3;exitCode=$code;stateOrder=$stateOrder
            stderr=(Get-Content -LiteralPath $stderr -Raw);processExited=$process.HasExited
        }
        $record | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot "native-$number.json")
        if ($code -ne 0 -or $stateOrder -ne 'Connecting,Connected,WaitingForShutdown,ShutdownRequested,Disconnecting,Disconnected,Disposed,ExitCode=0' -or $record.stderr) { throw 'Native console result mismatch.' }
        $results += $record
    }
    finally {
        if ($attached) { $null = [A3ConsoleControl]::FreeConsole() }
        if (!$process.HasExited) {
            # Cleanup only this script's own failed test process. Never classify this path as normal shutdown.
            $process.Kill()
            $process.WaitForExit()
            'Test harness failed; its own child was terminated for cleanup.' | Set-Content -LiteralPath (Join-Path $PSScriptRoot "native-$number-harness-failure.txt")
        }
        $process.Dispose()
    }
}
if ($results[0].clientId -eq $results[1].clientId) { throw 'Client ID reused across new processes.' }
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'native-console-results.json')
Write-Output 'Both native console processes remained connected, received CTRL_C_EVENT and exited with code 0.'
