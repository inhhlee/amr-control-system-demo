param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,80}$')][string]$RunId,
    [string]$ConsolePath='src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.exe',
    [string]$SeqUrl='http://127.0.0.1:5341'
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../../..").Path
Set-Location -LiteralPath $repo
$root=Join-Path $repo "evidence/LOG/$RunId"
if(Test-Path -LiteralPath $root){throw '새 RunId를 지정하세요.'}
New-Item -ItemType Directory -Path $root | Out-Null
$consoleExe=(Resolve-Path -LiteralPath ([IO.Path]::GetFullPath($ConsolePath,$repo))).Path
$dockerExe=(Get-Command docker -CommandType Application | Select-Object -First 1).Source
$simulatorExe=Join-Path $repo 'src/Amr.Simulator/bin/Debug/net10.0/Amr.Simulator.exe'
$children=[Collections.Generic.List[object]]::new();$checks=[Collections.Generic.List[object]]::new()
$container=$null;$reserved=$null
function Check([string]$Name,[bool]$Passed,[string]$Detail){
 $checks.Add(@{name=$Name;passed=$Passed;detail=$Detail});if(!$Passed){throw "FAIL $Name : $Detail"};Write-Output "PASS $Name"
}
function Docker([string[]]$Arguments){
 $text=& $dockerExe @Arguments 2>&1;if($LASTEXITCODE -ne 0){throw '시험 Docker 명령 실패'};($text -join "`n").Trim()
}
function StartChild([string]$Name,[string]$File,[string[]]$Arguments,[string]$Address='',[string]$LogRun=''){
 $info=[Diagnostics.ProcessStartInfo]::new($File);$info.WorkingDirectory=$repo;$info.UseShellExecute=$false;$info.CreateNoWindow=$true
 $info.RedirectStandardInput=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
 foreach($arg in $Arguments){$info.ArgumentList.Add($arg)}
 $info.Environment['AMR_SEQ_URL']=$Address;$info.Environment['AMR_LOG_RUN_ID']=$LogRun;$null=$info.Environment.Remove('AMR_SEQ_API_KEY')
 $process=[Diagnostics.Process]::Start($info)
 $child=[pscustomobject]@{Name=$Name;Process=$process;Lines=[Collections.Generic.List[string]]::new();Next=$null;Errors=$process.StandardError.ReadToEndAsync();Done=$false}
 $children.Add($child);return $child
}
function Pump($Child,[int]$Milliseconds){
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do{
  if($null -eq $Child.Next){$Child.Next=$Child.Process.StandardOutput.ReadLineAsync()}
  if($Child.Next.IsCompleted){$line=$Child.Next.GetAwaiter().GetResult();$Child.Next=$null;if($null -eq $line){break};$Child.Lines.Add($line)}
  else{Start-Sleep -Milliseconds 10}
 }while($clock.ElapsedMilliseconds -lt $Milliseconds)
}
function WaitLine($Child,[string]$Pattern,[int]$Count=1){
 $clock=[Diagnostics.Stopwatch]::StartNew()
 while(@($Child.Lines | Where-Object{$_ -match $Pattern}).Count -lt $Count){
  if($clock.Elapsed.TotalSeconds -gt 18){throw "입력/응답 시간 초과: $Pattern"};Pump $Child 40
 }
}
function InputLine($Child,[string]$Line){
 $Child.Process.StandardInput.WriteLine($Line);$Child.Process.StandardInput.Flush()
 @{child=$Child.Name;input=$Line;at=[DateTimeOffset]::UtcNow.ToString('O')}|ConvertTo-Json -Compress|Add-Content "$root/inputs.jsonl"
}
function Finish($Child,[bool]$AllowSeqFailure=$false){
 if(!$Child.Process.WaitForExit(9000)){throw '시험 콘솔 종료 시간 초과'}
 if($null -ne $Child.Next){$line=$Child.Next.GetAwaiter().GetResult();if($null -ne $line){$Child.Lines.Add($line)};$Child.Next=$null}
 $tail=$Child.Process.StandardOutput.ReadToEnd();if($tail){$Child.Lines.Add($tail)}
 $errorText=$Child.Errors.GetAwaiter().GetResult();$errorText|Set-Content "$root/$($Child.Name).stderr.log"
 ($Child.Lines -join "`n")|Set-Content "$root/$($Child.Name).stdout.log"
 if($Child.Process.ExitCode -ne 0 -or (!$AllowSeqFailure -and $errorText)){throw '콘솔 실패 또는 예상하지 않은 stderr'}
 $Child.Done=$true;return $errorText
}
function Rows($Events){
 @($Events | ForEach-Object{$row=@{Level=$_.Level};foreach($p in $_.Properties){$row[$p.Name]=$p.Value};$row})
}
function Query([string]$Filter){
 @(Invoke-RestMethod "$SeqUrl/api/events?filter=$([uri]::EscapeDataString($Filter))&count=1000&render=true" -TimeoutSec 5)
}
function StartAmr([string]$Name){StartChild $Name $simulatorExe @('--config',"$root/simulator.json",'--command-mode','--mqtt','--host','127.0.0.1','--port',"$port",'--telemetry-ms','200','--heartbeat-ms','1000')}
try{
 $api=Invoke-RestMethod "$SeqUrl/api" -TimeoutSec 5
 @{started=[DateTimeOffset]::Now.ToString('O');head=(git rev-parse HEAD);branch=(git branch --show-current);sdk=(dotnet --version);
 seqUrl=$SeqUrl;seqVersion=$api.Version;sharedSeqStopped=$false;console=$consoleExe;
 consoleDllSha256=(Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($consoleExe,'.dll')) -Algorithm SHA256).Hash}|
  ConvertTo-Json | Set-Content "$root/environment.json"
 $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
 $config=(Resolve-Path 'docker/mosquitto/config/mosquitto.conf').Path
 $container=Docker @('create','--name',"amr-trial-seq-$RunId",'--label',"amr.trial.seq=$RunId",'--publish',"127.0.0.1:${port}:1883",'--mount',"type=bind,source=$config,target=/mosquitto/config/mosquitto.conf,readonly",'eclipse-mosquitto:2.1.2-alpine')
 $null=Docker @('start',$container)
 '{"amrId":"AMR_001","initialPosition":{"x":-1,"y":0},"speedMps":1,"updateIntervalMs":50,"waypoints":[{"x":10,"y":2}]}' | Set-Content "$root/simulator.json"
 $arguments=@('--trial','--no-color','--show-payload','--host','127.0.0.1','--port',"$port",'--reconnect-delay-seconds','1','--reconnect-attempts','10')
 $trial=StartChild 'online' $consoleExe $arguments $SeqUrl "$RunId-online"
 WaitLine $trial '\[접속완료\]';$simulator=StartAmr 'amr-before';WaitLine $trial '종류=telemetry';Pump $trial 1300
 Check 'console-core-only' (@($trial.Lines|Where-Object{$_ -match '종류=telemetry'}).Count -eq 1 -and !($trial.Lines -match '\[내용\]|종류=heartbeat')) 'First Telemetry shown once; periodic Telemetry/Heartbeat/JSON hidden.'
 InputLine $trial 'detail on';WaitLine $trial '콘솔 상세 출력 ON';WaitLine $trial '\[수신상세\]';WaitLine $trial '\[내용\]'
 Check 'detail-on' $true 'Explicit detail mode shows periodic data and requested bounded preview.'
 InputLine $trial 'detail off';WaitLine $trial '콘솔 상세 출력 OFF';Pump $trial 200
 $before=@($trial.Lines|Where-Object{$_ -match '종류=telemetry'}).Count;Pump $trial 1000
 Check 'detail-off' (@($trial.Lines|Where-Object{$_ -match '종류=telemetry'}).Count -eq $before) 'Stable telemetry stopped printing; reception continues.'
 InputLine $trial 'move 10 2';WaitLine $trial '\[명령발행\]' 1;WaitLine $trial '상태=BUSY';Pump $trial 400
 InputLine $trial 'stop';WaitLine $trial 'result=SUCCESS.*reason=STOPPED';WaitLine $trial '상태=STOPPED'
 Check 'move-stop-final-ack' (@($trial.Lines|Where-Object{$_ -match 'result=FAILURE.*reason=STOP_INTERRUPTED'}).Count -gt 0) 'Independent MOVE failure and STOP success returned.'
 InputLine $trial 'move -1 0';WaitLine $trial 'result=SUCCESS.*reason=ARRIVED'
 Check 'manual-new-move-arrived' $true 'New MOVE from stopped position reached target with final ACK.'
 $snapshotCount=@($trial.Lines|Where-Object{$_ -match '종류=telemetry'}).Count
 $null=Docker @('stop','-t','1',$container);WaitLine $trial '\[연결끊김\]'
 $null=Docker @('start',$container);WaitLine $trial '\[재접속완료\]'
 $simulator=StartAmr 'amr-after';WaitLine $trial '종류=telemetry' ($snapshotCount+1)
 Check 'reconnect-receipt-visible' $true 'Fresh telemetry after reconnect is visible at Information even when IDLE repeats.'
 InputLine $trial '/exit';$null=Finish $trial
 $events=Query "RunId = '$RunId-online'";$events|ConvertTo-Json -Depth 12|Set-Content "$root/seq-events.json"
 $rows=Rows $events;$telemetry=@($rows|Where-Object{$_.EventType -eq 'TelemetryReceived'})
 Check 'seq-debug-telemetry' ($telemetry.Count -ge 10 -and @($telemetry|Where-Object{$_.Level -ne 'Debug'}).Count -eq 0) 'Debug Telemetry actually stored in Docker Seq.'
 Check 'seq-typed-fields' (@($telemetry|Where-Object{$_.AmrId -ne 'AMR_001' -or !$_.ContainsKey('X') -or !$_.ContainsKey('Y') -or !$_.ContainsKey('Speed') -or !$_.State -or !$_.SessionId -or !$_.DeviceTimestamp -or !$_.ReceivedAtUtc}).Count -eq 0) 'Device/session/position/speed/state/source and receive times present.'
 $stateEvents=Query "RunId = '$RunId-online' and EventType = 'TelemetryReceived' and AmrId = 'AMR_001' and State = 'BUSY' and @Level = 'Debug'"
 $stateEvents|ConvertTo-Json -Depth 12|Set-Content "$root/seq-state-query.json"
 Check 'seq-state-property-query' ($stateEvents.Count -gt 0) 'Server-side device/state/Debug filter returned moving telemetry.'
 $acks=@($rows|Where-Object{$_.EventType -eq 'AckReceived'});$ids=@($acks.CommandId|Sort-Object -Unique)
 Check 'seq-command-identity' ($acks.Count -eq 3 -and $ids.Count -eq 3 -and @($rows|Where-Object{$_.EventType -eq 'CommandRequested'}).Count -eq 3 -and @($rows|Where-Object{$_.EventType -eq 'CommandPublished'}).Count -eq 3) 'Three request IDs connect publication and actual final ACK.'
 $commandEvents=Query "RunId = '$RunId-online' and CommandId = '$($ids[0])'"
 $commandEvents|ConvertTo-Json -Depth 12|Set-Content "$root/seq-command-query.json"
 Check 'seq-command-property-query' ($commandEvents.Count -ge 3) 'Same request can be queried independently.'
 Check 'session-and-raw-boundary' (@($telemetry.SessionId|Sort-Object -Unique).Count -eq 2 -and @($rows|Where-Object{$_.ContainsKey('Payload') -or $_.ContainsKey('PayloadPreview') -or $_.ContainsKey('Model') -or $_.ContainsKey('Exception') -or $_.ContainsKey('ConsolePreview')}).Count -eq 0) 'Reconnect sessions differ; raw models/previews/exceptions not stored.'
 $reserved=[Net.Sockets.Socket]::new([Net.Sockets.AddressFamily]::InterNetwork,[Net.Sockets.SocketType]::Stream,[Net.Sockets.ProtocolType]::Tcp)
 $reserved.ExclusiveAddressUse=$true;$reserved.Bind([Net.IPEndPoint]::new([Net.IPAddress]::Loopback,0))
 $unavailable="http://127.0.0.1:$($reserved.LocalEndPoint.Port)"
 $offline=StartChild 'offline' $consoleExe $arguments $unavailable "$RunId-offline"
 WaitLine $offline '\[접속완료\]';WaitLine $offline '종류=telemetry'
 InputLine $offline 'move 10 2';WaitLine $offline '상태=BUSY';Pump $offline 300
 InputLine $offline 'stop';WaitLine $offline 'result=SUCCESS.*reason=STOPPED'
 InputLine $offline '/exit';$summary=Finish $offline $true
 Check 'seq-unavailable-communication-continues' ($summary -match 'SeqDeliverySummary.*Failed=[1-9]') 'MOVE/STOP and ACK still work; failed Seq delivery is counted at exit.'
 Check 'redirected-clean-output' (!(($trial.Lines -join "`n").Contains([char]27)) -and !(($trial.Lines -join "`n").Contains('trial>'))) 'Redirected plain logs do not contain prompt/cursor/ANSI controls.'
 @{result='passed';checks=$checks;telemetryDebugCount=$telemetry.Count;seqEvents=$events.Count;completed=[DateTimeOffset]::Now.ToString('O');sharedSeqStopped=$false}|
  ConvertTo-Json -Depth 6|Set-Content "$root/verification.json"
 Write-Output "PASS actual Docker Seq/trial: $root"
}catch{
 $_|Out-String|Set-Content "$root/failure.log"
 @{result='failed';checks=$checks}|ConvertTo-Json -Depth 6|Set-Content "$root/verification.json"
 throw
}finally{
 foreach($child in $children){
  if(!$child.Process.HasExited){$child.Process.Kill($true);$child.Process.WaitForExit()}
  if(!$child.Done){if($null -ne $child.Next){$line=$child.Next.GetAwaiter().GetResult();if($null -ne $line){$child.Lines.Add($line)}};$child.Lines.Add($child.Process.StandardOutput.ReadToEnd());($child.Lines -join "`n")|Set-Content "$root/$($child.Name).stdout.log";$child.Errors.GetAwaiter().GetResult()|Set-Content "$root/$($child.Name).stderr.log"}
  $child.Process.Dispose()
 }
 if($container){$owner=Docker @('inspect',$container,'--format','{{index .Config.Labels "amr.trial.seq"}}');if($owner -eq $RunId){$null=Docker @('rm','--force',$container)}}
 if($reserved){$reserved.Dispose()}
}
