param(
 [Parameter(Mandatory)][ValidateSet('Launch','Healthy','NoHeartbeat','Recovery','Silent','Recovery2','BrokerDown','Cleanup')][string]$Phase,
 [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,48}$')][string]$RunId,
 [string]$ProbeExe = 'tests/manual/COM02-A/HeartbeatProbe/bin/HeartbeatVerify/HeartbeatProbe.exe'
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../../..").Path
Set-Location $repo
$root=Join-Path $repo "evidence/COM-02/$RunId"
$docker=(Get-Command docker -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$headers=@{'X-Simulator-Map'='1'}
function Require([bool]$ok,[string]$message){if(!$ok){throw $message}}
function State { Invoke-RestMethod "$url/api/grid/state" -TimeoutSec 3 }
function Post([string]$path,$body=$null){
 $p=@{Uri="$url/$path";Method='Post';Headers=$headers;TimeoutSec=3}
 if($null -ne $body){$p.ContentType='application/json';$p.Body=$body|ConvertTo-Json -Compress}
 Invoke-RestMethod @p
}
function Wait-State([scriptblock]$condition,[int]$seconds=10){
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds($seconds)
 do{$s=State;if(&$condition $s){return $s};Start-Sleep -Milliseconds 50}while([DateTimeOffset]::UtcNow -lt $deadline)
 throw "State timeout: $($s.navigation.phase) / $($s.heartbeatMonitor.state) / $($s.failure)"
}
function Save([string]$name,$value){$value|ConvertTo-Json -Depth 30|Set-Content "$root/$name.json" -Encoding utf8}
function Owner {
 $label=& $docker inspect $owned.ContainerId --format '{{index .Config.Labels "amr.heartbeat.run"}}'
 Require ($LASTEXITCODE -eq 0 -and $label -eq $RunId) 'Owned Broker label mismatch.'
}
if($Phase -eq 'Launch'){
 Require (!(Test-Path "$root/owned-session.json")) 'Use a fresh RunId.'
 $null=New-Item -ItemType Directory $root -Force
 & $docker ps -a --no-trunc --format '{{json .}}'|Set-Content "$root/containers-before.jsonl"
 "listener 1883 0.0.0.0`nallow_anonymous true`npersistence false`nlog_dest stdout`nlog_type all"|Set-Content "$root/broker.conf" -Encoding ascii
 $id=& $docker run --detach --pull never --name "amr-hb-$RunId" --label "amr.heartbeat.run=$RunId" --publish 127.0.0.1::1883 --tmpfs /mosquitto/data --tmpfs /mosquitto/log --mount "type=bind,source=$root/broker.conf,target=/mosquitto/config/mosquitto.conf,readonly" eclipse-mosquitto:2.1.2-alpine
 Require ($LASTEXITCODE -eq 0 -and $id -match '^[a-f0-9]{64}$') 'Broker launch failed.'
 $mapping=& $docker port $id 1883/tcp
 Require ($mapping -match '^127\.0\.0\.1:(\d+)$') 'Unexpected port mapping.'
 $port=[int]$Matches[1]
 $exe=(Resolve-Path -LiteralPath $ProbeExe).Path
 $process=Start-Process $exe -ArgumentList @("$port","`"$root`"") -WindowStyle Hidden -WorkingDirectory $repo -RedirectStandardOutput "$root/probe.stdout.log" -RedirectStandardError "$root/probe.stderr.log" -PassThru
 Save 'owned-session' @{ContainerId=$id;BrokerPort=$port;ProcessId=$process.Id;ProcessStartTicks=$process.StartTime.ToUniversalTime().Ticks;Exe=$exe}
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds(10)
 while(!(Test-Path "$root/probe-session.json")){Require (!$process.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline) 'Probe startup failed.';Start-Sleep -Milliseconds 100}
 $process.Dispose()
 Get-Content "$root/probe-session.json"
 exit 0
}
$owned=Get-Content "$root/owned-session.json" -Raw|ConvertFrom-Json
$probe=Get-Content "$root/probe-session.json" -Raw|ConvertFrom-Json
$url=$probe.url
switch($Phase){
 Healthy {
  $ready=Wait-State {param($s) $s.navigation.phase -eq 'Ready' -and $s.heartbeatMonitor.state -eq 'Alive'}
  Start-Sleep -Milliseconds 1200
  $idle=State
  Require ($idle.heartbeatCount -gt $ready.heartbeatCount -and $idle.navigation.moves -eq 0) 'Ready stopped Heartbeat.'
  Save '01-ready' @{before=$ready;after=$idle}
  Post 'api/grid/obstacle' @{row=1;col=1;blocked=$true}|Out-Null
  Post 'api/grid/start'|Out-Null
  $waiting=Wait-State {param($s) $s.navigation.phase -eq 'Waiting'}
  Start-Sleep -Milliseconds 1200
  $still=State
  Require ($still.navigation.moves -eq 0 -and $still.heartbeatCount -gt $waiting.heartbeatCount -and $still.heartbeatMonitor.state -eq 'Alive') 'Blocked waiting stopped Heartbeat.'
  Save '02-waiting' @{before=$waiting;after=$still}
  Post 'api/grid/obstacle' @{row=1;col=1;blocked=$false}|Out-Null
  $arrived=Wait-State {param($s) $s.navigation.phase -eq 'Completed'}
  Require ($arrived.navigation.moves -eq 9 -and $arrived.position.row -eq 4 -and $arrived.position.col -eq 5 -and $arrived.missionFinalReceived) 'Wrong arrival/final-receipt confirmation.'
  Start-Sleep -Milliseconds 1200
  $after=State
  Require ($after.heartbeatCount -gt $arrived.heartbeatCount -and $after.brokerState -eq 'Subscribed' -and $after.heartbeatMonitor.state -eq 'Alive' -and $after.receivedTelemetry.state -eq 'IDLE' -and $after.receivedTelemetry.speed -eq 0) 'Arrival ended the AMR communication session.'
  Save '03-arrival-alive' @{before=$arrived;after=$after}
 }
 {$_ -in 'NoHeartbeat','Silent'} {
  $before=State
  Post 'verify/signal' @{mode=$(if($Phase -eq 'NoHeartbeat'){'heartbeat'}else{'none'})}|Out-Null
  $watch=[Diagnostics.Stopwatch]::StartNew()
  $lost=Wait-State {param($s) $s.heartbeatMonitor.state -eq 'Unresponsive'} 8
  $firstObservedMs=$watch.ElapsedMilliseconds
  Require ($lost.brokerState -eq 'Subscribed' -and $lost.heartbeatMonitor.elapsedMilliseconds -ge 5000 -and $lost.heartbeatMonitor.elapsedMilliseconds -lt 6000) 'AMR Timeout confused with Broker loss or outside timing tolerance.'
  if($Phase -eq 'NoHeartbeat'){Require ($lost.telemetryCount -gt $before.telemetryCount) 'Telemetry should continue while Heartbeat is suppressed.'}
  Start-Sleep -Milliseconds 1100
  $still=State
  Require ($still.heartbeatMonitor.timeoutCount -eq $before.heartbeatMonitor.timeoutCount+1) 'Duplicate Timeout for one interruption.'
  if($Phase -eq 'NoHeartbeat'){
   Owner
   & $docker exec $owned.ContainerId mosquitto_pub -h 127.0.0.1 -t amr/HB_ONE/heartbeat -m '{"id":"HB_ONE","ts":123,"status":"BAD"}'
   Require ($LASTEXITCODE -eq 0) 'Invalid-heartbeat injection failed.'
   & $docker exec $owned.ContainerId mosquitto_pub -h 127.0.0.1 -t amr/HB_OTHER/heartbeat -m '{"id":"HB_OTHER","ts":123,"status":"OK"}'
   Require ($LASTEXITCODE -eq 0) 'Other-AMR injection failed.'
   Start-Sleep -Milliseconds 600
   $invalid=State
   Require ($invalid.heartbeatMonitor.state -eq 'Unresponsive' -and $invalid.rejectedCount -eq $before.rejectedCount+1 -and $invalid.heartbeatMonitor.recoveryCount -eq $before.heartbeatMonitor.recoveryCount) 'Invalid/other-AMR heartbeat recovered target.'
   Save '05-invalid-other-isolation' $invalid
  }else{Require ($still.telemetryCount -eq $lost.telemetryCount -and $still.heartbeatCount -eq $lost.heartbeatCount) 'AMR silent fixture still received data.'}
  Save "04-$Phase" @{before=$before;firstTimeout=$lost;after=$still;faultToFirstObservedMs=$firstObservedMs;scenarioElapsedMs=$watch.ElapsedMilliseconds;fault=(Invoke-RestMethod "$url/verify/signal")}
 }
 {$_ -in 'Recovery','Recovery2'} {
  $before=State
  Post 'verify/signal' @{mode='all'}|Out-Null
  $recovered=Wait-State {param($s) $s.heartbeatMonitor.state -eq 'Alive'}
  Start-Sleep -Milliseconds 800
  $after=State
  Require ($after.heartbeatMonitor.recoveryCount -eq $before.heartbeatMonitor.recoveryCount+1 -and $after.heartbeatMonitor.timeoutCount -eq $before.heartbeatMonitor.timeoutCount) 'Recovery not exactly once.'
  Save "06-$Phase" @{before=$before;firstRecovery=$recovered;after=$after}
 }
 BrokerDown {
  Owner
  $before=State
  & $docker stop --time 3 $owned.ContainerId|Out-Null
  Require ($LASTEXITCODE -eq 0) 'Owned Broker stop failed.'
  $lost=Wait-State {param($s) $s.brokerState -eq 'Failed' -and $s.heartbeatMonitor.state -eq 'Stopped'}
  Start-Sleep -Milliseconds 5500
  $after=State
  Require (!$after.heartbeatMonitor.monitoring -and $after.heartbeatMonitor.timeoutCount -eq $before.heartbeatMonitor.timeoutCount -and $after.failure -eq 'BrokerDisconnected') 'Broker loss incorrectly generated AMR Timeout.'
  Require ($after.position.row -eq $before.position.row -and $after.position.col -eq $before.position.col -and $after.receivedTelemetry.state -eq $before.receivedTelemetry.state) 'Broker loss rewrote AMR position/work state.'
  Save '07-broker-loss-stopped-monitor' @{before=$before;disconnected=$lost;after=$after}
 }
 Cleanup {
  Owner
  Post 'verify/shutdown'|Out-Null
  $p=Get-Process -Id $owned.ProcessId -ErrorAction SilentlyContinue
  if($p){Require ($p.StartTime.ToUniversalTime().Ticks -eq $owned.ProcessStartTicks -and $p.Path -eq $owned.Exe) 'Probe identity changed.';Require ($p.WaitForExit(10000)) 'Probe did not exit gracefully.';$p.Dispose()}
  & $docker logs --timestamps $owned.ContainerId 2>&1|Set-Content "$root/broker.log"
  & $docker rm -v $owned.ContainerId|Out-Null
  Require ($LASTEXITCODE -eq 0) 'Owned stopped Broker removal failed.'
  $after=@(& $docker ps -a --no-trunc --format '{{json .}}')
  $after|Set-Content "$root/containers-after.jsonl"
  $before=@(Get-Content "$root/containers-before.jsonl"|Where-Object {$_}|ForEach-Object {$_|ConvertFrom-Json})
  $current=@($after|ForEach-Object {$_|ConvertFrom-Json})
  $same=@($before|Where-Object {$b=$_;!@($current|Where-Object {$_.ID -eq $b.ID -and $_.State -eq $b.State}).Count}).Count -eq 0
  Require $same 'An existing container changed state.'
  Save 'cleanup' @{gracefulShutdown=$true;ownedBrokerRemoved=$true;existingContainersPreserved=$same}
 }
}
Write-Output "$Phase passed: $url"
