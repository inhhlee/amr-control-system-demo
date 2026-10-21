param([ValidateSet('before','after')][string]$Phase='after')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location $repo
function Recorded([string]$Name,[string[]]$Arguments,[int]$ExpectedExit=0) {
 $log=Join-Path $PSScriptRoot "$Phase-$Name.log"
 if(Test-Path -LiteralPath $log){throw 'Existing execution is preserved. Use a new evidence directory.'}
 $start=[DateTimeOffset]::Now
 & dotnet @Arguments *> $log
 $exit=$LASTEXITCODE
 @{Command=@('dotnet')+$Arguments;Started=$start.ToString('o');Finished=[DateTimeOffset]::Now.ToString('o');ExitCode=$exit;ExpectedExitCode=$ExpectedExit} |
  ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot "$Phase-$Name.json") -Encoding utf8
 Get-Content -LiteralPath $log -Tail 14
 if($exit -ne $ExpectedExit){throw "$Name exit=$exit, expected=$ExpectedExit"}
}
Recorded 'build' @('build','AmrControlSystem.slnx','--no-restore','-m:1','-nr:false','-p:UseSharedCompilation=false')
if($Phase -eq 'before') {
 Recorded 'reproduction' @('test','tests/Amr.Simulator.Tests/Amr.Simulator.Tests.csproj','--no-build','--no-restore','--filter','FullyQualifiedName~ConnectionLossBeforeConnectReturns', '--logger','trx;LogFileName=before-reproduction.trx','--results-directory',$PSScriptRoot) 1
} else {
 Recorded 'simulator' @('test','tests/Amr.Simulator.Tests/Amr.Simulator.Tests.csproj','--no-build','--no-restore','--logger','trx;LogFileName=after-simulator.trx','--results-directory',$PSScriptRoot)
}
