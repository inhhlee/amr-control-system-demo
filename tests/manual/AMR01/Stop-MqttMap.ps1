param([Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,48}$')][string]$RunId)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$root=Join-Path $repo "evidence/AMR/$RunId"
$session=Get-Content "$root/session.json" -Raw|ConvertFrom-Json
$docker=(Get-Command docker -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$process=Get-Process -Id $session.ServerPid -ErrorAction SilentlyContinue
$hostExit=$null
if($process){
 if($process.StartTime.ToUniversalTime().Ticks -ne $session.ServerStartTicks -or $process.Path -ne $session.Exe){throw 'Server identity changed; stop refused'}
 $null=$process.Handle
 & pwsh -NoProfile -File "$PSScriptRoot/Send-OwnedCtrlC.ps1" -TargetProcessId $process.Id -StartTicks $session.ServerStartTicks -ResultPath "$root/stop-signal.json"
 if($LASTEXITCODE -ne 0){throw 'Graceful map stop failed'}
 $process.WaitForExit();$hostExit=$process.ExitCode;$process.Dispose()
}
$label=& $docker inspect $session.ContainerId --format '{{index .Config.Labels "amr.map.run"}}'
if($LASTEXITCODE -ne 0 -or $label -ne $RunId){throw 'Broker identity mismatch; removal refused'}
& $docker logs --timestamps $session.ContainerId 2>&1|Set-Content "$root/broker.log"
& $docker rm -f -v $session.ContainerId|Out-Null
if($LASTEXITCODE -ne 0){throw 'Broker removal failed'}
$after=& $docker ps -a --no-trunc --format '{{json .}}'
$after|Set-Content "$root/containers-after.jsonl"
$before=@(Get-Content "$root/containers-before.jsonl"|Where-Object {$_}|ForEach-Object {$_|ConvertFrom-Json})
$current=@($after|ForEach-Object {$_|ConvertFrom-Json})
$same=@($before|Where-Object {$b=$_;!@($current|Where-Object {$_.ID -eq $b.ID -and $_.State -eq $b.State}).Count}).Count -eq 0
$closed=$false
try{$null=Invoke-WebRequest "$($session.Url)/api/state" -TimeoutSec 2}catch{$closed=$true}
@{HostExitCode=$hostExit;HostPortClosed=$closed;OwnedBrokerRemoved=($session.ContainerId -notin $current.ID);ExistingContainersPreserved=$same;Finished=[DateTimeOffset]::Now.ToString('o')}|ConvertTo-Json|Set-Content "$root/cleanup.json"
if(!$closed -or !$same -or ($null -ne $hostExit -and $hostExit -ne 0)){throw 'Cleanup verification failed'}
Write-Output 'Map and owned Broker stopped; existing containers preserved.'
