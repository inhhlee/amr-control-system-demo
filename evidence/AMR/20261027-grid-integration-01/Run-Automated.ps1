$ErrorActionPreference='Stop'
$runRoot=$PSScriptRoot
$repoRoot=(Resolve-Path (Join-Path $runRoot '../../..')).Path
Set-Location -LiteralPath $repoRoot
function Run-Dotnet([string]$name,[string[]]$arguments) {
    $log=Join-Path $runRoot ($name+'.log')
    & dotnet @arguments *> $log
    $result=$LASTEXITCODE
    Write-Output "$name exit=$result"
    if($result -ne 0){Get-Content -LiteralPath $log -Tail 35;throw "$name failed ($result)"}
    Get-Content -LiteralPath $log -Tail 5
}
Run-Dotnet 'environment-authorized' @('--info')
Run-Dotnet 'restore-authorized' @('restore','AmrControlSystem.slnx','--verbosity','minimal')
Run-Dotnet 'build' @('build','AmrControlSystem.slnx','--no-restore','--verbosity','minimal','-m:1','-nr:false','-p:UseSharedCompilation=false')
foreach($project in @('Amr.Communication.Tests','Amr.Simulator.Tests','Amr.ControlGui.Tests','Amr.Simulator.Map.Tests')){
    Run-Dotnet $project @('test',"tests/$project/$project.csproj",'--no-build','--no-restore','--logger',"trx;LogFileName=$project.trx",'--results-directory',(Join-Path $runRoot 'tests'),'-m:1','-nr:false')
}

