param(
 [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,48}$')][string]$RunId,
 [string]$DockerPath
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location $repo
$root=Join-Path $repo "evidence/AMR/$RunId"
if(Test-Path $root){throw 'Use a new RunId; previous results are preserved.'}
$null=New-Item -ItemType Directory $root
if ([string]::IsNullOrWhiteSpace($DockerPath)) {
 $DockerPath=@(Get-Command docker -CommandType Application -ErrorAction Stop)[0].Source
}
$docker=(Resolve-Path -LiteralPath $DockerPath -ErrorAction Stop).Path
$env:AMR_SEQ_URL=$null; $env:AMR_SEQ_API_KEY=$null
$processes=[Collections.Generic.List[object]]::new()
$results=[Collections.Generic.List[object]]::new()
$container=$null; $passed=$false
$started=[DateTimeOffset]::UtcNow
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MqttDemoLaunch { [DllImport("kernel32.dll")] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add); }
'@
function Docker([string[]]$Arguments) {
 $text=& $docker @Arguments 2>&1 | Out-String
 if($LASTEXITCODE -ne 0){throw "Docker failed: $text"}
 return $text.Trim()
}
function Start-Owned([string]$Project,[string]$Name,[string[]]$Arguments) {
 $null=[MqttDemoLaunch]::SetConsoleCtrlHandler([IntPtr]::Zero,$false)
 $exe=Join-Path $repo "src/$Project/bin/Debug/net10.0/$Project.exe"
 $p=Start-Process $exe -ArgumentList $Arguments -WorkingDirectory $repo -WindowStyle Hidden -RedirectStandardOutput "$root/$Name.stdout.log" -RedirectStandardError "$root/$Name.stderr.log" -PassThru
 $null=$p.Handle
 $processes.Add(@{Process=$p;Name=$Name;Ticks=$p.StartTime.ToUniversalTime().Ticks})
 return $p
}
function Wait-Log([string]$Name,[string]$Pattern,[int]$Seconds=10) {
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds($Seconds)
 do {
  $text=[string](Get-Content "$root/$Name.stdout.log" -Raw)
  if($text -match $Pattern){return}
  Start-Sleep -Milliseconds 30
 } while([DateTimeOffset]::UtcNow -lt $deadline)
 throw "Expected log not observed: $Name / $Pattern"
}
function Wait-Exit($Process,[int]$Expected,[string]$Name) {
 if(!$Process.WaitForExit(10000)){throw "$Name did not exit."}
 $Process.WaitForExit()
 if($Process.ExitCode -ne $Expected){throw "$Name exit=$($Process.ExitCode), expected=$Expected"}
}
function Simulator-Args([string]$Config) { return @('--config',"`"$Config`"",'--mqtt','--host','127.0.0.1','--port',"$script:port",'--telemetry-ms','200','--heartbeat-ms','1000') }
function Read-Received {
 return @(Get-Content "$root/receiver.stdout.log" | ForEach-Object {if($_ -match ' Received (\{.*\})$'){ $Matches[1] | ConvertFrom-Json }})
}
function Verify-Route([string]$Name,[string]$Config) {
 $settings=Get-Content $Config -Raw | ConvertFrom-Json
 $beforeCount=@(Read-Received).Count
 $p=Start-Owned 'Amr.Simulator' $Name (Simulator-Args $Config)
 Wait-Exit $p 0 $Name
 $log=[string](Get-Content "$root/$Name.stdout.log" -Raw)
 if($log -notmatch 'MqttSimulationCompleted' -or (Get-Item "$root/$Name.stderr.log").Length -ne 0){throw "$Name did not complete cleanly"}
 $published=@(Get-Content "$root/$Name.stdout.log" | ForEach-Object {if($_ -match '^(Telemetry|Heartbeat)Published Topic=\S+ Payload=(\{.*\}) Delivery=Unconfirmed$'){@{Kind=$Matches[1].ToLowerInvariant();Payload=($Matches[2]|ConvertFrom-Json)}}})
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds(3)
 do { $all=@(Read-Received); if($all.Count -ge $beforeCount+$published.Count){break}; Start-Sleep -Milliseconds 30 } while([DateTimeOffset]::UtcNow -lt $deadline)
 $received=@($all | Select-Object -Skip $beforeCount)
 if($received.Count -ne $published.Count){throw "$Name sent/received count differs"}
 foreach($pub in $published) {
  $matchesReceived=@($received | Where-Object {$_.Kind -eq $pub.Kind -and $_.Model.Timestamp -eq $pub.Payload.ts -and $_.AmrId -eq $pub.Payload.id})
  $ok=@($matchesReceived | Where-Object {if($pub.Kind -eq 'heartbeat'){$_.Model.Status -eq 'OK'}else{$_.Model.Position.X -eq $pub.Payload.pos.x -and $_.Model.Position.Y -eq $pub.Payload.pos.y -and $_.Model.Speed -eq $pub.Payload.sp -and $_.Model.State -eq $pub.Payload.state}})
  if(!$ok.Count){throw "$Name payload differs from validated receiver model"}
 }
 $points=@($received | Where-Object Kind -eq 'telemetry')
 $target=$settings.waypoints[-1]
 if($points[0].Model.Position.X -ne $settings.initialPosition.x -or $points[0].Model.Position.Y -ne $settings.initialPosition.y -or $points[0].Model.Speed -ne 0 -or $points[0].Model.State -ne 'IDLE'){throw 'Initial state mismatch'}
 $last=$points[-1].Model
 if($last.Position.X -ne $target.x -or $last.Position.Y -ne $target.y -or $last.Speed -ne 0 -or $last.State -ne 'IDLE'){throw 'Arrival state mismatch'}
 if($Name -ne 'already-arrived' -and !@($points | Where-Object {$_.Model.State -eq 'BUSY' -and $_.Model.Speed -eq $settings.speedMps}).Count){throw 'Moving state not received'}
 # 송신 로그와 실제 이동 루프가 출력한 좌표·상태를 대조한다.
 $snapshots=@([regex]::Matches($log,'(?m)^(?:AmrInitialized|SimulationStarted|AmrMoved|SimulationCompleted) AmrId=\S+ X=(\S+) Y=(\S+) State=(\S+) CurrentSpeedMps=(\S+)') | ForEach-Object { @{X=[double]::Parse($_.Groups[1].Value,[cultureinfo]::InvariantCulture);Y=[double]::Parse($_.Groups[2].Value,[cultureinfo]::InvariantCulture);State=$_.Groups[3].Value;Speed=[double]::Parse($_.Groups[4].Value,[cultureinfo]::InvariantCulture)} })
 foreach($point in $points){$m=$point.Model; if(!@($snapshots | Where-Object {$_.X -eq $m.Position.X -and $_.Y -eq $m.Position.Y -and $_.Speed -eq $m.Speed -and $_.State -eq $m.State}).Count){throw 'Received position absent from actual movement snapshots'}}
 $received | ConvertTo-Json -Depth 8 | Set-Content "$root/$Name.received.json"
 $results.Add(@{Case=$Name;Passed=$true;ExitCode=$p.ExitCode;Telemetry=$points.Count;Heartbeat=@($received|Where-Object Kind -eq 'heartbeat').Count;Final=$last;AllPayloadsMatched=$true;AllCoordinatesMatched=$true})
}
try {
 Docker @('ps','-a','--no-trunc','--format','{{json .}}') | Set-Content "$root/containers-before.jsonl"
 $imageId=Docker @('image','inspect','eclipse-mosquitto:2.1.2-alpine','--format','{{.Id}}')
 "listener 1883 0.0.0.0`nallow_anonymous true`npersistence false`nlog_dest stdout`nlog_type all`nconnection_messages true" | Set-Content "$root/broker.conf" -Encoding ascii
 $container=Docker @('run','--detach','--pull','never','--name',"amr-mqtt-$RunId",'--label',"amr.mqtt.run=$RunId",'--publish','127.0.0.1::1883','--tmpfs','/mosquitto/data','--tmpfs','/mosquitto/log','--mount',"type=bind,source=$root/broker.conf,target=/mosquitto/config/mosquitto.conf,readonly",'eclipse-mosquitto:2.1.2-alpine')
 if($container -notmatch '^[a-f0-9]{64}$'){throw 'Container ID not confirmed'}
 $mapping=Docker @('port',$container,'1883/tcp')
 if($mapping -notmatch '^127\.0\.0\.1:(\d+)$'){throw 'Not a loopback-only Broker'}
 $script:port=[int]$Matches[1]
 $ready=$false
 for($attempt=0;$attempt -lt 30;$attempt++){
  if((Docker @('logs',$container)) -match 'mosquitto version .* running'){$ready=$true;break}
  Start-Sleep -Milliseconds 100
 }
 if(!$ready){throw 'Broker readiness timeout'}
 @{RunId=$RunId;ContainerId=$container;Port=$port;ImageId=$imageId;Started=$started.ToString('o');Head=(git rev-parse HEAD);Branch=(git branch --show-current);Sdk=(dotnet --version)} | ConvertTo-Json | Set-Content "$root/environment.json"
 $receiver=Start-Owned 'Amr.Communication.Console' 'receiver' @('--host','127.0.0.1','--port',"$port",'--show-payload')
 Wait-Log 'receiver' 'WaitingForShutdown'
 Copy-Item config/simulator.single.json "$root/default.json"
 Verify-Route 'default' "$root/default.json"
 $changed=Get-Content config/simulator.single.json -Raw | ConvertFrom-Json
 $changed.initialPosition.x=5; $changed.initialPosition.y=3; $changed.speedMps=2; $changed.waypoints=@(@{x=5;y=4},@{x=7;y=4})
 $changed | ConvertTo-Json -Depth 6 | Set-Content "$root/changed.json"
 Verify-Route 'changed' "$root/changed.json"
 $changed.waypoints=@(@{x=5;y=3})
 $changed | ConvertTo-Json -Depth 6 | Set-Content "$root/already-arrived.json"
 Verify-Route 'already-arrived' "$root/already-arrived.json"
 $retained=& $docker exec $container mosquitto_sub -h 127.0.0.1 -p 1883 -t 'amr/+/telemetry' -t 'amr/+/heartbeat' -C 1 -W 1 2> "$root/retained.stderr.log"
 $retainedCode=$LASTEXITCODE
 if($retainedCode -ne 27 -or $retained){throw 'Unexpected retained message or subscriber error'}
 $results.Add(@{Case='no-retained-message';Passed=$true;SubscriberExitCode=$retainedCode})
 $long=Get-Content config/simulator.single.json -Raw | ConvertFrom-Json
 $long.updateIntervalMs=[long]::MaxValue
 $long | ConvertTo-Json -Depth 6 | Set-Content "$root/long-interval.json"
 $cancel=Start-Owned 'Amr.Simulator' 'cancel' (Simulator-Args "$root/long-interval.json")
 Wait-Log 'cancel' 'HeartbeatPublished.*\r?\n(?:.|\r?\n)*HeartbeatPublished'
 & pwsh -NoProfile -File "$PSScriptRoot/Send-OwnedCtrlC.ps1" -TargetProcessId $cancel.Id -StartTicks $cancel.StartTime.ToUniversalTime().Ticks -ResultPath "$root/cancel-signal.json"
 if($LASTEXITCODE -ne 0){throw 'Cancellation controller failed'}
 Wait-Exit $cancel 0 'cancel'
 $cancelLog=[string](Get-Content "$root/cancel.stdout.log" -Raw)
 if($cancelLog -notmatch 'MqttSimulationCanceled Reason=UserCanceled' -or $cancelLog -match 'SimulationCompleted'){throw 'Cancellation mislabeled'}
 $results.Add(@{Case='long-update-independent-sending-and-cancel';Passed=$true;ExitCode=0;Heartbeats=([regex]::Matches($cancelLog,'HeartbeatPublished')).Count})
 $lost=Start-Owned 'Amr.Simulator' 'broker-loss' (Simulator-Args "$root/default.json")
 Wait-Log 'broker-loss' 'AmrMoved'
 $null=Docker @('stop','--time','3',$container)
 Wait-Exit $lost 1 'broker-loss'
 Wait-Exit $receiver 1 'receiver-broker-loss'
 if((Get-Content "$root/broker-loss.stderr.log" -Raw) -notmatch 'Reason=ConnectionLost'){throw 'Broker loss reason missing'}
 if((Get-Content "$root/receiver.stdout.log" -Raw) -match 'Validation=Rejected|QueueAdmission Result=(?!Accepted)'){throw 'Receiver rejected data'}
 $results.Add(@{Case='broker-loss';Passed=$true;SimulatorExit=1;ReceiverExit=1})
 $failed=Start-Owned 'Amr.Simulator' 'connect-failure' (Simulator-Args "$root/default.json")
 Wait-Exit $failed 1 'connect-failure'
 if((Get-Content "$root/connect-failure.stdout.log" -Raw) -match 'SimulationStarted'){throw 'Movement started without connection'}
 $results.Add(@{Case='connect-failure-before-movement';Passed=$true;ExitCode=1})
 $passed=$true
} catch { $_ | Out-String | Set-Content "$root/failure.log"; throw }
finally {
 foreach($entry in $processes){$p=$entry.Process; if(!$p.HasExited){$p.Kill();$p.WaitForExit()}; $p.Dispose()}
 if($container -match '^[a-f0-9]{64}$'){
  $label=Docker @('inspect',$container,'--format','{{index .Config.Labels "amr.mqtt.run"}}')
  if($label -ne $RunId){throw 'Broker ownership mismatch; cleanup refused'}
  Docker @('logs','--timestamps',$container) | Set-Content "$root/broker.log"
  $null=Docker @('rm','-f','-v',$container)
 }
 $after=Docker @('ps','-a','--no-trunc','--format','{{json .}}')
 $after | Set-Content "$root/containers-after.jsonl"
 $before=@(Get-Content "$root/containers-before.jsonl" | Where-Object {$_} | ForEach-Object {$_|ConvertFrom-Json})
 $afterObjects=@($after -split '\r?\n' | Where-Object {$_} | ForEach-Object {$_|ConvertFrom-Json})
 $preserved=@($before | Where-Object { $prior=$_; !@($afterObjects|Where-Object {$_.ID -eq $prior.ID -and $_.State -eq $prior.State}).Count }).Count -eq 0
 if(!$preserved){$passed=$false}
 @{Passed=$passed;Results=$results.ToArray();ExistingContainersPreserved=$preserved;OwnedBrokerRemoved=($container -notin $afterObjects.ID);OwnedProcessesExited=$true;Finished=[DateTimeOffset]::UtcNow.ToString('o')} | ConvertTo-Json -Depth 10 | Set-Content "$root/verification.json"
}
if(!$passed){throw 'Verification or cleanup failed'}
Write-Output "PASS: actual Simulator -> isolated Broker -> validated control receiver. Evidence: $root"
