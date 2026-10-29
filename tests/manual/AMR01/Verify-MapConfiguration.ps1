param(
 [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,48}$')][string]$SessionRunId,
 [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,48}$')][string]$RunId
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location $repo
$session=Get-Content -LiteralPath "evidence/AMR/$SessionRunId/session.json" -Raw|ConvertFrom-Json
$root=Join-Path $repo "evidence/AMR/$RunId"
if(Test-Path -LiteralPath $root){throw 'Use a new RunId.'}
New-Item -ItemType Directory -Path $root|Out-Null
$config=Join-Path $root 'settings.json'
$server=$null
$results=[Collections.Generic.List[object]]::new()
$samples=[Collections.Generic.List[object]]::new()
$headers=@{'X-Simulator-Map'='1'}
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class SettingsMapLaunch { [DllImport("kernel32.dll")] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add); }
'@
function Require([bool]$condition,[string]$message){if(!$condition){throw $message}}
function Save([string]$name,$data){$data|ConvertTo-Json -Depth 20|Set-Content -LiteralPath "$root/$name.json" -Encoding utf8;$results.Add(@{name=$name;verdict='passed'})}
function Read-State {
 $value=Invoke-RestMethod "$script:url/api/state" -TimeoutSec 5
 if($value.position){Require ($value.position.col -eq $value.receivedTelemetry.position.x -and $value.position.row -eq -$value.receivedTelemetry.position.y) 'Marker differs from control-received MQTT coordinates.'}
 $samples.Add($value)
 return $value
}
function Start-Map([string]$name){
 $null=[SettingsMapLaunch]::SetConsoleCtrlHandler([IntPtr]::Zero,$false)
 $script:server=Start-Process $session.Exe -ArgumentList @('--config',"`"$config`"") -WindowStyle Hidden -WorkingDirectory $repo -RedirectStandardOutput "$root/$name.stdout.log" -RedirectStandardError "$root/$name.stderr.log" -PassThru
 $script:startTicks=$server.StartTime.ToUniversalTime().Ticks
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds(10)
 do {
  $line=Get-Content -LiteralPath "$root/$name.stdout.log" -Raw
  if($line -match 'MapReady Url=(http://127\.0\.0\.1:\d+)'){$script:url=$Matches[1];return}
  if($server.HasExited){throw 'Configuration host exited before ready.'}
  Start-Sleep -Milliseconds 40
 }while([DateTimeOffset]::UtcNow -lt $deadline)
 throw 'Configuration host startup timed out.'
}
function Stop-Map([string]$name){
 if(!$server){return}
 if(!$server.HasExited){
  Require ($server.Path -eq $session.Exe -and $server.StartTime.ToUniversalTime().Ticks -eq $script:startTicks) 'Owned host identity changed.'
  & pwsh -NoProfile -File "$PSScriptRoot/Send-OwnedCtrlC.ps1" -TargetProcessId $server.Id -StartTicks $script:startTicks -ResultPath "$root/$name-stop.json"
  Require ($LASTEXITCODE -eq 0) 'Graceful host stop failed.'
  Require ($server.WaitForExit(10000)) 'Owned host did not exit.'
 }
 $server.Refresh()
 Require ($server.ExitCode -eq 0) 'Configuration host exit was not successful.'
 $server.Dispose();$script:server=$null
}
function Complete {
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds(10)
 do{$ready=Read-State;if($ready.navigation.phase -eq 'Ready'){break};Start-Sleep -Milliseconds 30}while([DateTimeOffset]::UtcNow -lt $deadline)
 Require ($ready.navigation.phase -eq 'Ready') 'Configured AMR session is not ready.'
 Invoke-RestMethod "$script:url/api/start" -Method Post -Headers $headers -TimeoutSec 5|Out-Null
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds(10)
 do {
  $state=Read-State
  if($state.navigation.phase -eq 'Completed'){return $state}
  Require ($state.navigation.phase -ne 'Failed') "MQTT failed: $($state.failure)"
  Start-Sleep -Milliseconds 30
 }while([DateTimeOffset]::UtcNow -lt $deadline)
 throw 'Configured mission did not complete.'
}
try {
 $settings=@{amrId='CONFIG_AMR_A';map=@(@(0,0,0,0,0,0,0,0),@(0,1,1,1,1,1,1,0),@(0,0,0,0,0,0,0,0));start=@{row=0;col=0};waypoints=@(@{row=0;col=7},@{row=2;col=7});stepIntervalMs=100;webPort=0;mqtt=@{enabled=$true;host='127.0.0.1';port=$session.BrokerPort;telemetryMs=70;heartbeatMs=200}}
 $settings|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $config -Encoding utf8
 Copy-Item -LiteralPath $config -Destination "$root/profile-a.json"
 Start-Map 'profile-a'
 $initial=Read-State
 Require ($initial.navigation.map.Count -eq 3 -and $initial.navigation.map[0].Count -eq 8 -and $initial.stepIntervalMs -eq 100 -and $initial.amrId -eq 'CONFIG_AMR_A' -and $initial.mqttEndpoint -eq "127.0.0.1:$($session.BrokerPort)") 'File-only settings were not applied.'
 Save 'file-only-settings-applied' $initial
 $complete=Complete
 Require ($complete.position.col -eq 7 -and $complete.position.row -eq 2 -and $complete.navigation.moves -eq 9) 'Expanded map coordinates did not arrive through MQTT.'
 Require (@($samples|Where-Object {$_.receivedTelemetry.state -eq 'BUSY' -and [Math]::Abs($_.receivedTelemetry.speed-10) -lt 0.000001}).Count -gt 0) 'Configured step interval was not reflected in nominal speed.'
 Save 'expanded-map-mqtt-arrival' $complete
 $settings.amrId='CONFIG_AMR_B';$settings.map=@(@(0,0,0),@(0,1,0));$settings.waypoints=@(@{row=1;col=2});$settings.stepIntervalMs=180
 $settings|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $config -Encoding utf8
 Copy-Item -LiteralPath $config -Destination "$root/profile-b.json"
 $unchanged=Read-State
 Require ($unchanged.amrId -eq 'CONFIG_AMR_A' -and $unchanged.stepIntervalMs -eq 100) 'Running host changed underneath an active configuration snapshot.'
 Save 'file-edit-waits-for-restart' $unchanged
 Stop-Map 'profile-a'
 Start-Map 'profile-b'
 $changed=Read-State
 Require ($changed.amrId -eq 'CONFIG_AMR_B' -and $changed.navigation.map.Count -eq 2 -and $changed.navigation.map[0].Count -eq 3 -and $changed.stepIntervalMs -eq 180) 'Restart did not apply edited configuration.'
 Save 'restart-applies-edited-file' $changed
 $complete=Complete
 Require ($complete.position.row -eq 1 -and $complete.position.col -eq 2 -and $complete.navigation.moves -eq 3) 'Edited route did not arrive through MQTT.'
 Save 'edited-route-mqtt-arrival' $complete
 Stop-Map 'profile-b'
 $settings.stepIntervalMs=0
 $settings|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $config -Encoding utf8
 $invalid=Start-Process $session.Exe -ArgumentList @('--config',"`"$config`"") -WindowStyle Hidden -WorkingDirectory $repo -RedirectStandardOutput "$root/invalid.stdout.log" -RedirectStandardError "$root/invalid.stderr.log" -PassThru
 Require ($invalid.WaitForExit(10000)) 'Invalid configuration host did not exit.'
 $invalid.Refresh();$errorText=Get-Content -LiteralPath "$root/invalid.stderr.log" -Raw
 Require ($invalid.ExitCode -eq 1 -and $errorText -match 'stepIntervalMs') 'Invalid configuration was not rejected before startup.'
 Save 'invalid-file-rejected' @{exitCode=$invalid.ExitCode;error=$errorText}
 $invalid.Dispose()
}catch{
 $results.Add(@{name='failure';verdict='failed';reason=$_.Exception.Message})
 throw
}finally{
 if($server){Stop-Map 'cleanup'}
 $results|ConvertTo-Json -Depth 6|Set-Content -LiteralPath "$root/verification.json" -Encoding utf8
 $samples|ForEach-Object {ConvertTo-Json $_ -Depth 16 -Compress}|Set-Content -LiteralPath "$root/samples.jsonl" -Encoding utf8
}
$results|ConvertTo-Json -Depth 6
