param([Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,48}$')][string]$RunId)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repository
$run = Join-Path $repository "evidence/COM-05/$RunId"
if (Test-Path -LiteralPath $run) { throw 'Execution ID exists.' }
New-Item -ItemType Directory -Path $run | Out-Null
$previousUrl = $env:AMR_SEQ_URL
$previousRun = $env:AMR_LOG_RUN_ID
$subscriber = $null

function Start-Native([string]$File, [string[]]$Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new($File)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    if (!$process.Start()) { throw 'Unable to start test process.' }
    return $process
}
function Invoke-Native([string]$File, [string[]]$Arguments, [string]$Name, [int]$TimeoutMs = 15000) {
    $process = Start-Native $File $Arguments
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit($TimeoutMs)) { $process.Kill($true); throw "Test process timeout: $Name" }
        $out = $stdout.GetAwaiter().GetResult()
        $err = $stderr.GetAwaiter().GetResult()
        $out | Set-Content "$run/$Name.stdout.log" -Encoding utf8
        $err | Set-Content "$run/$Name.stderr.log" -Encoding utf8
        return [pscustomobject]@{exitCode=$process.ExitCode;stdout=$out;stderr=$err}
    } finally { $process.Dispose() }
}

try {
    $started = [DateTimeOffset]::UtcNow
    # Inspect the existing managed service; do not start, stop or reconfigure it.
    $container = (docker inspect amr-demo-mosquitto-1 | ConvertFrom-Json)[0]
    if ($LASTEXITCODE -ne 0 -or !$container.State.Running) { throw 'Existing MQTT Broker is not running.' }
    if ($container.HostConfig.PortBindings.'1883/tcp'[0].HostPort -ne '1884') { throw 'Unexpected Broker port.' }
    $env:AMR_SEQ_URL = 'http://127.0.0.1:5341'
    $env:AMR_LOG_RUN_ID = $RunId
    $seq = Invoke-RestMethod "$env:AMR_SEQ_URL/api" -TimeoutSec 5
    $dll = Join-Path $repository 'src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.dll'
    $ids = @(1..3 | ForEach-Object { "COM05_${RunId}_$_" })
    $topics = @($ids | ForEach-Object { "amr/$_/command" })
    $subArgs = @('exec', 'amr-demo-mosquitto-1', 'mosquitto_sub', '-h', '127.0.0.1', '-p', '1883', '-V', 'mqttv5', '-F', '%t %q %r %p')
    foreach ($topic in $topics) { $subArgs += @('-t', $topic) }
    # A fourth command would reveal an unintended publish from an invalid CLI request.
    # A container PTY makes Mosquitto flush debug lines immediately, so SUBACK gates publication.
    $subscriber = Start-Native 'docker' (@('exec','-t') + $subArgs[1..($subArgs.Length - 1)] + @('-C', '4', '-W', '15', '-d'))
    $subError = $subscriber.StandardError.ReadToEndAsync()
    $prefix = [Collections.Generic.List[string]]::new()
    do {
        $line = $subscriber.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(8)).GetAwaiter().GetResult()
        if ($null -eq $line) { throw 'Subscriber exited before SUBACK.' }
        $prefix.Add($line)
    } while ($line -notmatch 'received SUBACK')
    $remaining = $subscriber.StandardOutput.ReadToEndAsync()
    $requests = @()
    $types = @('MOVE','STOP','RESET')
    for ($i = 0; $i -lt 3; $i++) {
        $commandArgs = @($dll, '--command', $types[$i], '--amr-id', $ids[$i])
        if ($i -eq 0) { $commandArgs += @('--x', '-30.5', '--y', '15.25') }
        $result = Invoke-Native 'dotnet' $commandArgs $types[$i]
        if ($result.exitCode -ne 0 -or $result.stdout -notmatch 'CommandPublished' -or $result.stdout -match 'SUCCESS') { throw 'Command run failed or claimed execution success.' }
        $matches = [regex]::Matches($result.stdout, 'CommandId="(CMD-[a-f0-9]{32})"')
        $commandIds = @($matches | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
        if ($commandIds.Count -ne 1) { throw 'Request/send ID was not preserved.' }
        $requests += [ordered]@{amrId=$ids[$i];type=$types[$i];commandId=$commandIds[0];topic=$topics[$i];exitCode=$result.exitCode}
    }
    $invalid = Invoke-Native 'dotnet' @($dll,'--command','MOVE','--amr-id',$ids[0],'--x','NaN','--y','1') 'invalid'
    if ($invalid.exitCode -ne 1 -or $invalid.stderr -notmatch 'InvalidCommandOptions' -or $invalid.stdout -match 'CommandRequested|CommandPublished') { throw 'Invalid input was not rejected before connect.' }
    if (!$subscriber.WaitForExit(20000)) { throw 'Subscriber failed to terminate within test deadline.' }
    $subOutput = ($prefix -join "`n") + "`n" + $remaining.GetAwaiter().GetResult()
    $subOutput | Set-Content "$run/subscriber.stdout.log" -Encoding utf8
    $subError.GetAwaiter().GetResult() | Set-Content "$run/subscriber.stderr.log" -Encoding utf8
    if ($subscriber.ExitCode -ne 27) { throw 'Expected subscriber timeout after exactly three commands, without a fourth.' }
    $received = @($subOutput -split "`r?`n" | Where-Object { $_.StartsWith('amr/') })
    if ($received.Count -ne 3) { throw "Expected three commands, observed $($received.Count)." }
    $wire = @()
    foreach ($line in $received) {
        $parts = $line -split ' ', 4
        $body = $parts[3] | ConvertFrom-Json
        $request = @($requests | Where-Object { $_.commandId -eq $body.cmdId })
        if ($request.Count -ne 1 -or $parts[0] -ne $request[0].topic -or $body.type -ne $request[0].type -or $parts[1] -ne '0' -or $parts[2] -ne '0') { throw 'Wire target/ID/type/QoS/retain mismatch.' }
        if ($body.type -eq 'MOVE') {
            if ($body.param.x -ne -30.5 -or $body.param.y -ne 15.25) { throw 'MOVE coordinates changed.' }
        } elseif (@($body.param.PSObject.Properties).Count -ne 0) { throw 'STOP/RESET parameters were not empty.' }
        if ($body.ts -lt $started.AddSeconds(-1).ToUnixTimeMilliseconds() -or $body.ts -gt [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()) { throw 'Unexpected command timestamp.' }
        $wire += [ordered]@{topic=$parts[0];qos=0;retain=$false;payload=$body}
    }
    if (@($requests.commandId | Select-Object -Unique).Count -ne 3) { throw 'Command IDs were reused.' }
    # A new subscriber must not receive the previous commands as retained messages.
    $late = Invoke-Native 'docker' ($subArgs + @('-C','1','-W','2')) 'late-subscriber' 5000
    if ($late.exitCode -ne 27 -or $late.stdout.Trim().Length -ne 0) { throw 'Unexpected retained command.' }
    $filter = [uri]::EscapeDataString("RunId = '$RunId' and EventType = 'CommandPublished'")
    $events = Invoke-RestMethod "$env:AMR_SEQ_URL/api/events?filter=$filter&count=100&render=true" -TimeoutSec 5
    $events | ConvertTo-Json -Depth 12 | Set-Content "$run/seq-events.json" -Encoding utf8
    if (@($events).Count -ne 3) { throw 'Expected three structured publish events in Seq.' }
    foreach ($request in $requests) {
        $query = [uri]::EscapeDataString("RunId = '$RunId' and AmrId = '$($request.amrId)' and CommandId = '$($request.commandId)'")
        $matched = Invoke-RestMethod "$env:AMR_SEQ_URL/api/events?filter=$query&count=100&render=true" -TimeoutSec 5
        if (@($matched).Count -ne 2) { throw 'Expected requested/published pair for each ID in Seq.' }
    }
    [ordered]@{baseCommit=(git rev-parse HEAD);branch=(git branch --show-current);sourceState='uncommitted #29';
        startedAtUtc=$started.ToString('O');completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');sdk=(dotnet --version);
        os=[Runtime.InteropServices.RuntimeInformation]::OSDescription;brokerImage=$container.Config.Image;
        endpoint='127.0.0.1:1884';seqVersion=$seq.Version;serviceStartedOrStopped=$false} |
        ConvertTo-Json -Depth 5 | Set-Content "$run/environment.json" -Encoding utf8
    $files = @(rg --files src tests -g '*.cs' -g '*.csproj' -g '*.ps1')
    @($files | ForEach-Object { [ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash} }) |
        ConvertTo-Json -Depth 4 | Set-Content "$run/source-version.json" -Encoding utf8
    [ordered]@{result='passed';requests=$requests;received=$wire;invalidExitCode=$invalid.exitCode;
        unexpectedCommands=0;lateRetainedCommands=0;seqPublishedEvents=$events.Count;actualAmrExecution='not tested';
        ackCorrelation='not implemented';servicesReused=$true} | ConvertTo-Json -Depth 8 | Set-Content "$run/verification.json" -Encoding utf8
    Write-Output "PASS: three real MQTT commands, invalid rejection, no retained command, Seq ID correlation. $run"
} catch {
    $_ | Out-String | Set-Content "$run/failure.log" -Encoding utf8
    throw
} finally {
    if ($null -ne $subscriber) {
        if (!$subscriber.HasExited) { $subscriber.Kill($true) }
        $subscriber.Dispose()
    }
    $env:AMR_SEQ_URL = $previousUrl
    $env:AMR_LOG_RUN_ID = $previousRun
}
