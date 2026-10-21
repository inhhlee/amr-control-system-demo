param([string]$Attempt='final',[switch]$SkipBuild)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location $repo
function Recorded([string]$Name,[string[]]$Arguments) {
 $log=Join-Path $PSScriptRoot "$Attempt-$Name.log"
 if(Test-Path -LiteralPath $log){throw 'Use a new Attempt; existing logs are preserved.'}
 $start=[DateTimeOffset]::Now
 & dotnet @Arguments *> $log
 $exit=$LASTEXITCODE
 @{Command=@('dotnet')+$Arguments;Started=$start.ToString('o');Finished=[DateTimeOffset]::Now.ToString('o');ExitCode=$exit} |
  ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot "$Attempt-$Name.json") -Encoding utf8
 Get-Content -LiteralPath $log -Tail 9
 if($exit -ne 0){throw "$Name failed: $exit"}
}
if (-not $SkipBuild) {
 Recorded 'build' @('build','AmrControlSystem.slnx','--no-restore','-m:1','-nr:false','-p:UseSharedCompilation=false')
}
Recorded 'simulator' @('test','tests/Amr.Simulator.Tests/Amr.Simulator.Tests.csproj','--no-build','--no-restore','--logger',"trx;LogFileName=$Attempt-simulator.trx",'--results-directory',$PSScriptRoot)
Recorded 'communication' @('test','tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj','--no-build','--no-restore','--logger',"trx;LogFileName=$Attempt-communication.trx",'--results-directory',$PSScriptRoot)
