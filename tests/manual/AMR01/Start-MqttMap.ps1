param(
 [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,48}$')][string]$RunId,
 [string]$MapExe,
 [string]$Config='config/map-demo/settings.json'
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location $repo
$root=Join-Path $repo "evidence/AMR/$RunId"
if(Test-Path $root){throw 'Use a new RunId.'}
if(!$MapExe){$MapExe=Join-Path $repo 'tools/Amr.Simulator.Map/bin/Debug/net10.0/Amr.Simulator.Map.exe'}
$MapExe=(Resolve-Path -LiteralPath $MapExe).Path
$Config=(Resolve-Path -LiteralPath $Config).Path
$null=New-Item -ItemType Directory $root
$docker=Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'
$container=$null; $server=$null; $ready=$false
function Docker([string[]]$Arguments){$text=& $docker @Arguments 2>&1|Out-String;if($LASTEXITCODE -ne 0){throw "Docker failed: $text"};return $text.Trim()}
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class MqttMapLaunch { [DllImport("kernel32.dll")] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add); }
'@
try {
 Docker @('ps','-a','--no-trunc','--format','{{json .}}')|Set-Content "$root/containers-before.jsonl"
 $image=Docker @('image','inspect','eclipse-mosquitto:2.1.2-alpine','--format','{{.Id}}')
 "listener 1883 0.0.0.0`nallow_anonymous true`npersistence false`nlog_dest stdout`nlog_type all`nconnection_messages true"|Set-Content "$root/broker.conf" -Encoding ascii
 $container=Docker @('run','--detach','--pull','never','--name',"amr-map-$RunId",'--label',"amr.map.run=$RunId",'--publish','127.0.0.1::1883','--tmpfs','/mosquitto/data','--tmpfs','/mosquitto/log','--mount',"type=bind,source=$root/broker.conf,target=/mosquitto/config/mosquitto.conf,readonly",'eclipse-mosquitto:2.1.2-alpine')
 if($container -notmatch '^[a-f0-9]{64}$'){throw 'Unconfirmed Broker ID'}
 $mapping=Docker @('port',$container,'1883/tcp')
 if($mapping -notmatch '^127\.0\.0\.1:(\d+)$'){throw 'Expected loopback Broker'}
 $port=[int]$Matches[1]
 $brokerReady=$false
 for($i=0;$i -lt 30;$i++){if((Docker @('logs',$container)) -match 'mosquitto version .* running'){$brokerReady=$true;break};Start-Sleep -Milliseconds 100}
 if(!$brokerReady){throw 'Broker did not start'}
 $null=[MqttMapLaunch]::SetConsoleCtrlHandler([IntPtr]::Zero,$false)
 $server=Start-Process $MapExe -ArgumentList @('--port','0','--config',"`"$Config`"",'--mqtt-host','127.0.0.1','--mqtt-port',"$port",'--telemetry-ms','200','--heartbeat-ms','1000') -WorkingDirectory $repo -WindowStyle Hidden -RedirectStandardOutput "$root/server.stdout.log" -RedirectStandardError "$root/server.stderr.log" -PassThru
 $null=$server.Handle
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds(10)
 do {
  $text=[string](Get-Content "$root/server.stdout.log" -Raw)
  if($text -match 'MapReady Url=(http://127\.0\.0\.1:\d+)'){$url=$Matches[1];break}
  if($server.HasExited){throw 'Map server exited before ready'}
  Start-Sleep -Milliseconds 30
 }while([DateTimeOffset]::UtcNow -lt $deadline)
 if(!$url){throw 'Map startup timeout'}
 $state=Invoke-RestMethod "$url/api/state"
 if($state.source -ne 'mqtt-grid-control-receiver' -or $state.position){throw 'Unexpected initial MQTT map state'}
 @{RunId=$RunId;ContainerId=$container;BrokerPort=$port;ImageId=$image;ServerPid=$server.Id;ServerStartTicks=$server.StartTime.ToUniversalTime().Ticks;Exe=$MapExe;Url=$url;ProductEvidence=$state.evidenceDirectory;Started=[DateTimeOffset]::Now.ToString('o')}|ConvertTo-Json|Set-Content "$root/session.json"
 $ready=$true
 Write-Output "Map ready: $url"
 Write-Output "Stop: pwsh -NoProfile -File tests/manual/AMR01/Stop-MqttMap.ps1 -RunId $RunId"
} finally {
 if(!$ready){
  if($server -and !$server.HasExited){$server.Kill();$server.WaitForExit()}
  if($container -match '^[a-f0-9]{64}$' -and (Docker @('inspect',$container,'--format','{{index .Config.Labels "amr.map.run"}}')) -eq $RunId){Docker @('logs',$container)|Set-Content "$root/broker.log";$null=Docker @('rm','-f','-v',$container)}
 }
 if($server){$server.Dispose()}
}
