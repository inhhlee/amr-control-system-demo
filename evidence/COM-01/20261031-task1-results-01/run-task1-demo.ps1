param([string]$RunId='20261031-task1-results-01')
$ErrorActionPreference='Stop'
$repo='C:\amr-control-system-demo'
$root=Join-Path $repo "evidence/COM-01/$RunId"
$exe=Join-Path $repo 'src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.exe'
$dockerExe=(Get-Command docker -CommandType Application | Select-Object -First 1).Source
$pwshExe=(Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
$env:AMR_SEQ_URL=$null
$env:AMR_SEQ_API_KEY=$null
$env:AMR_LOG_RUN_ID=$null
Set-Location -LiteralPath $repo
if(!(Test-Path -LiteralPath "$root/build.log") -or (Test-Path -LiteralPath "$root/verification.json")){throw 'Build first; never overwrite an execution.'}
$started=[DateTimeOffset]::Now
$checks=[Collections.Generic.List[object]]::new()
$children=[Collections.Generic.List[object]]::new()
$inputs=[Collections.Generic.List[object]]::new()
$container=$null
$subscriber=$null
$failure=$null
$cleanupErrors=[Collections.Generic.List[string]]::new()

function Native([string]$File,[string[]]$Arguments,[int]$TimeoutMs=20000){
 $info=[Diagnostics.ProcessStartInfo]::new($File)
 $info.UseShellExecute=$false;$info.CreateNoWindow=$true
 $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
 foreach($arg in $Arguments){$info.ArgumentList.Add($arg)}
 $p=[Diagnostics.Process]::new();$p.StartInfo=$info
 try{
  if(!$p.Start()){throw 'Process start failed.'}
  $outTask=$p.StandardOutput.ReadToEndAsync();$errTask=$p.StandardError.ReadToEndAsync()
  if(!$p.WaitForExit($TimeoutMs)){$p.Kill($true);$p.WaitForExit();throw 'Test command timed out.'}
  $out=$outTask.GetAwaiter().GetResult();$err=$errTask.GetAwaiter().GetResult()
  @{file=$File;arguments=$Arguments;exitCode=$p.ExitCode;at=[DateTimeOffset]::Now.ToString('O')}|ConvertTo-Json -Compress|Add-Content "$root/commands.jsonl" -Encoding utf8
  return [pscustomobject]@{Code=$p.ExitCode;Out=$out;Err=$err}
 }finally{$p.Dispose()}
}
function Docker([string[]]$Arguments){$r=Native $dockerExe $Arguments;if($r.Code -ne 0){throw "Docker command failed: $($r.Err) $($r.Out)"};$r.Out.Trim()}
function Check([string]$Name,[bool]$Passed,[string]$Detail){
 $checks.Add([ordered]@{name=$Name;passed=$Passed;detail=$Detail})
 if(!$Passed){throw "Check failed: $Name ($Detail)"}
 Write-Output "PASS $Name"
}
function Owner{
 if($container -notmatch '^[a-f0-9]{64}$'){throw 'Missing owned Broker identity.'}
 if((Docker @('inspect',$container,'--format','{{index .Config.Labels "amr.task1.demo"}}')) -ne $RunId){throw 'Owned Broker label changed.'}
}
function Start-App([string]$Name,[string[]]$Arguments){
 $p=Start-Process -FilePath $exe -ArgumentList $Arguments -WorkingDirectory $repo -WindowStyle Hidden -RedirectStandardOutput "$root/$Name.stdout.log" -RedirectStandardError "$root/$Name.stderr.log" -PassThru
 $null=$p.Handle
 $entry=[pscustomobject]@{Name=$Name;Process=$p;Ticks=$p.StartTime.ToUniversalTime().Ticks}
 $children.Add($entry)
 return $entry
}
function Wait-Log($Entry,[string]$Pattern,[int]$Seconds=12){
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds($Seconds)
 do{
  $text=[string](Get-Content -LiteralPath "$root/$($Entry.Name).stdout.log" -Raw)
  if($text -match $Pattern){return $text}
  if($Entry.Process.HasExited){throw "Application exited before $Pattern"}
  if([DateTimeOffset]::UtcNow -gt $deadline){throw "Missing expected output: $Pattern"}
  Start-Sleep -Milliseconds 40
 }while($true)
}
function Publish([string]$Name,[string]$Id,[string]$Kind,$Payload){
 $json=$Payload|ConvertTo-Json -Depth 8 -Compress
 $inputs.Add(@{name=$Name;topic="amr/$Id/$Kind";payload=$Payload})
 $inputs|ConvertTo-Json -Depth 10|Set-Content "$root/inputs.json" -Encoding utf8
 $null=Docker @('exec',$container,'mosquitto_pub','-h','127.0.0.1','-t',"amr/$Id/$Kind",'-m',$json,'-q','0')
}

try{
 $before=Docker @('ps','-a','--no-trunc','--format','{{json .}}')
 $before|Set-Content "$root/containers-before.jsonl" -Encoding utf8
 $server=Docker @('version','--format','{{json .Server}}')
 $server|Set-Content "$root/docker-server.json" -Encoding utf8
 $null=Docker @('image','inspect','eclipse-mosquitto:2.1.2-alpine','--format','{{.Id}}')
 @('listener 1883 0.0.0.0','allow_anonymous true','persistence false','log_dest stdout','log_type all')|Set-Content "$root/broker.conf" -Encoding ascii
 $container=Docker @('run','--detach','--pull','never','--name',"amr-task1-$RunId",'--label',"amr.task1.demo=$RunId",'--publish','127.0.0.1::1883','--tmpfs','/mosquitto/data','--tmpfs','/mosquitto/log','--mount',"type=bind,source=$root/broker.conf,target=/mosquitto/config/mosquitto.conf,readonly",'eclipse-mosquitto:2.1.2-alpine')
 Owner
 $mapping=Docker @('port',$container,'1883/tcp')
 if($mapping -notmatch '^127\.0\.0\.1:(\d+)$'){throw 'Expected loopback port.'}
 $port=[int]$Matches[1]
 @{containerId=$container;port=$port;runId=$RunId;started=$started.ToString('O')}|ConvertTo-Json|Set-Content "$root/session.json" -Encoding utf8
 $receiver=Start-App 'receiver' @('--host','127.0.0.1','--port',"$port",'--show-payload')
 $monitor=Start-App 'monitor' @('--monitor-amr','DEMO_AMR_001','--host','127.0.0.1','--port',"$port")
 $null=Wait-Log $receiver 'WaitingForShutdown'
 $null=Wait-Log $monitor 'WaitingForShutdown'
 Check 'server-connect-and-subscribe' $true 'Existing console connected to a separate real Mosquitto Broker and subscribed.'

 Publish 'heartbeat-1' 'DEMO_AMR_001' 'heartbeat' @{id='DEMO_AMR_001';ts=100;status='OK'}
 $null=Wait-Log $monitor 'HeartbeatMonitoringStarted'
 Publish 'heartbeat-2' 'DEMO_AMR_002' 'heartbeat' @{id='DEMO_AMR_002';ts=100;status='OK'}
 Publish 'telemetry-1' 'DEMO_AMR_001' 'telemetry' @{id='DEMO_AMR_001';ts=101;pos=@{x=1.25;y=2.5};sp=0.5;state='BUSY'}
 Publish 'telemetry-2' 'DEMO_AMR_002' 'telemetry' @{id='DEMO_AMR_002';ts=101;pos=@{x=3;y=4};sp=0;state='IDLE'}
 Publish 'alarm-input' 'DEMO_AMR_001' 'alarm' @{id='DEMO_AMR_001';ts=102;code='DEMO_SENSOR';level='WARN';alarmState='ACTIVE';message='시험용 메시지: 실제 AMR 장애가 아님'}
 Publish 'ack-input' 'DEMO_AMR_001' 'ack' @{cmdId='DEMO_RESPONSE_001';result='SUCCESS';ts=103}
 Publish 'invalid-missing-timestamp' 'DEMO_AMR_001' 'heartbeat' @{id='DEMO_AMR_001';status='OK'}
 $null=Wait-Log $receiver 'Rejected.*Code=MissingField'
 $text=Wait-Log $receiver 'DEMO_RESPONSE_001'
 $received=@($text -split '\r?\n'|Where-Object {$_ -match ' Received '}|ForEach-Object {($_ -split ' Received ',2)[1]|ConvertFrom-Json})
 foreach($kind in @('heartbeat','telemetry','alarm','ack')){Check "receive-$kind" (@($received|Where-Object {$_.Kind -eq $kind}).Count -gt 0) 'Validated and delivered as a normal model.'}
 Check 'two-device-routing' (@($received|Where-Object {$_.AmrId -eq 'DEMO_AMR_002' -and $_.Kind -eq 'telemetry'}).Count -eq 1) 'Second device remains separately identified.'
 Check 'invalid-message-rejected' ($text -match 'Rejected.*Code=MissingField') 'Missing timestamp rejected; no normal delivery for that input.'

 $info=[Diagnostics.ProcessStartInfo]::new($dockerExe)
 $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
 foreach($arg in @('exec','-t',$container,'mosquitto_sub','-h','127.0.0.1','-t','amr/DEMO_AMR_001/command','-C','3','-W','30','-F','%t %p','-d')){$info.ArgumentList.Add($arg)}
 $subscriber=[Diagnostics.Process]::new();$subscriber.StartInfo=$info
 if(!$subscriber.Start()){throw 'Unable to start command observer.'}
 $subError=$subscriber.StandardError.ReadToEndAsync()
 $prefix=[Collections.Generic.List[string]]::new()
 do{
  $line=$subscriber.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(10)).GetAwaiter().GetResult()
  if($null -eq $line){throw 'Command observer exited before SUBACK.'}
  $prefix.Add($line)
 }until($line -match 'SUBACK')
 $requests=[Collections.Generic.List[object]]::new()
 foreach($kind in @('MOVE','STOP','RESET')){
  $arguments=@('--command',$kind,'--amr-id','DEMO_AMR_001','--host','127.0.0.1','--port',"$port")
  if($kind -eq 'MOVE'){$arguments+=@('--x','10','--y','20')}
  $r=Native $exe $arguments
  $r.Out|Set-Content "$root/command-$kind.stdout.log" -Encoding utf8
  $r.Err|Set-Content "$root/command-$kind.stderr.log" -Encoding utf8
  if($r.Code -ne 0 -or $r.Out -notmatch 'CommandPublished' -or $r.Out -notmatch 'CommandId="([^"]+)"'){throw "Command publication failed: $kind"}
  $requests.Add(@{type=$kind;cmdId=$Matches[1];amrId='DEMO_AMR_001'})
 }
 $tail=$subscriber.StandardOutput.ReadToEndAsync()
 if(!$subscriber.WaitForExit(12000)){throw 'Command observer did not receive three commands.'}
 $wire=($prefix -join [Environment]::NewLine)+[Environment]::NewLine+$tail.GetAwaiter().GetResult()
 $wire|Set-Content "$root/command-observer.stdout.log" -Encoding utf8
 $subError.GetAwaiter().GetResult()|Set-Content "$root/command-observer.stderr.log" -Encoding utf8
 $messages=@($wire -split '\r?\n'|Where-Object {$_ -match '^amr/DEMO_AMR_001/command \{'}|ForEach-Object {($_ -split ' ',2)[1]|ConvertFrom-Json})
 foreach($request in $requests){Check "command-$($request.type)-received" (@($messages|Where-Object {$_.cmdId -eq $request.cmdId -and $_.type -eq $request.type}).Count -eq 1) 'Real Broker observer received the requested target, type and request ID; AMR execution was not tested.'}
 $requests|ConvertTo-Json -Depth 5|Set-Content "$root/command-requests.json" -Encoding utf8

 $null=Wait-Log $monitor 'HeartbeatTimedOut'
 Check 'heartbeat-timeout' $true 'DEMO_AMR_001 produced no further valid Heartbeat; timeout detected.'
 Publish 'heartbeat-recovery' 'DEMO_AMR_001' 'heartbeat' @{id='DEMO_AMR_001';ts=200;status='OK'}
 $null=Wait-Log $monitor 'HeartbeatRecovered'
 Check 'heartbeat-recovery' $true 'New valid Heartbeat restored the liveness observation without reconnecting the Broker.'
 Owner
 $null=Docker @('stop','--time','3',$container)
 foreach($entry in @($receiver,$monitor)){
  $ended=$entry.Process.WaitForExit(8000);$entry.Process.Refresh()
  Check "$($entry.Name)-broker-loss" ($ended -and $entry.Process.ExitCode -eq 1) 'Broker stopped; application detected loss and exited with code 1.'
 }
 $null=Docker @('start',$container)
 $mapping=Docker @('port',$container,'1883/tcp')
 if($mapping -notmatch '^127\.0\.0\.1:(\d+)$'){throw 'Unexpected restarted Broker port.'}
 $fresh=Start-App 'fresh' @('--host','127.0.0.1','--port',$Matches[1],'--show-payload')
 $null=Wait-Log $fresh 'WaitingForShutdown'
 Publish 'fresh-session-heartbeat' 'DEMO_AMR_001' 'heartbeat' @{id='DEMO_AMR_001';ts=300;status='OK'}
 $null=Wait-Log $fresh ' Received '
 Check 'manual-new-session-after-broker-restart' $true 'Explicitly launched a new console process after Broker restart. This is not automatic reconnection.'
 $ctrl=Native $pwshExe @('-NoProfile','-File',"$repo/tests/manual/AMR01/Send-OwnedCtrlC.ps1",'-TargetProcessId',"$($fresh.Process.Id)",'-StartTicks',"$($fresh.Ticks)",'-ResultPath',"$root/normal-stop.json")
 $ctrl.Out|Set-Content "$root/normal-stop.stdout.log" -Encoding utf8
 $ctrl.Err|Set-Content "$root/normal-stop.stderr.log" -Encoding utf8
 $ended=$fresh.Process.WaitForExit(8000);$fresh.Process.Refresh()
 Check 'normal-shutdown' ($ctrl.Code -eq 0 -and $ended -and $fresh.Process.ExitCode -eq 0) 'Owned isolated console received CTRL_C_EVENT and completed normal cleanup.'
}catch{
 $failure=$_
 $_|Out-String|Set-Content "$root/failure.log" -Encoding utf8
}finally{
 foreach($entry in $children){
  try{if(!$entry.Process.HasExited){if($entry.Process.Path -ne $exe -or $entry.Process.StartTime.ToUniversalTime().Ticks -ne $entry.Ticks){throw 'Process identity changed.'};$entry.Process.Kill();$entry.Process.WaitForExit()};$entry.Process.Dispose()}catch{$cleanupErrors.Add($_.Exception.Message)}
 }
 if($subscriber){try{if(!$subscriber.HasExited){$subscriber.Kill($true);$subscriber.WaitForExit()};$subscriber.Dispose()}catch{$cleanupErrors.Add($_.Exception.Message)}}
 if($container){try{Owner;Docker @('logs','--timestamps',$container)|Set-Content "$root/broker.log" -Encoding utf8;$null=Docker @('rm','-f','-v',$container)}catch{$cleanupErrors.Add($_.Exception.Message)}}
 try{
  $after=Docker @('ps','-a','--no-trunc','--format','{{json .}}')
  $after|Set-Content "$root/containers-after.jsonl" -Encoding utf8
  $old=@($before -split '\r?\n'|Where-Object {$_}|ForEach-Object {$_|ConvertFrom-Json})
  $new=@($after -split '\r?\n'|Where-Object {$_}|ForEach-Object {$_|ConvertFrom-Json})
  $preserved=@($old|Where-Object {$b=$_;@($new|Where-Object {$_.ID -eq $b.ID -and $_.State -eq $b.State}).Count -ne 1}).Count -eq 0
  $checks.Add(@{name='existing-containers-preserved';passed=$preserved;detail='Compared existing container IDs and states before and after the demo.'})
 }catch{$cleanupErrors.Add($_.Exception.Message)}
 $sources=@('src/Amr.Communication.Console/CommandConsoleRunner.cs','src/Amr.Communication.Console/ConsoleRunner.cs','src/Amr.Communication.Console/HeartbeatConsoleRunner.cs','src/Amr.Communication/Mqtt/Sessions/ControlCommunicationSession.cs','src/Amr.Communication/Mqtt/Subscriptions/MessageSubscription.cs','src/Amr.Communication/Command/CommandPublisher.cs','src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.dll','src/Amr.Communication/bin/Debug/net10.0/Amr.Communication.dll')
 @($sources|ForEach-Object {@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $repo $_) -Algorithm SHA256).Hash}})|ConvertTo-Json -Depth 5|Set-Content "$root/source-files.json" -Encoding utf8
 @{runId=$RunId;started=$started.ToString('O');ended=[DateTimeOffset]::Now.ToString('O');code=(git rev-parse HEAD);branch=(git branch --show-current);sdk=(dotnet --version);os=[Runtime.InteropServices.RuntimeInformation]::OSDescription;checks=$checks;passed=($null -eq $failure -and $cleanupErrors.Count -eq 0 -and @($checks|Where-Object {!$_.passed}).Count -eq 0);cleanupErrors=$cleanupErrors;limits=@('No automatic reconnect implementation.','Alarm and ACK were synthetic test input, not simulator execution results.','No original-request/ACK correlation implementation.','No long-duration/load test.')}|ConvertTo-Json -Depth 8|Set-Content "$root/verification.json" -Encoding utf8
}
if($failure){throw $failure}
if($cleanupErrors.Count){throw ($cleanupErrors -join '; ')}
Write-Output "Evidence: $root"
