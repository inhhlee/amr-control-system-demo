param([Parameter(Mandatory)][int]$TargetProcessId, [Parameter(Mandatory)][long]$StartTicks, [Parameter(Mandatory)][string]$ResultPath)
$ErrorActionPreference='Stop'
$target=Get-Process -Id $TargetProcessId
if($target.StartTime.ToUniversalTime().Ticks -ne $StartTicks -or $target.ProcessName -notin @('Amr.Simulator','Amr.Communication.Console','Amr.Simulator.Map')){throw 'Owned process identity mismatch.'}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MqttDemoControl {
 [DllImport("kernel32.dll")] public static extern bool FreeConsole();
 [DllImport("kernel32.dll")] public static extern bool AttachConsole(uint pid);
 [DllImport("kernel32.dll")] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
 [DllImport("kernel32.dll")] public static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
 [DllImport("kernel32.dll")] public static extern uint GetConsoleProcessList([Out] uint[] list, uint count);
}
'@
$attached=$false
try {
 $null=[MqttDemoControl]::FreeConsole()
 if(![MqttDemoControl]::AttachConsole([uint32]$TargetProcessId)){throw 'AttachConsole failed.'}
 $attached=$true
 $members=[uint32[]]::new(16)
 $count=[MqttDemoControl]::GetConsoleProcessList($members,16)
 if($count -eq 0 -or $count -gt 16){throw 'Cannot verify console ownership.'}
 $members=@($members[0..($count-1)])
 if(@($members | Where-Object {$_ -ne $PID -and $_ -ne $TargetProcessId}).Count){throw 'Unowned process in console; no signal sent.'}
 if(![MqttDemoControl]::SetConsoleCtrlHandler([IntPtr]::Zero,$true)){throw 'Controller protection failed.'}
 if(![MqttDemoControl]::GenerateConsoleCtrlEvent(0,0)){throw 'Ctrl+C failed.'}
 if(!$target.WaitForExit(5000)){throw 'Target did not exit.'}
 @{Sent=$true;Pid=$TargetProcessId;StartTicks=$StartTicks;ConsolePids=$members} | ConvertTo-Json | Set-Content -LiteralPath $ResultPath
} finally { if($attached){$null=[MqttDemoControl]::FreeConsole()} }
