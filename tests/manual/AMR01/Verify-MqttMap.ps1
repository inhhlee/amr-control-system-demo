param(
 [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,48}$')][string]$SessionRunId,
 [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,48}$')][string]$RunId,
 [switch]$StopBrokerAtEnd
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$root=Join-Path $repo "evidence/AMR/$RunId"
if(Test-Path $root){throw 'Use a new RunId.'}
$null=New-Item -ItemType Directory $root
$session=Get-Content (Join-Path $repo "evidence/AMR/$SessionRunId/session.json") -Raw|ConvertFrom-Json
$url=$session.Url
$docker=Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'
$headers=@{'X-Simulator-Map'='1'}
$samples=[Collections.Generic.List[object]]::new()
$results=[Collections.Generic.List[object]]::new()
function Read-State {
 $s=Invoke-RestMethod "$url/api/state" -TimeoutSec 3
 if($s.source -ne 'mqtt-control-receiver'){throw 'Not a MQTT receiver map'}
 if($s.position){
  $t=$s.receivedTelemetry
  if(!$t -or $s.position.x -ne $t.position.x -or $s.position.y -ne $t.position.y -or $s.speed -ne $t.speed -or $s.amrState -ne $t.state){throw 'Map differs from received telemetry'}
 }elseif($s.receivedTelemetry){throw 'Received telemetry not projected'}
 $samples.Add(@{runId=$s.runId;phase=$s.phase;position=$s.position;speed=$s.speed;state=$s.amrState;telemetry=$s.receivedTelemetry;count=$s.telemetryCount})
 return $s
}
function Start-Run {
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds(2)
 do {
  $r=Invoke-WebRequest "$url/api/start" -Method Post -Headers $headers -SkipHttpErrorCheck
  if([int]$r.StatusCode -eq 202){return}
  if([int]$r.StatusCode -ne 409){throw 'Start failed'}
  Start-Sleep -Milliseconds 50
 }while([DateTimeOffset]::UtcNow -lt $deadline)
 throw 'Previous run has not finished'
}
function Wait-State([string]$Phase,[switch]$Moving){
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds(10)
 do {
  $s=Read-State
  if(($Moving -and $s.phase -eq 'Running' -and $s.amrState -eq 'BUSY') -or (!$Moving -and $s.phase -eq $Phase)){return $s}
  if($s.phase -eq 'Failed' -and $Phase -ne 'Failed'){throw "Execution failed: $($s.receiveFailure)"}
  Start-Sleep -Milliseconds 60
 }while([DateTimeOffset]::UtcNow -lt $deadline)
 throw "Did not observe $Phase"
}
function Inject([string]$Topic,[string]$Payload){
 & $docker exec $session.ContainerId mosquitto_pub -h 127.0.0.1 -p 1883 -t $Topic -m $Payload
 if($LASTEXITCODE -ne 0){throw 'Fixture publication failed'}
}
function Verify-Completed($state,[string]$Name){
 $target=$state.settings.waypoints[-1]
 if($state.position.x -ne $target.x -or $state.position.y -ne $target.y -or $state.speed -ne 0 -or $state.amrState -ne 'IDLE' -or $state.exitCode -ne 0 -or $state.receivedTelemetry.timestamp -ne $state.lastPublishedTimestamp){throw 'Final received state mismatch'}
 $state|ConvertTo-Json -Depth 12|Set-Content "$root/$Name.json"
 $file=Join-Path $state.evidenceDirectory ($state.runId+'.received.jsonl')
 $rows=@(Get-Content $file|ForEach-Object {$_|ConvertFrom-Json}|Where-Object { $_.event -eq 'Received' -and $_.value.kind -eq 'telemetry' })
 foreach($sample in @($samples|Where-Object {$_.runId -eq $state.runId -and $_.telemetry})){
  $t=$sample.telemetry
  if(!@($rows|Where-Object {$_.value.model.timestamp -eq $t.timestamp -and $_.value.model.position.x -eq $t.position.x -and $_.value.model.position.y -eq $t.position.y -and $_.value.model.speed -eq $t.speed -and $_.value.model.state -eq $t.state}).Count){throw 'API point missing from actual received log'}
 }
 $source=[string](Get-Content (Join-Path $state.evidenceDirectory ($state.runId+'.stdout.log')) -Raw)
 foreach($row in $rows){
  $t=$row.value.model
  $snapshots=[regex]::Matches($source,'(?m)^(?:AmrInitialized|SimulationStarted|AmrMoved|SimulationCompleted) AmrId=\S+ X=(\S+) Y=(\S+) State=(\S+) CurrentSpeedMps=(\S+)')
  if(!@($snapshots|Where-Object {[double]::Parse($_.Groups[1].Value,[cultureinfo]::InvariantCulture) -eq $t.position.x -and [double]::Parse($_.Groups[2].Value,[cultureinfo]::InvariantCulture) -eq $t.position.y -and $_.Groups[3].Value -eq $t.state -and [double]::Parse($_.Groups[4].Value,[cultureinfo]::InvariantCulture) -eq $t.speed}).Count){throw 'Received point absent from actual simulator movement'}
 }
 $results.Add(@{Case=$Name;Passed=$true;Run=$state.runId;Telemetry=$state.telemetryCount;Heartbeat=$state.heartbeatCount;Elapsed=$state.elapsedSeconds;ReceivedPointsMatched=$rows.Count})
}
try {
 $label=& $docker inspect $session.ContainerId --format '{{index .Config.Labels "amr.map.run"}}'
 if($LASTEXITCODE -ne 0 -or $label -ne $SessionRunId){throw 'Broker ownership mismatch'}
 $initial=Read-State
 if($initial.phase -in @('Running','Canceling','WaitingForTelemetry')){throw 'User simulation active; finish it first'}
 if($initial.phase -eq 'Ready' -and ($initial.position -or $initial.telemetryCount -ne 0)){throw 'Configured position masquerades as received'}
 Start-Run
 $overlap=Invoke-WebRequest "$url/api/start" -Method Post -Headers $headers -SkipHttpErrorCheck
 if([int]$overlap.StatusCode -ne 409){throw 'Overlapping execution accepted'}
 $moving=Wait-State '' -Moving
 $id=$moving.settings.amrId
 # 시험용 불량 입력과 다른 AMR 입력. 실제 이동 데이터와 별도 취급한다.
 Inject "amr/$id/telemetry" ('{"id":"'+$id+'","ts":100,"pos":{"x":999,"y":999},"sp":1,"state":"INVALID"}')
 Inject 'amr/OTHER_AMR/telemetry' '{"id":"OTHER_AMR","ts":100,"pos":{"x":999,"y":999},"sp":1,"state":"BUSY"}'
 $completed=Wait-State 'Completed'
 Verify-Completed $completed 'arrival'
 if($completed.rejectedCount -ne 1){throw 'Invalid telemetry not rejected'}
 if(@($samples|Where-Object {$_.position.x -eq 999 -or $_.position.y -eq 999}).Count){throw 'Invalid/other AMR moved marker'}
 $results.Add(@{Case='invalid-and-other-amr-isolation';Passed=$true;Rejected=$completed.rejectedCount})
 Start-Run
 $null=Wait-State '' -Moving
 $null=Invoke-WebRequest "$url/api/cancel" -Method Post -Headers $headers
 $canceled=Wait-State 'Canceled'
 $canceled|ConvertTo-Json -Depth 12|Set-Content "$root/canceled.json"
 if($canceled.exitCode -ne 0){throw 'User cancellation exit code'}
 $results.Add(@{Case='cancel';Passed=$true})
 Start-Run
 $restart=Wait-State 'Completed'
 Verify-Completed $restart 'restart'
 if($StopBrokerAtEnd){
  Start-Run
  $null=Wait-State '' -Moving
  & $docker stop --time 3 $session.ContainerId|Out-Null
  if($LASTEXITCODE -ne 0){throw 'Owned Broker stop failed'}
  $failed=Wait-State 'Failed'
  if($failed.exitCode -ne 1 -or !$failed.receivedTelemetry -or $failed.receiveFailure -ne 'BrokerDisconnected'){throw 'Broker failure mislabeled'}
  $failed|ConvertTo-Json -Depth 12|Set-Content "$root/broker-loss.json"
  $results.Add(@{Case='broker-loss-no-false-arrival';Passed=$true;ExitCode=$failed.exitCode})
 }
 @{Passed=$true;Results=$results.ToArray();Samples=$samples.Count;SessionRunId=$SessionRunId;Url=$url;ProductEvidence=$completed.evidenceDirectory;Finished=[DateTimeOffset]::Now.ToString('o')}|ConvertTo-Json -Depth 8|Set-Content "$root/verification.json"
 Write-Output "PASS: MQTT receiver model -> map, actual movement, invalid data isolation, cancel/restart. $root"
}catch{$_|Out-String|Set-Content "$root/failure.log";throw}
finally{$samples|ForEach-Object {$_|ConvertTo-Json -Depth 6 -Compress}|Set-Content "$root/samples.jsonl"}
