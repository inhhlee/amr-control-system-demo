param([Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,64}$')][string]$RunId,[switch]$FinalAcceptance)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../../..").Path
Set-Location -LiteralPath $repo
$root=Join-Path $repo "evidence/COM-06/$RunId"
if(Test-Path -LiteralPath $root){throw 'Use a fresh RunId.'}
$null=New-Item -ItemType Directory -Path $root
$dockerExe=(Get-Command docker -CommandType Application | Select-Object -First 1).Source
$consoleExe=Join-Path $repo 'src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.exe'
$simulatorExe=Join-Path $repo 'src/Amr.Simulator/bin/Debug/net10.0/Amr.Simulator.exe'
$env:AMR_SEQ_URL=$null;$env:AMR_SEQ_API_KEY=$null;$env:AMR_LOG_RUN_ID=$null
$checks=[Collections.Generic.List[object]]::new()
$children=[Collections.Generic.List[object]]::new()
$container=$null;$failure=$null
$started=[DateTimeOffset]::Now
$tracked=@(Get-ChildItem src/Amr.Communication,src/Amr.Communication.Console -Recurse -File | Where-Object {$_.Extension -in '.cs','.csproj' -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]'} | ForEach-Object {$_.FullName})
$tracked+=@($consoleExe,(Join-Path $repo 'src/Amr.Communication/bin/Debug/net10.0/Amr.Communication.dll'),$simulatorExe,(Join-Path $repo 'config/simulator.single.json'),$PSCommandPath)
@($tracked|ForEach-Object {@{path=[IO.Path]::GetRelativePath($repo,$_);sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}})|ConvertTo-Json -Depth 4|Set-Content "$root/source-files.json" -Encoding utf8
function Native([string]$File,[string[]]$Arguments){
 $info=[Diagnostics.ProcessStartInfo]::new($File);$info.UseShellExecute=$false;$info.CreateNoWindow=$true
 $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
 foreach($arg in $Arguments){$info.ArgumentList.Add($arg)}
 $p=[Diagnostics.Process]::new();$p.StartInfo=$info
 try{
  if(!$p.Start()){throw 'Native command could not start.'}
  $out=$p.StandardOutput.ReadToEndAsync();$err=$p.StandardError.ReadToEndAsync()
  if(!$p.WaitForExit(20000)){$p.Kill($true);$p.WaitForExit();throw 'Native command timed out.'}
  $result=[pscustomobject]@{Code=$p.ExitCode;Out=$out.GetAwaiter().GetResult();Err=$err.GetAwaiter().GetResult()}
  @{file=$File;arguments=$Arguments;exitCode=$p.ExitCode;at=[DateTimeOffset]::Now.ToString('O')}|ConvertTo-Json -Compress|Add-Content "$root/commands.jsonl" -Encoding utf8
  return $result
 }finally{$p.Dispose()}
}
function Docker([string[]]$Arguments){$r=Native $dockerExe $Arguments;if($r.Code -ne 0){throw "Docker failed: $($r.Err) $($r.Out)"};$r.Out.Trim()}
function Check([string]$Name,[bool]$Passed,[string]$Detail){
 $checks.Add(@{name=$Name;passed=$Passed;detail=$Detail})
 if(!$Passed){throw "Failed: $Name ($Detail)"}
 Write-Output "PASS $Name"
}
function Owner{
 if($container -notmatch '^[a-f0-9]{64}$' -or (Docker @('inspect',$container,'--format','{{index .Config.Labels "amr.trial.run"}}')) -ne $RunId){throw 'Owned Broker identity changed.'}
}
function Start-Reader([string]$Name,[string]$File,[string[]]$Arguments){
 $info=[Diagnostics.ProcessStartInfo]::new($File);$info.UseShellExecute=$false;$info.CreateNoWindow=$true
 $info.WorkingDirectory=$repo;$info.RedirectStandardInput=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
 foreach($arg in $Arguments){$info.ArgumentList.Add($arg)}
 $p=[Diagnostics.Process]::new();$p.StartInfo=$info
 if(!$p.Start()){throw 'Child startup failed.'}
 $entry=[pscustomobject]@{Name=$Name;Process=$p;Ticks=$p.StartTime.ToUniversalTime().Ticks;File=$File;Lines=[Collections.Generic.List[string]]::new();Next=$null;ErrorTask=$p.StandardError.ReadToEndAsync();Done=$false}
 $children.Add($entry)
 return $entry
}
function Text($Entry){$Entry.Lines -join [Environment]::NewLine}
function Wait-Line($Entry,[string]$Pattern,[int]$Count=1,[int]$Seconds=15){
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds($Seconds)
 while([regex]::Matches((Text $Entry),$Pattern).Count -lt $Count){
  if($null -eq $Entry.Next){$Entry.Next=$Entry.Process.StandardOutput.ReadLineAsync()}
  $remaining=$deadline-[DateTimeOffset]::UtcNow
  if($remaining.TotalMilliseconds -le 0){throw "Missing output: $Pattern"}
  $line=$Entry.Next.WaitAsync($remaining).GetAwaiter().GetResult();$Entry.Next=$null
  if($null -eq $line){throw "Child exited before output: $Pattern"}
  $Entry.Lines.Add($line);$line|Add-Content "$root/$($Entry.Name).stdout.log" -Encoding utf8
 }
}
function Finish($Entry,[int]$Expected){
 if(!$Entry.Process.WaitForExit(8000)){throw 'Child did not exit.'}
 if($null -ne $Entry.Next){$line=$Entry.Next.GetAwaiter().GetResult();$Entry.Next=$null;if($null -ne $line){$Entry.Lines.Add($line)}}
 $tail=$Entry.Process.StandardOutput.ReadToEnd()
 if($tail){$Entry.Lines.Add($tail)}
 (Text $Entry)|Set-Content "$root/$($Entry.Name).stdout.log" -Encoding utf8
 $errorText=$Entry.ErrorTask.GetAwaiter().GetResult();$errorText|Set-Content "$root/$($Entry.Name).stderr.log" -Encoding utf8
 $Entry.Process.Refresh()
 if($Entry.Process.ExitCode -ne $Expected -or $errorText){throw "Unexpected child exit: $($Entry.Name) $($Entry.Process.ExitCode) $errorText"}
 $Entry.Done=$true
}
function Input-Line($Entry,[string]$Line){$Entry.Process.StandardInput.WriteLine($Line);$Entry.Process.StandardInput.Flush();@{child=$Entry.Name;line=$Line;at=[DateTimeOffset]::Now.ToString('O')}|ConvertTo-Json -Compress|Add-Content "$root/console-inputs.jsonl" -Encoding utf8}
function Publish([string]$Kind,$Payload,[string]$AmrId='AMR_001'){
 $json=$Payload|ConvertTo-Json -Depth 6 -Compress
 $null=Docker @('exec',$container,'mosquitto_pub','-h','127.0.0.1','-t',"amr/$AmrId/$Kind",'-m',$json)
}
function Simulator([string]$Name,$Receiver,[int]$Count){
 $p=Start-Process -FilePath $simulatorExe -ArgumentList @('--config','config/simulator.single.json','--mqtt','--host','127.0.0.1','--port',"$port",'--telemetry-ms','200','--heartbeat-ms','1000') -WindowStyle Hidden -WorkingDirectory $repo -RedirectStandardOutput "$root/$Name.stdout.log" -RedirectStandardError "$root/$Name.stderr.log" -PassThru
 $null=$p.Handle;$ticks=$p.StartTime.ToUniversalTime().Ticks
 try{
  Wait-Line $Receiver '위치=\(1,2\) 속도=0 상태=IDLE' $Count
  if(!$p.WaitForExit(5000)){throw 'Simulator did not finish.'};$p.Refresh()
  if($p.ExitCode -ne 0 -or (Get-Content "$root/$Name.stderr.log" -Raw)){throw 'Simulator failed.'}
 }finally{if(!$p.HasExited){if($p.Path -ne $simulatorExe -or $p.StartTime.ToUniversalTime().Ticks -ne $ticks){throw 'Simulator identity changed.'};$p.Kill();$p.WaitForExit()};$p.Dispose()}
}
function Observer([string]$Name,[int]$Count,[string]$Topic='amr/AMR_001/command'){
 $s=Start-Reader $Name $dockerExe @('exec','-t',$container,'mosquitto_sub','-h','127.0.0.1','-t',$Topic,'-C',"$Count",'-W','20','-F','%t %p','-d')
 Wait-Line $s 'SUBACK'
 return $s
}
try{
 $before=Docker @('ps','-a','--no-trunc','--format','{{json .}}');$before|Set-Content "$root/containers-before.jsonl"
 $dockerServer=Docker @('version','--format','{{json .Server}}');$dockerServer|Set-Content "$root/docker-server.json"
 $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
 @('listener 1883 0.0.0.0','allow_anonymous true','persistence false','log_dest stdout','log_type all')|Set-Content "$root/broker.conf" -Encoding ascii
 $container=Docker @('run','--detach','--pull','never','--name',"amr-trial-$RunId",'--label',"amr.trial.run=$RunId",'--publish',"127.0.0.1:${port}:1883",'--tmpfs','/mosquitto/data','--tmpfs','/mosquitto/log','--mount',"type=bind,source=$root/broker.conf,target=/mosquitto/config/mosquitto.conf,readonly",'eclipse-mosquitto:2.1.2-alpine')
 Owner
 @{containerId=$container;port=$port;started=$started.ToString('O');runId=$RunId}|ConvertTo-Json|Set-Content "$root/session.json"
 $run=Start-Reader 'trial' $consoleExe @('--trial','--no-color','--show-payload','--host','127.0.0.1','--port',"$port",'--reconnect-delay-seconds','1','--reconnect-attempts','10')
 Wait-Line $run '\[접속완료\]'
 Check 'connect-and-four-subscriptions' ((Text $run) -match 'Heartbeat·Telemetry·Alarm·ACK 구독 승인') 'Ready was logged after four subscriptions.'
 Simulator 'simulator-before' $run 1
 Check 'actual-simulator-final-received' $true 'Actual Simulator final (1,2), IDLE, speed0 was received.'
 Publish 'heartbeat' @{id='AMR_001';status='OK'}
 Wait-Line $run '\[검사거부\].*MissingField'
 Publish 'alarm' @{id='AMR_001';ts=100;code='TEST_SENSOR';level='WARN';alarmState='ACTIVE'}
 Publish 'ack' @{cmdId='TEST_RESPONSE';ts=101;result='SUCCESS'}
 Wait-Line $run '종류=alarm';Wait-Line $run '종류=ack.*TEST_RESPONSE'
 Check 'invalid-rejected-and-valid-alarm-ack-received' $true 'Synthetic Alarm/ACK are communication test inputs, not actual AMR execution.'
 $sub=Observer 'commands-before' 3
 foreach($verb in @('move 10 20','stop','reset')){Input-Line $run $verb}
 Wait-Line $run '\[명령발행\]' 3
 Wait-Line $sub '(?m)^amr/AMR_001/command ' 3
 Finish $sub 0
 $wire=@($sub.Lines|Where-Object {$_ -match '^amr/AMR_001/command \{'}|ForEach-Object {($_ -split ' ',2)[1]|ConvertFrom-Json})
 Check 'three-interactive-commands-received' ($wire.Count -eq 3 -and ($wire.type -join ',') -eq 'MOVE,STOP,RESET' -and $wire[0].param.x -eq 10 -and $wire[0].param.y -eq 20 -and @($wire.cmdId|Select-Object -Unique).Count -eq 3) 'MOVE coordinates, STOP/RESET and unique request IDs received on real Broker.'
 if($FinalAcceptance){
  $ackReceiver=Start-Reader 'ack-receiver' $consoleExe @('--host','127.0.0.1','--port',"$port",'--show-payload')
  Wait-Line $ackReceiver 'WaitingForShutdown'
  $requests=[Collections.Generic.List[object]]::new()
  foreach($m in $wire){$requests.Add(@{amrId='AMR_001';cmdId=$m.cmdId;type=$m.type})}
  $secondObserver=Observer 'second-device-commands' 3 'amr/AMR_002/command'
  foreach($verb in @('MOVE','STOP','RESET')){
   $arguments=@('--command',$verb,'--amr-id','AMR_002','--host','127.0.0.1','--port',"$port")
   if($verb -eq 'MOVE'){$arguments+=@('--x','-3','--y','4')}
   $publication=Native $consoleExe $arguments
   $publication.Out|Set-Content "$root/device2-$verb.stdout.log" -Encoding utf8
   $publication.Err|Set-Content "$root/device2-$verb.stderr.log" -Encoding utf8
   if($publication.Code -ne 0){throw 'Second-device command publication failed.'}
  }
  Wait-Line $secondObserver '(?m)^amr/AMR_002/command ' 3;Finish $secondObserver 0
  $secondWire=@($secondObserver.Lines|Where-Object {$_ -match '^amr/AMR_002/command \{'}|ForEach-Object {($_ -split ' ',2)[1]|ConvertFrom-Json})
  foreach($m in $secondWire){$requests.Add(@{amrId='AMR_002';cmdId=$m.cmdId;type=$m.type})}
  if($requests.Count -ne 6 -or @($requests.cmdId|Select-Object -Unique).Count -ne 6){throw 'Expected six distinct command requests.'}
  $known=@{};foreach($request in $requests){$known["$($request.amrId)|$($request.cmdId)"]=$request}
  $ackMatches=[Collections.Generic.List[object]]::new();$ackCount=0
  for($index=$requests.Count-1;$index -ge 0;$index--){
   $request=$requests[$index]
   $payload=@{cmdId=$request.cmdId;result=$(if($index%2){'SUCCESS'}else{'FAILURE'});ts=([long]1000+$index)}
   if($index%2){$payload.id=$request.amrId}
   if($index%3 -eq 0){$payload.reason='TEST_REASON_PRESERVED'}
   Publish 'ack' $payload $request.amrId
   $ackCount++;Wait-Line $ackReceiver ' Received .*"Kind":"ack"' $ackCount
   $last=@($ackReceiver.Lines|Where-Object {$_ -match ' Received .*"Kind":"ack"'})[-1]
   $actual=($last -split ' Received ',2)[1]|ConvertFrom-Json
   $key="$($actual.AmrId)|$($actual.Model.CommandId)"
   if(!$known.ContainsKey($key) -or $actual.AmrId -ne $request.amrId -or $actual.Model.CommandId -ne $request.cmdId -or $actual.Model.Result -ne $payload.result -or $actual.Model.Timestamp -ne $payload.ts -or $actual.Model.Id -ne $payload.id -or $actual.Model.Reason -ne $payload.reason){throw 'ACK metadata was changed or linked to the wrong request.'}
   $ackMatches.Add(@{request=$known[$key];actual=$actual;testDouble=$true})
  }
  Check 'two-device-reversed-ack-request-identity' ($ackMatches.Count -eq 6 -and $ackMatches[0].request.cmdId -eq $requests[5].cmdId -and $ackMatches[5].request.cmdId -eq $requests[0].cmdId) 'Six real Broker commands and reverse ACKs matched by original target plus request ID in a test-only lookup.'
  Check 'ack-success-failure-and-optional-fields-preserved' (@($ackMatches.actual.Model.Result|Select-Object -Unique).Count -eq 2) 'SUCCESS/FAILURE and original timestamp, optional payload ID/reason were compared without normalization.'
  $wrong=@{id='AMR_002';cmdId=$requests[0].cmdId;result='SUCCESS';ts=2000}
  Publish 'ack' $wrong 'AMR_002';$ackCount++;Wait-Line $ackReceiver ' Received .*"Kind":"ack"' $ackCount
  $last=@($ackReceiver.Lines|Where-Object {$_ -match ' Received .*"Kind":"ack"'})[-1]
  $unmatched=($last -split ' Received ',2)[1]|ConvertFrom-Json
  $unknownKey="$($unmatched.AmrId)|$($unmatched.Model.CommandId)"
  Check 'different-device-ack-not-matched-to-request' (!$known.ContainsKey($unknownKey) -and $unmatched.AmrId -eq 'AMR_002' -and $ackMatches.Count -eq 6) 'The valid response retained its Topic target, so the test lookup did not attach it to the other AMR request.'
  Publish 'ack' @{result='SUCCESS';ts=2001} 'AMR_001'
  Wait-Line $ackReceiver 'Rejected.*MissingField.*\$\.cmdId'
  Publish 'ack' @{cmdId='AFTER_INVALID_ACK';result='FAILURE';reason='EXPECTED_TEST_INPUT';ts=2002} 'AMR_001'
  $ackCount++;Wait-Line $ackReceiver ' Received .*"Kind":"ack"' $ackCount
  $last=@($ackReceiver.Lines|Where-Object {$_ -match ' Received .*"Kind":"ack"'})[-1]
  $afterInvalid=($last -split ' Received ',2)[1]|ConvertFrom-Json
  Check 'bad-ack-rejected-and-next-ack-delivered' ($afterInvalid.Model.CommandId -eq 'AFTER_INVALID_ACK' -and $afterInvalid.Model.Result -eq 'FAILURE') 'Malformed ACK was rejected; the same receiver continued with the next valid failure response.'
  @{requests=$requests;reverseMatches=$ackMatches;unmatched=$unmatched;afterInvalid=$afterInvalid;scope='test doubles for communication identity, not actual AMR execution or Core business tracking'}|ConvertTo-Json -Depth 12|Set-Content "$root/ack-correlation.json" -Encoding utf8
 }
 Wait-Line $run '\[AMR무응답\]'
 Publish 'heartbeat' @{id='AMR_001';ts=200;status='OK'}
 Wait-Line $run '\[생존복구\]'
 Check 'heartbeat-timeout-and-recovery' $true 'Heartbeat gap and recovery were observed while Broker stayed connected.'
 Owner;$null=Docker @('stop','--time','3',$container)
 Wait-Line $run '\[연결끊김\]';Wait-Line $run '\[재접속대기\]'
 if($FinalAcceptance){Finish $ackReceiver 1}
 Input-Line $run 'stop';Wait-Line $run '\[발행거부\]'
 Check 'disconnected-command-rejected' $true 'New request was rejected, without an application send queue.'
 Owner;$null=Docker @('start',$container)
 Wait-Line $run '\[재접속완료\]' 1 25
 Check 'automatic-reconnect-and-resubscribe' $true 'Same console process automatically connected and subscribed after Broker restart.'
 if($FinalAcceptance){
  Publish 'ack' @{cmdId='AFTER_RECONNECT_ACK';result='FAILURE';reason='TEST_ONLY';ts=3000}
  Publish 'alarm' @{id='AMR_001';code='AFTER_RECOVERY';level='WARN';alarmState='CLEARED';ts=3001}
  Wait-Line $run '종류=ack.*AFTER_RECONNECT_ACK';Wait-Line $run '종류=alarm.*AFTER_RECOVERY'
  Check 'alarm-and-ack-received-after-reconnect' $true 'All four subscriptions resume: final Simulator run checks telemetry/heartbeat; synthetic inputs check Alarm/ACK.'
 }
 $none=Native $dockerExe @('exec',$container,'mosquitto_sub','-h','127.0.0.1','-t','amr/AMR_001/command','-C','1','-W','2','-F','%t %p')
 $none.Out|Set-Content "$root/no-replay.stdout.log";$none.Err|Set-Content "$root/no-replay.stderr.log"
 Check 'no-command-replay-after-reconnect' ($none.Code -ne 0 -and !$none.Out -and $none.Err -match 'Timed out') 'No command observed during a 2-second post-reconnect window; source/automatic tests also check no replay.'
 Simulator 'simulator-after' $run 2
 Check 'actual-receive-resumed-after-reconnect' $true 'Second actual Simulator run final Telemetry received in the same console process.'
 $subAfter=Observer 'commands-after' 1
 Input-Line $run 'reset';Wait-Line $run '\[명령발행\]' 4
 Wait-Line $subAfter '(?m)^amr/AMR_001/command ' 1;Finish $subAfter 0
 Check 'new-command-after-reconnect' ((Text $subAfter) -match '"type":"RESET"') 'A new explicit request can be published after recovery.'
 $gitExe=(Get-Command git -CommandType Application | Select-Object -First 1).Source
 $bashExe=Join-Path (Split-Path (Split-Path $gitExe -Parent) -Parent) 'bin/bash.exe'
 if(!(Test-Path -LiteralPath $bashExe)){throw 'Git Bash executable was not found beside Git.'}
 $bashCommand="set -o pipefail; cd /c/amr-control-system-demo && { sleep 2; printf 'status\n/exit\n'; } | dotnet src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.dll --trial --no-color --host 127.0.0.1 --port $port --reconnect-attempts 0"
 $bash=Native $bashExe @('--noprofile','--norc','-c',$bashCommand)
 $bash.Out|Set-Content "$root/git-bash.stdout.log" -Encoding utf8;$bash.Err|Set-Content "$root/git-bash.stderr.log" -Encoding utf8
 Check 'git-bash-command-input-and-exit' ($bash.Code -eq 0 -and $bash.Out -match '\[접속완료\]' -and $bash.Out -match 'Broker=Connected' -and $bash.Out -match 'ExitCode=0') 'Git Bash without profiles ran the compiled trial with piped status/exit input. Native screen/color/keyboard capture is separate.'
 Input-Line $run 'status';Wait-Line $run '\[상태\].*Broker=Connected'
 Input-Line $run '/exit';Wait-Line $run '\[종료결과\].*ExitCode=0';Finish $run 0
 Check 'normal-exit-with-input-pipe-open' $true '/exit joined input/supervisor/session work while stdin pipe remained open.'
 Owner;$null=Docker @('stop','--time','3',$container)
 $fail=Start-Reader 'exhausted' $consoleExe @('--trial','--no-color','--host','127.0.0.1','--port',"$port",'--connect-timeout-seconds','1','--reconnect-delay-seconds','1','--reconnect-attempts','2')
 Wait-Line $fail '\[종료결과\].*ExitCode=1' 1 20;Finish $fail 1
 Check 'retry-limit-terminates-without-extra-input' ([regex]::Matches((Text $fail),'\[재접속시도\]').Count -eq 2 -and [regex]::Matches((Text $fail),'\[접속\]').Count -eq 3) 'Initial attempt plus two retries; exhausted process exits with stdin still open.'
 $cancel=Start-Reader 'cancel-wait' $consoleExe @('--trial','--no-color','--host','127.0.0.1','--port',"$port",'--connect-timeout-seconds','1','--reconnect-delay-seconds','30')
 Wait-Line $cancel '\[재접속대기\]'
 $exitStarted=[DateTimeOffset]::UtcNow;Input-Line $cancel '/exit';Wait-Line $cancel '\[종료결과\].*ExitCode=0' 1 4;Finish $cancel 0
 Check 'exit-cancels-retry-delay' (([DateTimeOffset]::UtcNow-$exitStarted).TotalSeconds -lt 4) 'Exit did not wait for the configured 30-second retry delay.'
}catch{
 $failure=$_;$_|Out-String|Set-Content "$root/failure.log" -Encoding utf8
}finally{
 foreach($entry in $children){
  try{
   if(!$entry.Process.HasExited){if($entry.Process.StartTime.ToUniversalTime().Ticks -ne $entry.Ticks){throw 'Child identity changed.'};$entry.Process.Kill($true);$entry.Process.WaitForExit()}
   if($null -ne $entry.Next){try{$null=$entry.Next.GetAwaiter().GetResult()}catch{}}
   if(!$entry.Done){try{($entry.Lines -join [Environment]::NewLine)+[Environment]::NewLine+$entry.Process.StandardOutput.ReadToEnd()|Set-Content "$root/$($entry.Name).stdout.log";$entry.ErrorTask.GetAwaiter().GetResult()|Set-Content "$root/$($entry.Name).stderr.log"}catch{}}
   $entry.Process.Dispose()
  }catch{if(!$failure){$failure=$_}}
 }
 if($container){try{Owner;Docker @('logs','--timestamps',$container)|Set-Content "$root/broker.log";$null=Docker @('rm','-f','-v',$container)}catch{if(!$failure){$failure=$_}}}
 try{
  $after=Docker @('ps','-a','--no-trunc','--format','{{json .}}');$after|Set-Content "$root/containers-after.jsonl"
  $old=@($before -split '\r?\n'|Where-Object {$_}|ForEach-Object {$_|ConvertFrom-Json});$new=@($after -split '\r?\n'|Where-Object {$_}|ForEach-Object {$_|ConvertFrom-Json})
  $same=@($old|Where-Object {$id=$_.ID;!@($new|Where-Object {$_.ID -eq $id}).Count}).Count -eq 0
  $mutations=@(Get-Content "$root/commands.jsonl"|ForEach-Object {$_|ConvertFrom-Json}|Where-Object {$_.arguments[0] -in @('start','stop','rm')})
  $protected=@($old.ID)
  $touched=@($mutations|Where-Object {@($_.arguments|Where-Object {$_ -in $protected}).Count}).Count -gt 0
  $checks.Add(@{name='existing-containers-preserved';passed=($same -and !$touched);detail='Protected container IDs retained and no start/stop/rm was issued against them. Independent restart-state changes are recorded, not attributed to this test.'})
 }catch{if(!$failure){$failure=$_}}
 @{runId=$RunId;code=(git rev-parse HEAD);branch=(git branch --show-current);started=$started.ToString('O');ended=[DateTimeOffset]::Now.ToString('O');sdk=(dotnet --version);port=$port;checks=$checks;passed=(!$failure -and @($checks|Where-Object {!$_.passed}).Count -eq 0);scope='actual MQTT and CLI integration; not full task1/Core/AMR execution validation'}|ConvertTo-Json -Depth 8|Set-Content "$root/verification.json" -Encoding utf8
}
if($failure){throw $failure}
Write-Output "PASS actual console trial: $root"
