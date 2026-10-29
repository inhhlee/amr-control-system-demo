param(
 [Parameter(Mandatory)][ValidateSet('Launch','Healthy','Timeout','Recovery','Resume','SlowQueue','BrokerDown','Cleanup')][string]$Phase,
 [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,48}$')][string]$RunId,
 [string]$ProbeExe='tests/manual/COM02-A/FleetProbe/bin/TwoAmrVerify/FleetProbe.exe'
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../../..").Path
Set-Location $repo
$root=Join-Path $repo "evidence/COM-02/$RunId"
$docker=(Get-Command docker -CommandType Application|Select-Object -First 1).Source
$headers=@{'X-Simulator-Map'='1'}
function Require([bool]$ok,[string]$message){if(!$ok){throw $message}}
function Docker([string[]]$Arguments){$value=& $docker @Arguments 2>&1|Out-String;if($LASTEXITCODE -ne 0){throw "Docker failed: $value"};$value.Trim()}
function Save([string]$name,$data){$data|ConvertTo-Json -Depth 40|Set-Content "$root/$name.json" -Encoding utf8}
function State {Invoke-RestMethod "$url/api/state" -TimeoutSec 3}
function Post([string]$path,$body=$null){$params=@{Uri="$url/$path";Method='Post';Headers=$headers;TimeoutSec=3};if($null -ne $body){$params.ContentType='application/json';$params.Body=$body|ConvertTo-Json -Compress};Invoke-RestMethod @params}
function Status($state,[string]$id){$state.domain.statuses|Where-Object{$_.amrId -eq $id}}
function View($state,[string]$id){$state.fleet|Where-Object{$_.amrId -eq $id}}
function Wait-State([scriptblock]$condition,[int]$seconds=10){$deadline=[DateTimeOffset]::UtcNow.AddSeconds($seconds);do{$s=State;if(&$condition $s){return $s};Start-Sleep -Milliseconds 40}while([DateTimeOffset]::UtcNow -lt $deadline);throw "State timeout: $($s.domain.statuses|ConvertTo-Json -Compress -Depth 8)"}
function Owner {Require ((Docker @('inspect',$owned.ContainerId,'--format','{{index .Config.Labels "amr.fleet.run"}}')) -eq $RunId) 'Owned Broker label mismatch.'}
if($Phase -eq 'Launch'){
 Require (!(Test-Path -LiteralPath $root)) 'Use a fresh RunId.'
 $null=New-Item -ItemType Directory $root
 Docker @('ps','-a','--no-trunc','--format','{{json .}}')|Set-Content "$root/containers-before.jsonl"
 "listener 1883 0.0.0.0`nallow_anonymous true`npersistence false`nlog_dest stdout`nlog_type all"|Set-Content "$root/broker.conf" -Encoding ascii
 $container=Docker @('run','--detach','--pull','never','--name',"amr-fleet-$RunId",'--label',"amr.fleet.run=$RunId",'--publish','127.0.0.1::1883','--tmpfs','/mosquitto/data','--tmpfs','/mosquitto/log','--mount',"type=bind,source=$root/broker.conf,target=/mosquitto/config/mosquitto.conf,readonly",'eclipse-mosquitto:2.1.2-alpine')
 Require ($container -match '^[a-f0-9]{64}$') 'Unconfirmed Broker ID.'
 $mapping=Docker @('port',$container,'1883/tcp');Require ($mapping -match '^127\.0\.0\.1:(\d+)$') 'Unexpected port mapping.';$port=[int]$Matches[1]
 foreach($file in @('settings.json','settings.amr002.json')){
  $mission=Get-Content "config/map-demo/$file" -Raw|ConvertFrom-Json
  $mission.mqtt.port=$port;$mission.stepIntervalMs=3000;$mission.mqtt.heartbeatMs=200;$mission.mqtt.telemetryMs=100
  $mission|ConvertTo-Json -Depth 12|Set-Content "$root/$file" -Encoding utf8
 }
 @{webPort=0;missions=@('settings.json','settings.amr002.json')}|ConvertTo-Json|Set-Content "$root/fleet.json" -Encoding utf8
 $exe=(Resolve-Path -LiteralPath $ProbeExe).Path
 $p=Start-Process $exe -ArgumentList @("`"$root/fleet.json`"","`"$root`"") -WindowStyle Hidden -WorkingDirectory $repo -RedirectStandardOutput "$root/probe.stdout.log" -RedirectStandardError "$root/probe.stderr.log" -PassThru
 Save 'owned-session' @{ContainerId=$container;BrokerPort=$port;Pid=$p.Id;StartTicks=$p.StartTime.ToUniversalTime().Ticks;Exe=$exe}
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds(10);while(!(Test-Path "$root/probe-session.json")){Require (!$p.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline) 'Probe startup failed.';Start-Sleep -Milliseconds 40}
 $p.Dispose();Get-Content "$root/probe-session.json";exit 0
}
$owned=Get-Content "$root/owned-session.json" -Raw|ConvertFrom-Json
$probe=Get-Content "$root/probe-session.json" -Raw|ConvertFrom-Json
$url=$probe.url
switch($Phase){
 Healthy {
  $ready=Wait-State {param($s) $s.domain.devices.Count -eq 2 -and @($s.domain.statuses|Where-Object{$_.communication -ne 'CONNECTED'}).Count -eq 0}
  Start-Sleep -Milliseconds 800;$after=State
  foreach($id in @('AMR_001','AMR_002')){Require ((View $after $id).heartbeatCount -gt (View $ready $id).heartbeatCount) 'Ready Heartbeat stopped.';Post 'api/start' @{amrId=$id}|Out-Null}
  $running=Wait-State {param($s) @($s.domain.statuses|Where-Object{$_.mission.state -eq 'Running'}).Count -eq 2}
  Save '01-two-alive-running' @{ready=$ready;after=$after;running=$running}
 }
 Timeout {
  foreach($id in @('AMR_001','AMR_002')){Post 'api/grid/reset' @{amrId=$id}|Out-Null;Post 'api/start' @{amrId=$id}|Out-Null}
  $before=Wait-State {param($s) @($s.domain.statuses|Where-Object{$_.mission.state -eq 'Running'}).Count -eq 2}
  Post 'verify/signal' @{amrId='AMR_001';mode='heartbeat'}|Out-Null
  $watch=[Diagnostics.Stopwatch]::StartNew()
  $lost=Wait-State {param($s) (Status $s 'AMR_001').communication -eq 'COMM_LOST'} 8
  $firstMs=$watch.ElapsedMilliseconds
  Require ((Status $lost 'AMR_001').mission.state -eq 'OnHold' -and (Status $lost 'AMR_002').communication -eq 'CONNECTED') 'A1 Timeout affected A2 or did not hold mission.'
  Require ($lost.brokerState -eq 'Subscribed' -and $lost.domain.alarms.Count -eq 1 -and $lost.domain.alarms[0].amrId -eq 'AMR_001') 'Wrong connection/alarm classification.'
  Owner
  foreach($input in @(@{id='AMR_001';status='BAD'},@{id='OTHER';status='OK'})){$payload=@{id=$input.id;ts=123;status=$input.status}|ConvertTo-Json -Compress;$null=Docker @('exec',$owned.ContainerId,'mosquitto_pub','-h','127.0.0.1','-t',"amr/$($input.id)/heartbeat",'-m',$payload)}
  Start-Sleep -Milliseconds 700;$still=State
  Require ((Status $still 'AMR_001').communication -eq 'COMM_LOST' -and (View $still 'AMR_001').telemetryCount -gt (View $before 'AMR_001').telemetryCount) 'Invalid/other/Telemetry incorrectly recovered A1.'
  Require ((View $still 'AMR_002').heartbeatCount -gt (View $before 'AMR_002').heartbeatCount -and (View $still 'AMR_002').heartbeatMonitor.timeoutCount -eq 0) 'A2 monitoring was blocked.'
  Save '02-a1-timeout-a2-alive' @{before=$before;firstTimeout=$lost;after=$still;faultToFirstObservedMs=$firstMs;scenarioElapsedMs=$watch.ElapsedMilliseconds}
 }
 Recovery {
  Post 'verify/signal' @{amrId='AMR_001';mode='all'}|Out-Null
  $recovered=Wait-State {param($s) (Status $s 'AMR_001').communication -eq 'CONNECTED'}
  Require ((Status $recovered 'AMR_001').mission.state -eq 'OnHold' -and $recovered.domain.alarms.Count -eq 0) 'Recovery auto-resumed mission or failed to clear active alarm.'
  Save '03-recovered-still-held' $recovered
 }
 Resume {
  $before=State;Require ((Status $before 'AMR_001').mission.state -eq 'OnHold') 'A1 not awaiting explicit resume.'
  Post 'api/start' @{amrId='AMR_001'}|Out-Null
  $after=State
  Require ((Status $after 'AMR_001').mission.state -in @('Running','Completed') -and (Status $after 'AMR_002').communication -eq 'CONNECTED') 'Explicit resume failed or affected B.'
  Save '04-explicit-resume' @{before=$before;after=$after}
 }
 SlowQueue {
  Post 'verify/delay' @{amrId='AMR_001';blocked=$true}|Out-Null
  try{
   Start-Sleep -Milliseconds 300;$before=State;$counts=Invoke-RestMethod "$url/verify/delay"
   Start-Sleep -Milliseconds 1400;$after=State;$later=Invoke-RestMethod "$url/verify/delay"
   Require ($counts.AMR_001 -eq $later.AMR_001 -and $later.AMR_002 -gt $counts.AMR_002) 'Per-AMR queue independence failed.'
   Require ((Status $after 'AMR_001').communication -eq 'CONNECTED' -and (Status $after 'AMR_001').lastHeartbeatReceivedAt -gt (Status $before 'AMR_001').lastHeartbeatReceivedAt) 'Heartbeat observation waited for slow Core queue.'
   Save '05-independent-queues' @{before=$before;after=$after;countsBefore=$counts;countsAfter=$later}
  }finally{Post 'verify/delay' @{amrId='AMR_001';blocked=$false}|Out-Null}
 }
 BrokerDown {
  Owner
  foreach($id in @('AMR_001','AMR_002')){Post 'api/grid/reset' @{amrId=$id}|Out-Null;Post 'api/start' @{amrId=$id}|Out-Null}
  $before=Wait-State {param($s) @($s.domain.statuses|Where-Object{$_.mission.state -eq 'Running'}).Count -eq 2}
  $null=Docker @('stop','--time','3',$owned.ContainerId)
  $lost=Wait-State {param($s) @($s.domain.statuses|Where-Object{$_.communication -eq 'BROKER_LOST'}).Count -eq 2}
  Start-Sleep -Milliseconds 5500;$after=State
  foreach($id in @('AMR_001','AMR_002')){Require ((View $after $id).heartbeatMonitor.state -eq 'Stopped' -and (View $after $id).heartbeatMonitor.timeoutCount -eq (View $before $id).heartbeatMonitor.timeoutCount) 'Broker loss caused extra AMR Timeout.'}
  Require ($after.domain.alarms.Count -eq 2 -and $after.brokerState -eq 'Failed') 'Both domain Broker-loss alarms missing.'
  Require (@($after.domain.statuses|Where-Object{$_.mission.state -eq 'OnHold'}).Count -eq 2) 'Broker loss did not hold active missions.'
  foreach($path in @('api/cancel','api/grid/reset')){
   $denied=Invoke-WebRequest "$url/$path" -Method Post -Headers $headers -ContentType 'application/json' -Body '{"amrId":"AMR_001"}' -SkipHttpErrorCheck
   Require ([int]$denied.StatusCode -ge 400) 'Disconnected control request was accepted.'
  }
  $after=State;Require (@($after.domain.statuses|Where-Object{$_.mission.state -eq 'OnHold'}).Count -eq 2) 'Rejected control changed held missions.'
  Save '06-broker-loss-both-stopped' @{before=$before;firstLoss=$lost;after=$after}
 }
 Cleanup {
  Owner;Post 'verify/shutdown'|Out-Null
  $p=Get-Process -Id $owned.Pid -ErrorAction SilentlyContinue
  if($p){Require ($p.StartTime.ToUniversalTime().Ticks -eq $owned.StartTicks -and $p.Path -eq $owned.Exe) 'Probe identity mismatch.';Require ($p.WaitForExit(10000)) 'Probe did not exit.';$p.Dispose()}
  Docker @('logs','--timestamps',$owned.ContainerId)|Set-Content "$root/broker.log"
  $null=Docker @('rm','-v',$owned.ContainerId)
  $after=Docker @('ps','-a','--no-trunc','--format','{{json .}}');$after|Set-Content "$root/containers-after.jsonl"
  $current=@($after -split "`n"|Where-Object{$_}|ForEach-Object{$_|ConvertFrom-Json})
  $before=@(Get-Content "$root/containers-before.jsonl"|Where-Object{$_}|ForEach-Object{$_|ConvertFrom-Json})
  foreach($item in $before){Require (@($current|Where-Object{$_.ID -eq $item.ID -and $_.State -eq $item.State}).Count -eq 1) 'Existing container changed.'}
  Save 'cleanup' @{gracefulHostExit=$true;ownedBrokerRemoved=$true;existingContainersPreserved=$true}
 }
}
Write-Output "$Phase passed: $url"
