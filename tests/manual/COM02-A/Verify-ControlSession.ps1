param(
 [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,48}$')][string]$RunId,
 [string]$ConsoleExe='src/Amr.Communication.Console/bin/ControlSessionVerify/Amr.Communication.Console.exe'
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../../..").Path
Set-Location $repo
$exe=(Resolve-Path -LiteralPath $ConsoleExe).Path
$root=Join-Path $repo "evidence/COM-02/$RunId"
if(Test-Path -LiteralPath $root){throw 'Use a fresh RunId.'}
$null=New-Item -ItemType Directory $root
$docker=(Get-Command docker -CommandType Application | Select-Object -First 1).Source
function Docker([string[]]$Arguments){$value=& $docker @Arguments 2>&1|Out-String;if($LASTEXITCODE -ne 0){throw "Docker failed: $value"};$value.Trim()}
function Require([bool]$ok,[string]$message){if(!$ok){throw $message}}
$before=Docker @('ps','-a','--no-trunc','--format','{{json .}}')
$before|Set-Content "$root/containers-before.jsonl"
"listener 1883 0.0.0.0`nallow_anonymous true`npersistence false`nlog_dest stdout`nlog_type all"|Set-Content "$root/broker.conf" -Encoding ascii
$container=$null;$process=$null
$checks=[Collections.Generic.List[object]]::new()
function Owner {
 Require ($container -match '^[a-f0-9]{64}$') 'Missing owned container ID.'
 Require ((Docker @('inspect',$container,'--format','{{index .Config.Labels "amr.session.run"}}')) -eq $RunId) 'Broker identity changed.'
}
function Publish([string]$kind,[string]$id='CONSOLE_ONE',[string]$status='OK'){
 $payload=if($kind -eq 'heartbeat'){@{id=$id;ts=123;status=$status}}else{@{id=$id;ts=123;pos=@{x=1;y=2};sp=0;state='IDLE'}}
 $null=Docker @('exec',$container,'mosquitto_pub','-h','127.0.0.1','-t',"amr/$id/$kind",'-m',($payload|ConvertTo-Json -Compress))
}
function Wait-Log([string]$pattern){
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds(10)
 do{$text=[string](Get-Content "$root/session.stdout.log" -Raw);if($text -match $pattern){return $text};Require (!$process.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline) "Missing log $pattern";Start-Sleep -Milliseconds 30}while($true)
}
try {
 $container=Docker @('run','--detach','--pull','never','--name',"amr-session-$RunId",'--label',"amr.session.run=$RunId",'--publish','127.0.0.1::1883','--tmpfs','/mosquitto/data','--tmpfs','/mosquitto/log','--mount',"type=bind,source=$root/broker.conf,target=/mosquitto/config/mosquitto.conf,readonly",'eclipse-mosquitto:2.1.2-alpine')
 Owner
 $mapping=Docker @('port',$container,'1883/tcp')
 Require ($mapping -match '^127\.0\.0\.1:(\d+)$') 'Unexpected Broker port.'
 $port=[int]$Matches[1]
 $process=Start-Process $exe -ArgumentList @('--monitor-amr','CONSOLE_ONE','--host','127.0.0.1','--port',"$port") -WindowStyle Hidden -WorkingDirectory $repo -RedirectStandardOutput "$root/session.stdout.log" -RedirectStandardError "$root/session.stderr.log" -PassThru
 $null=$process.Handle
 $ticks=$process.StartTime.ToUniversalTime().Ticks
 @{containerId=$container;brokerPort=$port;exe=$exe;pid=$process.Id;startTicks=$ticks}|ConvertTo-Json|Set-Content "$root/owned-session.json"
 $null=Wait-Log 'WaitingForShutdown'
 Publish heartbeat
 $null=Wait-Log 'HeartbeatMonitoringStarted'
 $checks.Add(@{name='map-free-valid-heartbeat';verdict='passed'})
 Publish telemetry
 Publish heartbeat CONSOLE_ONE BAD
 Publish heartbeat OTHER
 $null=Wait-Log 'HeartbeatTimedOut'
 Start-Sleep -Milliseconds 600
 $text=[string](Get-Content "$root/session.stdout.log" -Raw)
 Require ([regex]::Matches($text,'HeartbeatTimedOut').Count -eq 1 -and $text -notmatch 'HeartbeatRecovered') 'Wrong timeout or invalid recovery.'
 Require ($text -match 'Rejected' -and $text -match 'IgnoredAmr' -and $text -match 'telemetry') 'Missing isolation observations.'
 $checks.Add(@{name='timeout-valid-target-only';verdict='passed'})
 Publish heartbeat
 $null=Wait-Log 'HeartbeatRecovered'
 Owner
 $null=Docker @('stop','--time','3',$container)
 Require ($process.WaitForExit(8000)) 'Console did not stop on Broker loss.'
 $process.Refresh()
 $text=[string](Get-Content "$root/session.stdout.log" -Raw)
 Require ($process.ExitCode -eq 1 -and $text -match 'BrokerDisconnected' -and $text -match 'Closed' -and [regex]::Matches($text,'HeartbeatRecovered').Count -eq 1 -and [regex]::Matches($text,'HeartbeatTimedOut').Count -eq 1) 'Broker loss did not terminate session correctly.'
 $checks.Add(@{name='recovery-and-broker-loss-exit';verdict='passed';exitCode=$process.ExitCode})
 $process.Dispose();$process=$null
 # A fresh session must await first Heartbeat and close normally on owner cancellation.
 $null=Docker @('start',$container)
 $mapping=Docker @('port',$container,'1883/tcp');Require ($mapping -match '^127\.0\.0\.1:(\d+)$') 'Unexpected restarted port.';$port=[int]$Matches[1]
 $process=Start-Process $exe -ArgumentList @('--monitor-amr','CONSOLE_ONE','--host','127.0.0.1','--port',"$port") -WindowStyle Hidden -WorkingDirectory $repo -RedirectStandardOutput "$root/fresh.stdout.log" -RedirectStandardError "$root/fresh.stderr.log" -PassThru
 $null=$process.Handle;$ticks=$process.StartTime.ToUniversalTime().Ticks
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds(8)
 do{ $fresh=[string](Get-Content "$root/fresh.stdout.log" -Raw); if($fresh -match 'WaitingForShutdown'){break};Require (!$process.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline) 'Fresh session startup failed.';Start-Sleep -Milliseconds 40 }while($true)
 Require ($fresh -notmatch 'HeartbeatMonitoringStarted|HeartbeatRecovered|HeartbeatTimedOut') 'Fresh session reused liveness.'
 & pwsh -NoProfile -File "$PSScriptRoot/../AMR01/Send-OwnedCtrlC.ps1" -TargetProcessId $process.Id -StartTicks $ticks -ResultPath "$root/normal-stop.json"
 Require ($LASTEXITCODE -eq 0 -and $process.WaitForExit(5000)) 'Owned Ctrl+C failed.'
 $process.Refresh()
 Require ($process.ExitCode -eq 0 -and (Get-Content "$root/fresh.stdout.log" -Raw) -match 'Closed') 'Normal session cleanup failed.'
 $checks.Add(@{name='fresh-session-and-owner-cancel';verdict='passed';exitCode=$process.ExitCode})
} catch {$checks.Add(@{name='failure';verdict='failed';reason=$_.Exception.Message});throw}
finally {
 if($process){if(!$process.HasExited){if($process.Path -ne $exe -or $process.StartTime.ToUniversalTime().Ticks -ne $ticks){throw 'Process identity changed.'};$process.Kill();$process.WaitForExit()};$process.Dispose()}
 if($container){Owner;Docker @('logs','--timestamps',$container)|Set-Content "$root/broker.log";$null=Docker @('rm','-f','-v',$container)}
 $after=Docker @('ps','-a','--no-trunc','--format','{{json .}}');$after|Set-Content "$root/containers-after.jsonl"
 $current=@($after -split "`n"|Where-Object {$_}|ForEach-Object {$_|ConvertFrom-Json})
 $same=@($before -split "`n"|Where-Object {$_}|ForEach-Object {$_|ConvertFrom-Json}|Where-Object {$b=$_;!@($current|Where-Object {$_.ID -eq $b.ID -and $_.State -eq $b.State}).Count}).Count -eq 0
 $checks.Add(@{name='owned-cleanup-existing-containers';verdict=$(if($same){'passed'}else{'failed'})})
 $checks|ConvertTo-Json -Depth 8|Set-Content "$root/verification.json"
 Require $same 'Existing container state changed.'
}
Write-Output "PASS: map-free control session, Timeout, recovery, Broker loss, fresh session and Ctrl+C. $root"
