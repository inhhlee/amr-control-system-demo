param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,36}$')][string]$RunId,
    [ValidatePattern('^$|^[a-zA-Z0-9_-]{1,36}$')][string]$ReuseAutomaticFrom
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repository
$run = Join-Path $repository "evidence/DEMO/$RunId"
if (Test-Path "$run/execution.json") { throw 'Execution ID exists; use a new RunId.' }
New-Item -ItemType Directory -Path $run -Force | Out-Null
$env:GIT_OPTIONAL_LOCKS = '0'
# Logging is disabled only for these test processes. No existing Seq is started.
$env:AMR_SEQ_URL = $null
$env:AMR_SEQ_API_KEY = $null
$env:AMR_LOG_RUN_ID = $RunId
$docker = (Get-Command docker -ErrorAction SilentlyContinue).Source
if (!$docker) { $docker = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe' }
$env:PATH = (Split-Path -Parent $docker) + [IO.Path]::PathSeparator + $env:PATH
$pwsh = (Get-Command pwsh).Source
$steps = [Collections.Generic.List[object]]::new()
$failure = $null
$container = $null
$started = [DateTimeOffset]::UtcNow
[ordered]@{runId=$RunId;startedAtUtc=$started.ToString('O');head=(git rev-parse HEAD);branch=(git branch --show-current)} | ConvertTo-Json | Set-Content "$run/execution.json" -Encoding utf8

function Invoke-Recorded([string]$File, [string[]]$Arguments, [string]$Name, [int]$TimeoutSeconds = 120) {
    $at = [DateTimeOffset]::UtcNow
    $info = [Diagnostics.ProcessStartInfo]::new($File)
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.WorkingDirectory = $repository
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $info
    $exitCode = -1; $out = ''; $err = ''; $launched = $false
    $outputTask = $null; $errorTask = $null
    try {
        $launched = $process.Start()
        if (!$launched) { throw "Unable to start $Name" }
        $outputTask = $process.StandardOutput.ReadToEndAsync(); $errorTask = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true); $process.WaitForExit(); throw "Timeout in $Name after ${TimeoutSeconds}s"
        }
        $process.WaitForExit(); $exitCode = $process.ExitCode
    } finally {
        if ($launched -and !$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        if ($null -ne $outputTask) { $out = $outputTask.GetAwaiter().GetResult() }
        if ($null -ne $errorTask) { $err = $errorTask.GetAwaiter().GetResult() }
        $out | Set-Content "$run/$Name.stdout.log" -Encoding utf8
        $err | Set-Content "$run/$Name.stderr.log" -Encoding utf8
        [ordered]@{name=$Name;program=$File;arguments=$Arguments;startedAtUtc=$at.ToString('O');completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');exitCode=$exitCode} | ConvertTo-Json -Compress | Add-Content "$run/commands.jsonl" -Encoding utf8
        $process.Dispose()
    }
    if ($exitCode -ne 0) { throw "$Name failed ($exitCode); see $run/$Name.stderr.log" }
    return $out
}
function Invoke-Step([string]$Name, [string]$Title, [scriptblock]$Action) {
    $at = [DateTimeOffset]::UtcNow
    Write-Output "RUN $Name : $Title"
    [ordered]@{step=$Name;title=$Title;atUtc=$at.ToString('O')} | ConvertTo-Json | Set-Content "$run/current-stage.json" -Encoding utf8
    try {
        & $Action
        $steps.Add([ordered]@{step=$Name;title=$Title;result='passed';startedAtUtc=$at.ToString('O');completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')})
        Write-Output "PASS $Name"
    } catch {
        $steps.Add([ordered]@{step=$Name;title=$Title;result='failed';error=$_.Exception.Message;startedAtUtc=$at.ToString('O');completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}); throw
    } finally { $steps.ToArray() | ConvertTo-Json -Depth 5 | Set-Content "$run/steps.json" -Encoding utf8 }
}
function Assert-Passed([string]$Path) {
    if ((Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json).result -ne 'passed') { throw "Missing passed result: $Path" }
}
try {
    $files = @(git -c core.quotepath=false ls-files src tests config global.json AmrControlSystem.slnx) + @('tests/manual/DEMO/Invoke-ReportDemo.ps1')
    @($files | Sort-Object -Unique | ForEach-Object { [ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash} }) | ConvertTo-Json -Depth 4 | Set-Content "$run/source-version.json" -Encoding utf8
    $dockerVersion = Invoke-Recorded $docker @('version','--format','{{.Client.Version}} / {{.Server.Version}}') 'docker-version'
    $beforeText = Invoke-Recorded $docker @('ps','-a','--no-trunc','--format','{{json .}}') 'containers-before'
    $beforeText | Set-Content "$run/containers-before.jsonl" -Encoding utf8
    $volumesBefore = Invoke-Recorded $docker @('volume','ls','--format','{{.Name}}') 'volumes-before'
    $image = 'eclipse-mosquitto:2.1.2-alpine'
    $imageId = (Invoke-Recorded $docker @('image','inspect',$image,'--format','{{.Id}}') 'image').Trim()
    [ordered]@{os=[Runtime.InteropServices.RuntimeInformation]::OSDescription;sdk=(dotnet --version);runtime=(dotnet --list-runtimes);powershell=$PSVersionTable.PSVersion.ToString();docker=$dockerVersion.Trim();image=$image;imageId=$imageId;mqttnet='5.2.0.1603';seqTested=$false;head=(git rev-parse HEAD);branch=(git branch --show-current);sourceState='working tree, source-version.json'} | ConvertTo-Json -Depth 4 | Set-Content "$run/environment.json" -Encoding utf8
    Invoke-Step '01-automatic' 'Current communication and Simulator build / automated regression' {
        if ($ReuseAutomaticFrom) {
            $priorRun = Join-Path $repository "evidence/DEMO/$ReuseAutomaticFrom"
            # Reuse only actual successful TRX with identical product/test/build/config files.
            $priorFiles = Get-Content "$priorRun/source-version.json" -Raw | ConvertFrom-Json
            foreach ($file in ($priorFiles | Where-Object { $_.path -notlike '*.ps1' })) {
                if ((Get-FileHash -LiteralPath $file.path).Hash -ne $file.sha256) { throw "Automatic evidence is stale: $($file.path)" }
            }
            $counts = @()
            foreach ($project in @('Communication','Simulator')) {
                [xml]$trx = Get-Content "$priorRun/automatic/$project/$project.trx" -Raw
                $count = $trx.TestRun.ResultSummary.Counters
                if ([int]$count.total -le 0 -or [int]$count.total -ne [int]$count.passed -or [int]$count.failed -ne 0) { throw "Prior $project TRX is not fully passed." }
                $counts += [ordered]@{project=$project;total=[int]$count.total;passed=[int]$count.passed;trx="evidence/DEMO/$ReuseAutomaticFrom/automatic/$project/$project.trx"}
            }
            [ordered]@{sourceRunId=$ReuseAutomaticFrom;mode='reused, not rerun';nonScriptFilesIdentical=$true;projects=$counts} | ConvertTo-Json -Depth 6 | Set-Content "$run/automatic-reuse.json" -Encoding utf8
            return
        }
        foreach ($project in @('Communication','Simulator')) {
            $path = "tests/Amr.$project.Tests/Amr.$project.Tests.csproj"
            $null = Invoke-Recorded 'dotnet' @('build',$path,'--no-restore','-m:1','-nr:false','-p:UseSharedCompilation=false','--verbosity','minimal') "build-$project" 180
            $null = Invoke-Recorded 'dotnet' @('test',$path,'--no-build','--no-restore','--logger',"trx;LogFileName=$project.trx",'--results-directory',"$run/automatic/$project") "tests-$project" 180
        }
    }
    @'
listener 1883 0.0.0.0
allow_anonymous true
persistence false
log_dest stdout
log_type all
connection_messages true
log_timestamp true
'@ | Set-Content "$run/broker.conf" -Encoding ascii
    $name = ('amr-demo-' + $RunId).ToLowerInvariant()
    $container = (Invoke-Recorded $docker @('run','--detach','--pull','never','--name',$name,'--label',"amr.demo.run=$RunId",'--publish','127.0.0.1::1883','--mount',"type=bind,source=$run/broker.conf,target=/mosquitto/config/mosquitto.conf,readonly",$image) 'broker-create').Trim()
    if ($container -notmatch '^[a-f0-9]{64}$') { throw 'Owned Broker container ID was not confirmed.' }
    $mapping = (Invoke-Recorded $docker @('port',$container,'1883/tcp') 'broker-port').Trim()
    if ($mapping -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Expected loopback-only dynamic mapping.' }
    $brokerPort = [int]$Matches[1]
    $ready = $false
    for ($attempt=0; $attempt -lt 30; $attempt++) {
        $log = Invoke-Recorded $docker @('logs','--timestamps',$container) "broker-ready-$attempt"
        if ($log -match 'mosquitto version [^\r\n]+ running') { $ready=$true; break }
        Start-Sleep -Milliseconds 100
    }
    if (!$ready) { throw 'Isolated Broker failed to become ready.' }
    [ordered]@{containerId=$container;name=$name;endpoint=$mapping;imageId=$imageId;existingServicesChanged=$false} | ConvertTo-Json | Set-Content "$run/broker.json" -Encoding utf8
    Invoke-Step '02-messages' 'Two AMRs, four message types, malformed inputs and recovery' {
        $id = "$RunId-msg"
        $null = Invoke-Recorded $pwsh @('-NoProfile','-File','tests/manual/COM01-B/Verify-Mqtt.ps1','-RunId',$id,'-BrokerContainer',$container,'-BrokerPort',"$brokerPort") 'messages' 200
        Assert-Passed "evidence/COM-01/$id/verification.json"
    }
    Invoke-Step '03-commands' 'MOVE / STOP / RESET, invalid input and no retained commands' {
        $id = "$RunId-cmd"
        $null = Invoke-Recorded $pwsh @('-NoProfile','-File','tests/manual/COM05-A/Verify-Commands.ps1','-RunId',$id,'-BrokerContainer',$container,'-BrokerPort',"$brokerPort",'-SkipSeq') 'commands' 80
        Assert-Passed "evidence/COM-05/$id/verification.json"
    }
    Invoke-Step '04-keepalive' 'Idle KeepAlive for 12 seconds, PING pairs, Ctrl+C exit 0' {
        $id = "$RunId-c1"
        $null = Invoke-Recorded $pwsh @('-NoProfile','-File','tests/manual/COM03-C/Verify-KeepAlive.ps1','-RunId',$id) 'keepalive' 90
        Assert-Passed "evidence/COM-03/$id/verification.json"
    }
    Invoke-Step '05-disconnect' 'Broker stop, disconnect cleanup / exit 1, restart and NEW client' {
        $id = "$RunId-c2"
        $null = Invoke-Recorded $pwsh @('-NoProfile','-File','tests/manual/COM03-C/Verify-BrokerShutdown.ps1','-RunId',$id) 'disconnect' 100
        Assert-Passed "evidence/COM-03/$id/verification.json"
    }
    Invoke-Step '06-no-response' 'TCP stays open; discard Broker responses, detect timeout and exit 1' {
        $id = "$RunId-c3"
        $null = Invoke-Recorded $pwsh @('-NoProfile','-File','tests/manual/COM03-C/Verify-ResponseGate.ps1','-RunId',$id,'-MeasureTimeout','-BlockDelaySeconds','2') 'no-response' 100
        Assert-Passed "evidence/COM-03/$id/verification.json"
    }
    Invoke-Step '07-simulator' 'Actual initialization console regression; movement checked by automated tests' {
        $id = "$RunId-sim"
        $null = Invoke-Recorded $pwsh @('-NoProfile','-File','tests/manual/AMR01/Verify-Settings.ps1','-RunId',$id) 'simulator' 100
    }
} catch {
    $failure = $_
    $_ | Out-String | Set-Content "$run/failure.log" -Encoding utf8
} finally {
    $cleanupErrors = [Collections.Generic.List[string]]::new()
    if ($container -match '^[a-f0-9]{64}$') {
        foreach ($item in @(@{name='broker-final';args=@('logs','--timestamps',$container)},@{name='broker-stop';args=@('stop','--time','5',$container)},@{name='broker-remove';args=@('rm','--volumes',$container)})) {
            try { $null = Invoke-Recorded $docker $item.args $item.name }
            catch { $cleanupErrors.Add($_.Exception.Message); if (!$failure) { $failure=$_ } }
        }
    }
    try {
        $afterText = Invoke-Recorded $docker @('ps','-a','--no-trunc','--format','{{json .}}') 'containers-after'
        $afterText | Set-Content "$run/containers-after.jsonl" -Encoding utf8
        $before = @($beforeText -split '\r?\n' | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json })
        $after = @($afterText -split '\r?\n' | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json })
        if ($before.Count -ne $after.Count) { throw 'Container count changed after demo.' }
        foreach ($old in $before) {
            $current = @($after | Where-Object ID -eq $old.ID)
            if ($current.Count -ne 1 -or $current[0].State -ne $old.State -or $current[0].Names -ne $old.Names) { throw 'Pre-existing container identity/state changed.' }
        }
        foreach ($file in (Get-Content "$run/source-version.json" -Raw | ConvertFrom-Json)) {
            if ((Get-FileHash -LiteralPath $file.path).Hash -ne $file.sha256) { throw "Source changed during demo: $($file.path)" }
        }
        $volumesAfter = Invoke-Recorded $docker @('volume','ls','--format','{{.Name}}') 'volumes-after'
        $oldVolumes = @($volumesBefore -split '\r?\n' | Where-Object { $_ } | Sort-Object)
        $newVolumes = @($volumesAfter -split '\r?\n' | Where-Object { $_ } | Sort-Object)
        if (@(Compare-Object $oldVolumes $newVolumes).Count) { throw 'Docker volume inventory changed after demo.' }
        [ordered]@{result='passed';testBrokerRemoved=$true;testAnonymousVolumesRemoved=$true;existingVolumesPreserved=$true;volumeCountBefore=$oldVolumes.Count;volumeCountAfter=$newVolumes.Count;existingContainerStatesPreserved=$true;containersBefore=$before.Count;containersAfter=$after.Count;sourceUnchangedDuringExecution=$true;cleanupErrors=$cleanupErrors.ToArray()} | ConvertTo-Json -Depth 4 | Set-Content "$run/cleanup.json" -Encoding utf8
    } catch { $cleanupErrors.Add($_.Exception.Message); if (!$failure) { $failure=$_ } }
    if ($cleanupErrors.Count) { $cleanupErrors | Set-Content "$run/cleanup-errors.log" -Encoding utf8 }
    [ordered]@{runId=$RunId;result=if($failure){'failed'}else{'passed'};startedAtUtc=$started.ToString('O');completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');steps=$steps.ToArray();seqTested=$false;guiTested=$false;automaticReconnectTested=$false;amrCommandExecutionTested=$false;ackCorrelationImplemented=$false;simulatorRealtimeMovementTested=$false} | ConvertTo-Json -Depth 7 | Set-Content "$run/verification.json" -Encoding utf8
}
if ($failure) { throw $failure }
Write-Output "PASS: report demo; evidence/DEMO/$RunId/verification.json"
