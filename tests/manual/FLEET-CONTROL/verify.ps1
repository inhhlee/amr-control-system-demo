param([string]$Docker='C:/Users/Admin/AppData/Local/Programs/DockerDesktop/resources/bin/docker.exe',
 [string]$Evidence='', [string]$Configuration='FleetEngine')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repo
if (!$Evidence) {$Evidence="evidence/AMR/fleet-control-$(Get-Date -Format 'yyyyMMdd-HHmmss')"}
$root=[IO.Path]::GetFullPath($Evidence)
if (Test-Path $root) {throw 'Use a new evidence directory.'}
New-Item -ItemType Directory -Path $root | Out-Null
$results=[Collections.Generic.List[object]]::new()
$container=$null; $sim=$null; $observer=$null; $takeover=$null
$exe="$repo/src/Amr.Simulator/bin/$Configuration/net10.0/Amr.Simulator.exe"
$started=[DateTimeOffset]::Now
function Assert([bool]$ok,[string]$why) {if (!$ok) {throw $why}}
function Pass([string]$id,[string]$detail) {$results.Add(@{id=$id;passed=$true;detail=$detail}); "$id PASS $detail"}
function DockerRun([string[]]$a) {$v=& $Docker @a 2>&1 | Out-String; if ($LASTEXITCODE -ne 0) {throw $v}; $v.Trim()}
function Events {
 if (!(Test-Path "$root/mqtt.log")) {return}
 foreach ($line in Get-Content "$root/mqtt.log") {
  if ($line -match '^(amr/[^ ]+) (\{.*\})$') {
   try {[pscustomobject]@{topic=$Matches[1];payload=($Matches[2]|ConvertFrom-Json)}} catch {}
  }
 }
}
function WaitFor([scriptblock]$p,[string]$why,[int]$seconds=8) {
 $end=[DateTimeOffset]::Now.AddSeconds($seconds)
 do {if (& $p) {return}; Start-Sleep -Milliseconds 40} while ([DateTimeOffset]::Now -lt $end)
 throw "Timeout: $why"
}
function HasAck([string]$d,[string]$id,[string]$result,[string]$reason) {
 @((Events)|Where-Object {$_.topic -eq "amr/$d/ack" -and $_.payload.cmdId -eq $id -and $_.payload.result -eq $result -and $_.payload.reason -eq $reason}).Count -gt 0
}
function Latest([string]$d) {@((Events)|Where-Object {$_.topic -eq "amr/$d/telemetry"})[-1].payload}
function SendCommand([string]$d,[string]$id,[string]$type,$param) {
 $json=@{cmdId=$id;type=$type;ts=[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds();param=$param}|ConvertTo-Json -Compress
 @{topic="amr/$d/command";payload=($json|ConvertFrom-Json)}|ConvertTo-Json -Depth 5 -Compress|Add-Content "$root/commands.jsonl"
 $r=$json|& $Docker exec -i $container mosquitto_pub -h 127.0.0.1 -p 1883 -V mqttv5 -q 0 -t "amr/$d/command" -s 2>&1
 Assert ($LASTEXITCODE -eq 0) "Publish failed: $r"
}
function StopOwned([string]$name,[int]$code) {
 & pwsh -NoProfile -File tests/manual/AMR01/Send-OwnedCtrlC.ps1 -TargetProcessId $sim.Id -StartTicks $sim.StartTime.ToUniversalTime().Ticks -ResultPath "$root/$name-shutdown.json"
 Assert ($LASTEXITCODE -eq 0) 'Owned Ctrl+C failed'
 Assert ($sim.WaitForExit(5000)) 'Process did not exit'; $sim.WaitForExit()
 Assert ($sim.ExitCode -eq $code) "Unexpected exit: $($sim.ExitCode)"
}
try {
 & dotnet build src/Amr.Simulator -c $Configuration --no-restore --nologo -v minimal 2>&1 | Set-Content "$root/build.log"
 Assert ($LASTEXITCODE -eq 0) 'Build failed'
 $deps=Get-Content "$repo/src/Amr.Simulator/bin/$Configuration/net10.0/Amr.Simulator.deps.json" -Raw|ConvertFrom-Json
 $libs=@($deps.libraries.PSObject.Properties.Name)
 $libs|ConvertTo-Json|Set-Content "$root/runtime-dependencies.json"
 Assert (@($libs|Where-Object {$_ -match 'Amr\.(Core|ControlGui|Infrastructure)'}).Count -eq 0) 'Control dependency'
 Pass 'FLEET-01' 'Independent EXE without Core/GUI/Infrastructure dependencies.'
 DockerRun @('ps','--format','{{.Names}} {{.Ports}}')|Set-Content "$root/containers-before.log"
 $container='amr-fleet-check-'+[guid]::NewGuid().ToString('N')
 $brokerConfig="$repo/tests/manual/FAULT-RESET/mosquitto.conf"
 DockerRun @('run','--rm','-d','--pull','never','--name',$container,'-p','127.0.0.1::1883','--mount',"type=bind,source=$brokerConfig,target=/mosquitto/config/mosquitto.conf,readonly",'eclipse-mosquitto:2.1.2-alpine')|Set-Content "$root/container-id.txt"
 $mapping=DockerRun @('port',$container,'1883/tcp')
 Assert ($mapping -match '^127\.0\.0\.1:(\d+)$') 'Port mapping'; $port=[int]$Matches[1]
 DockerRun @('inspect',$container)|Set-Content "$root/broker-inspect.json"
 WaitFor {(DockerRun @('logs',$container)) -match 'mosquitto version .* running'} 'Broker ready'
 $observer=Start-Process $Docker -ArgumentList @('exec',$container,'mosquitto_sub','-h','127.0.0.1','-p','1883','-V','mqttv5','-t','amr/#','-v','-W','90') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$root/mqtt.log" -RedirectStandardError "$root/mqtt.err"
 $null=$observer.Handle
 WaitFor {(DockerRun @('logs',$container)) -match 'Received SUBSCRIBE'} 'Observer ready'
 $settings=@{amrs=@(
 @{amrId='A';mode='command';initialPosition=@{x=4;y=2};speedMps=2;updateIntervalMs=50;waypoints=@(@{x=100;y=2});faults=@(@{code='SHARED_CODE';level='ERR';activateAfterMs=15000;resolveAfterMs=23000;message='fleet audit'})},
 @{amrId='B';mode='command';initialPosition=@{x=0;y=5};speedMps=2;updateIntervalMs=50;waypoints=@(@{x=100;y=5});faults=@()},
 @{amrId='R';mode='route';initialPosition=@{x=0;y=10};speedMps=5;updateIntervalMs=50;waypoints=@(@{x=1;y=10},@{x=1;y=11});faults=@()}
 )}
 $settings|ConvertTo-Json -Depth 8|Set-Content "$root/fleet.json"
 $arguments=@('--fleet-config',('"{0}/fleet.json"' -f $root),'--mqtt','--host','127.0.0.1','--port',"$port",'--telemetry-ms','100','--heartbeat-ms','250')
 $arguments|ConvertTo-Json|Set-Content "$root/simulator-arguments.json"
 $sim=Start-Process $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput "$root/simulator.log" -RedirectStandardError "$root/simulator.err"
 $null=$sim.Handle
 WaitFor {@((Events)|Where-Object {$_.topic -like '*/heartbeat'}|Select-Object -ExpandProperty topic -Unique).Count -eq 3} 'Three devices ready'
 WaitFor {$r=Latest 'R'; $r.state -eq 'IDLE' -and $r.pos.x -eq 1 -and $r.pos.y -eq 11} 'Two route waypoints'
 Assert (@((Events)|Where-Object {$_.topic -eq 'amr/R/ack'}).Count -eq 0) 'Synthetic route ACK'
 Pass 'FLEET-02' 'A/B command wait; R configured route arrival and continued Heartbeat.'
 SendCommand 'A' 'SAME-ID' 'MOVE' @{x=1000;y=2}
 SendCommand 'B' 'SAME-ID' 'MOVE' @{x=1000;y=5}
 WaitFor {(Latest 'A').state -eq 'BUSY' -and (Latest 'B').state -eq 'BUSY'} 'Independent MOVE'
 SendCommand 'B' 'BUSY-RESET' 'RESET' @{}
 WaitFor {HasAck 'B' 'BUSY-RESET' 'FAILURE' 'BUSY_RESET_REJECTED'} 'BUSY RESET'
 SendCommand 'B' 'STOP-1' 'STOP' @{}
 WaitFor {(HasAck 'B' 'STOP-1' 'SUCCESS' 'STOPPED') -and (HasAck 'B' 'SAME-ID' 'FAILURE' 'STOP_INTERRUPTED')} 'B STOP'
 Assert ((Latest 'A').state -eq 'BUSY') 'B STOP affected A'
 SendCommand 'B' 'CONTINUE' 'MOVE' @{x=1000;y=5}
 Pass 'FLEET-03' 'Same cmdId is device-scoped; B STOP/RESET do not stop A.'
 WaitFor {HasAck 'A' 'SAME-ID' 'FAILURE' 'FAULT_INTERRUPTED'} 'A fault at 15s' 20
 WaitFor {(Latest 'A').state -eq 'FAULT'} 'FAULT telemetry'
 $faultPosition=(Latest 'A').pos; Assert ((Latest 'A').sp -eq 0) 'Fault speed'
 $bBefore=Latest 'B'; WaitFor {(Latest 'B').pos.x -gt ($bBefore.pos.x+1)} 'B moving during A fault'
 Assert ((Latest 'B').state -eq 'BUSY') 'A fault stopped B'
 Assert (@((Events)|Where-Object {$_.topic -eq 'amr/B/alarm'}).Count -eq 0) 'Alarm leaked to B'
 Pass 'FLEET-04' 'A FAULT/ACTIVE/failed MOVE; B coordinates and periodic messages continue.'
 SendCommand 'A' 'EARLY' 'RESET' @{}
 WaitFor {HasAck 'A' 'EARLY' 'FAILURE' 'FAULT_CAUSE_ACTIVE'} 'Unresolved RESET'
 WaitFor {(Get-Content "$root/simulator.log" -Raw) -match 'FaultCauseResolved AmrId=A'} 'Cause resolution' 12
 Assert ((Latest 'A').state -eq 'FAULT') 'Auto FAULT clear'
 Assert (@((Events)|Where-Object {$_.topic -like '*/alarm' -and $_.payload.alarmState -eq 'CLEARED'}).Count -eq 0) 'Auto alarm clear'
 SendCommand 'A' 'RECOVER' 'RESET' @{}
 WaitFor {HasAck 'A' 'RECOVER' 'SUCCESS' 'RESET_COMPLETED'} 'RESET recovery'
 $last=Latest 'A'
 Assert ($last.state -eq 'IDLE' -and $last.pos.x -eq $faultPosition.x -and $last.pos.y -eq $faultPosition.y) 'RESET moved position'
 Pass 'FLEET-05' 'Cause resolution stays latched; new RESET clears alarm and returns IDLE at same coordinates.'
 SendCommand 'A' 'AFTER-RESET' 'MOVE' @{x=($faultPosition.x+1);y=2}
 SendCommand 'R' 'AFTER-ROUTE' 'MOVE' @{x=2;y=11}
 WaitFor {(HasAck 'A' 'AFTER-RESET' 'SUCCESS' 'ARRIVED') -and (HasAck 'R' 'AFTER-ROUTE' 'SUCCESS' 'ARRIVED')} 'MOVE after recovery/route'
 SendCommand 'B' 'STOP-2' 'STOP' @{}
 WaitFor {(HasAck 'B' 'CONTINUE' 'FAILURE' 'STOP_INTERRUPTED') -and (HasAck 'B' 'STOP-2' 'SUCCESS' 'STOPPED')} 'Final B STOP'
 $events=@(Events)
 foreach ($event in $events|Where-Object {$_.topic -notlike '*/command'}) {Assert ($event.payload.id -ceq $event.topic.Split('/')[1]) 'Topic/payload ID mismatch'}
 $acks=@($events|Where-Object {$_.topic -like '*/ack'})
 Assert ($acks.Count -eq 10) "Expected 10 ACKs, got $($acks.Count)"
 $acks|ConvertTo-Json -Depth 6|Set-Content "$root/ack-mapping.json"
 Assert (@($events|Where-Object {$_.topic -like '*/alarm'}).Count -eq 2) 'ACTIVE/CLEARED pair'
 Pass 'FLEET-06' '10 target+cmdId final results; correct topic/payload, coordinates and alarm pair.'
 StopOwned 'normal' 0
 Assert ((Get-Item "$root/simulator.err").Length -eq 0) 'Unexpected stderr'
 Assert (([regex]::Matches((Get-Content "$root/simulator.log" -Raw),'CommandSessionEnded ExitCode=0')).Count -eq 3) 'Missing device join'
 Pass 'FLEET-07' 'Ctrl+C joins all three devices; exit 0 without stderr.'
 $sim=Start-Process $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput "$root/disconnect.log" -RedirectStandardError "$root/disconnect.err"
 $null=$sim.Handle
 WaitFor {(Get-Content "$root/disconnect.log" -Raw) -match 'CommandSessionReady AmrId=R'} 'Second process ready'
 $brokerText=DockerRun @('logs',$container)
 $rMatches=[regex]::Matches($brokerText,'Received SUBSCRIBE from ([^\r\n]+)\r?\n[^\r\n]*amr/R/command')
 Assert ($rMatches.Count -ge 1) 'Cannot identify owned R client'
 $clientId=$rMatches[$rMatches.Count-1].Groups[1].Value.Trim()
 $takeover=Start-Process $Docker -ArgumentList @('exec',$container,'mosquitto_sub','-h','127.0.0.1','-p','1883','-V','mqttv5','-i',$clientId,'-t','audit/hold','-W','5') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$root/takeover.log" -RedirectStandardError "$root/takeover.err"
 WaitFor {(Get-Content "$root/disconnect.log" -Raw) -match 'CommandSessionEnded ExitCode=1 AmrId=R'} 'R disconnected'
 Assert (!$sim.HasExited) 'One disconnection stopped all'
 SendCommand 'B' 'AFTER-R-DISCONNECT' 'MOVE' @{x=1;y=5}
 WaitFor {HasAck 'B' 'AFTER-R-DISCONNECT' 'SUCCESS' 'ARRIVED'} 'B after R failure'
 StopOwned 'partial-failure' 1
 Pass 'FLEET-08' 'R connection terminates alone; B completes MOVE; global exit aggregates failure 1.'
} catch {
 $results.Add(@{id='FLEET-ERROR';passed=$false;detail=$_.ToString()})
 $_|Out-String|Set-Content "$root/failure.txt"; "FLEET FAILED: $_"
} finally {
 if ($sim -and !$sim.HasExited) {$sim.Kill(); $sim.WaitForExit()}
 if ($container) {& $Docker logs $container 2>&1|Set-Content "$root/broker.log"; & $Docker stop -t 2 $container|Out-Null}
 foreach ($owned in @($observer,$takeover)) {if ($owned -and !$owned.HasExited) {if (!$owned.WaitForExit(3000)) {$owned.Kill()}}}
 & $Docker ps --format '{{.Names}} {{.Ports}}'|Set-Content "$root/containers-after.log"
 @{started=$started.ToString('o');ended=[DateTimeOffset]::Now.ToString('o');head=(git rev-parse HEAD);branch=(git branch --show-current);sdk=(dotnet --version);port=$port;results=$results.ToArray()}|ConvertTo-Json -Depth 10|Set-Content "$root/results.json"
 Get-ChildItem src/Amr.Simulator,src/Amr.Communication -Recurse -File|Where-Object {$_.Extension -in '.cs','.csproj' -and $_.FullName -notmatch '\\(bin|obj)\\'}|Get-FileHash -Algorithm SHA256|Select-Object Path,Hash|ConvertTo-Json|Set-Content "$root/source-hashes.json"
}
if (@($results|Where-Object {!$_.passed}).Count) {exit 1}
