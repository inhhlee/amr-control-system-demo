param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunId,
    [switch]$AllowBrokerStop
)
$ErrorActionPreference = 'Stop'
if (!$AllowBrokerStop) { throw 'This test stops the amr-demo Broker. Use -AllowBrokerStop only for an isolated test Broker.' }
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repository
$run = Join-Path $repository "evidence/COM-01/$RunId"
if (Test-Path -LiteralPath $run) { throw 'Execution ID exists. Choose a new RunId.' }
New-Item -ItemType Directory -Path $run | Out-Null
$started = [DateTimeOffset]::UtcNow
$process = $null
$restoreBroker = $false
$stdout = "$run/console.stdout.log"
$stderr = "$run/console.stderr.log"

function Wait-Log([scriptblock]$condition) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    do {
        [string]$text = if (Test-Path -LiteralPath $stdout) { Get-Content -LiteralPath $stdout -Raw } else { '' }
        if ($text.Length -gt 0 -and (& $condition $text)) { return $text }
        if ($process.HasExited) { throw 'Console exited before expected output.' }
        Start-Sleep -Milliseconds 20
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'Expected output timed out.'
}

try {
    $container = docker compose -f docker/compose.yaml ps -q mosquitto
    if ($LASTEXITCODE -ne 0 -or !$container) { throw 'An isolated amr-demo Broker must already be running.' }
    docker inspect $container --format '{{.Name}} {{.Config.Image}} {{json .NetworkSettings.Ports}}' > "$run/broker-before.log"
    if ($LASTEXITCODE -ne 0) { throw 'Broker inspection failed.' }
    [ordered]@{ startedAtUtc=$started.ToString('O'); sdk=(dotnet --version);
        os=[Runtime.InteropServices.RuntimeInformation]::OSDescription;
        baseCommit=(git rev-parse HEAD); branch=(git branch --show-current);
        endpoint='127.0.0.1:1884'; container=$container; brokerStopExplicitlyAllowed=$true } |
        ConvertTo-Json | Set-Content -Encoding utf8 "$run/environment.json"
    $files = @(rg --files src tests -g '*.cs' -g '*.csproj' -g '*.ps1' -g '*.json') + @('global.json','AmrControlSystem.slnx')
    [ordered]@{baseCommit=(git rev-parse HEAD);files=@($files | ForEach-Object {
        [ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}
    })} | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 "$run/source-version.json"

    $executable = Join-Path $repository 'src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.exe'
    $process = Start-Process -FilePath $executable -ArgumentList @('--host','127.0.0.1','--port','1884',
        '--test-delay-amr','AMR_001','--test-delay-ms','60000') -WorkingDirectory $repository -WindowStyle Hidden `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $null = $process.Handle
    $null = Wait-Log { param($text) $text.Contains('Subscribed TopicFilters=') }
    $inputs = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'inputs.json') -Raw | ConvertFrom-Json).sessions[1]
    $inputs | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 "$run/inputs.json"
    foreach ($case in $inputs) {
        $info = [Diagnostics.ProcessStartInfo]::new('docker')
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        foreach ($argument in @('compose','-f','docker/compose.yaml','exec','-T','mosquitto','mosquitto_pub',
                '-h','127.0.0.1','-p','1883','-V','mqttv5','-q','0','-t',$case.topic,'-m',$case.payload)) {
            $info.ArgumentList.Add($argument)
        }
        $publisher = [Diagnostics.Process]::Start($info)
        try {
            $outputTask = $publisher.StandardOutput.ReadToEndAsync()
            $errorTask = $publisher.StandardError.ReadToEndAsync()
            if (!$publisher.WaitForExit(8000)) { $publisher.Kill($true); throw 'Publisher timed out.' }
            $outputTask.GetAwaiter().GetResult() + $errorTask.GetAwaiter().GetResult() | Add-Content "$run/publish.log"
            if ($publisher.ExitCode -ne 0) { throw 'MQTT publication failed.' }
        }
        finally { $publisher.Dispose() }
        if ($case.name -eq 'heartbeat-AMR_001') {
            $null = Wait-Log { param($text) $text.Contains('ProcessingStarted AmrId="AMR_001" Kind=heartbeat') }
        }
    }
    $null = Wait-Log { param($text)
        ([regex]::Matches($text, 'QueueAdmission Result=Accepted')).Count -eq 5 -and
        ([regex]::Matches($text, ' Received ')).Count -eq 1
    }

    # 시험 전용 Compose 서비스만 중단하고 finally에서 실행 전의 가동 상태로 되돌린다.
    $restoreBroker = $true
    docker compose -f docker/compose.yaml stop -t 5 mosquitto *> "$run/broker-stop.log"
    if ($LASTEXITCODE -ne 0) { throw 'Test Broker stop failed.' }
    if (!$process.WaitForExit(20000)) { throw 'Console failed to finish after Broker disconnection.' }
    $process.WaitForExit()
    docker compose -f docker/compose.yaml logs --no-color --timestamps --since $started.ToString('O') mosquitto *> "$run/broker.log"
    if ($LASTEXITCODE -ne 0) { throw 'Broker log collection failed.' }
    $text = Get-Content -LiteralPath $stdout -Raw
    $events = @($text -split '\r?\n' | Where-Object { $_.Contains(' QueueEvent ') } | ForEach-Object {
        $_.Substring($_.IndexOf(' QueueEvent ')+12) | ConvertFrom-Json
    })
    $discarded = ($events | Where-Object Code -eq 'ShutdownDiscarded' | Measure-Object Count -Sum).Sum
    $cancelled = @($events | Where-Object Code -eq 'HandlerCancelled').Count
    $received = [regex]::Matches($text, ' Received ').Count
    if ($process.ExitCode -ne 1 -or !$text.Contains('ConnectionLost') -or !$text.Contains('SubscriptionStopped') -or
        !$text.Contains('Disposed') -or $discarded -ne 3 -or $cancelled -ne 1 -or $received -ne 1) {
        throw 'Unexpected disconnect cleanup result.'
    }
    if ((Get-Item -LiteralPath $stderr).Length -ne 0) { throw 'Unexpected stderr output.' }
    $lossOffset = $text.IndexOf('ConnectionLost')
    if ($text.Substring($lossOffset).Contains('ProcessingStarted') -or $text.Substring($lossOffset).Contains(' Received ')) {
        throw 'Processing started or delivered after connection loss.'
    }
    [ordered]@{result='passed';completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');
        publications=5;delivered=$received;handlerCancelled=$cancelled;shutdownDiscarded=$discarded;
        connectionLost=$true;subscriptionStopped=$true;disposed=$true;exitCode=$process.ExitCode;
        startedAfterLoss=$false;deliveredAfterLoss=$false} |
        ConvertTo-Json | Set-Content -Encoding utf8 "$run/verification.json"
    Write-Output "PASS: real Broker disconnection, cancellation/discard, cleanup and exit 1. Evidence: $run"
}
catch { $_ | Out-String | Set-Content -Encoding utf8 "$run/runner-failure.log"; throw }
finally {
    if ($null -ne $process) {
        if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
    if ($restoreBroker) {
        docker compose -f docker/compose.yaml start mosquitto *> "$run/broker-restore.log"
        if ($LASTEXITCODE -ne 0) { throw 'Test finished, but restoring the test Broker failed. See broker-restore.log.' }
    }
}
