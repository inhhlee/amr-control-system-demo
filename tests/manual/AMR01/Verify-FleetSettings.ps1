param([Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,48}$')][string]$RunId)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location $repo
$root = Join-Path $repo "evidence/AMR/$RunId"
if (Test-Path -LiteralPath $root) { throw 'Use a new RunId.' }
$null = New-Item -ItemType Directory -Path $root
$docker = (Get-Command docker -CommandType Application | Select-Object -First 1).Source
$simulator = (Resolve-Path 'src/Amr.Simulator/bin/Debug/net10.0/Amr.Simulator.exe').Path
$receiver = (Resolve-Path 'src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.exe').Path
function Docker([string[]]$Arguments) {
    $result = & $docker @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Docker failed: $result" }
    $result.Trim()
}
$container = $null; $receiveProcess = $null
$verifications = [System.Collections.Generic.List[object]]::new()
function Check([string]$name, [bool]$pass) {
    $verifications.Add([pscustomobject]@{ Name=$name; Passed=$pass })
    if (!$pass) { throw "Verification failed: $name" }
}
try {
    Docker @('ps','-a','--no-trunc','--format','{{json .}}') | Set-Content "$root/containers-before.jsonl"
    $null = Docker @('image','inspect','eclipse-mosquitto:2.1.2-alpine')
    "listener 1883 0.0.0.0`nallow_anonymous true`npersistence false`nlog_dest stdout`nlog_type all" | Set-Content "$root/broker.conf" -Encoding ascii
    $container = Docker @('run','--detach','--pull','never','--name',"amr-fleet-$RunId",'--label',"amr.fleet.run=$RunId",'--publish','127.0.0.1::1883','--mount',"type=bind,source=$root/broker.conf,target=/mosquitto/config/mosquitto.conf,readonly",'eclipse-mosquitto:2.1.2-alpine')
    if ($container -notmatch '^[a-f0-9]{64}$') { throw 'Unconfirmed owned container.' }
    $mapping = Docker @('port',$container,'1883/tcp')
    if ($mapping -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Expected loopback binding.' }
    $port = [int]$Matches[1]
    $receiveProcess = Start-Process $receiver -ArgumentList @('--host','127.0.0.1','--port',"$port",'--show-payload') -WorkingDirectory $repo -WindowStyle Hidden -RedirectStandardOutput "$root/receiver.stdout.log" -RedirectStandardError "$root/receiver.stderr.log" -PassThru
    $ready = $false
    for ($i=0; $i -lt 100; $i++) {
        if ((Get-Content "$root/receiver.stdout.log" -Raw) -match 'Subscribed.*ack') { $ready=$true; break }
        if ($receiveProcess.HasExited) { throw 'Receiver exited before subscribing.' }
        Start-Sleep -Milliseconds 100
    }
    Check 'ReceiverSubscribedFourTypes' $ready
    $template = Get-Content 'config/simulator.fleet.json' -Raw | ConvertFrom-Json -AsHashtable
    foreach ($count in @(1,2,3)) {
        $amrs = @()
        for ($i=0; $i -lt $count; $i++) {
            $amr = $template.amrs[0] | ConvertTo-Json -Depth 10 | ConvertFrom-Json -AsHashtable
            $amr.amrId = "F${count}_AMR_$i"
            $amr.initialPosition = @{x=0;y=$i}
            $amr.speedMps = $i+1
            $amr.waypoints = @(@{x=1;y=$i})
            $amrs += $amr
        }
        $config = Join-Path $root "fleet-$count.json"
        @{amrs=$amrs} | ConvertTo-Json -Depth 10 | Set-Content $config -Encoding utf8
        & $simulator --fleet-config $config --mqtt --host 127.0.0.1 --port $port --telemetry-ms 100 --heartbeat-ms 200 1> "$root/fleet-$count.stdout.log" 2> "$root/fleet-$count.stderr.log"
        Check "Fleet${count}Exit0" ($LASTEXITCODE -eq 0)
        $log = Get-Content "$root/fleet-$count.stdout.log" -Raw
        Check "Fleet${count}Count" ($log -match "FleetConfigurationLoaded AmrCount=$count")
        # QoS0 발행 반환이 아니라 실제 기존 수신 콘솔의 Received를 확인한다.
        $observed = $false
        for ($retry=0; $retry -lt 30; $retry++) {
            $received = @(foreach ($line in Get-Content "$root/receiver.stdout.log") {
                if ($line -match '\bReceived (\{.*\})$') { $Matches[1] | ConvertFrom-Json }
            })
            $observed = $true
            foreach ($amr in $amrs) {
                $telemetry = @($received | Where-Object { $_.AmrId -eq $amr.amrId -and $_.Kind -eq 'telemetry' })
                $heartbeat = @($received | Where-Object { $_.AmrId -eq $amr.amrId -and $_.Kind -eq 'heartbeat' })
                $final = $telemetry | Select-Object -Last 1
                if (!$heartbeat.Count -or !$final -or $final.Model.Position.X -ne 1 -or
                    $final.Model.Position.Y -ne $amr.initialPosition.y -or $final.Model.Speed -ne 0 -or $final.Model.State -ne 'IDLE') { $observed=$false }
            }
            if ($observed) { break }
            Start-Sleep -Milliseconds 100
        }
        Check "Fleet${count}EachIdReceivedTelemetryAndHeartbeat" $observed
        foreach ($amr in $amrs) {
            $id = [regex]::Escape($amr.amrId)
            Check "Fleet${count}Arrival-$($amr.amrId)" ($log -match "SimulationCompleted AmrId=$id X=1 Y=$($amr.initialPosition.y) State=IDLE CurrentSpeedMps=0")
        }
    }
    $bad = @{amrs=@($amrs[0],$amrs[0])} | ConvertTo-Json -Depth 10
    $bad | Set-Content "$root/duplicate.json"
    & $simulator --fleet-config "$root/duplicate.json" 1> "$root/duplicate.stdout.log" 2> "$root/duplicate.stderr.log"
    Check 'DuplicateIdRejectedBeforeInitialization' ($LASTEXITCODE -eq 1 -and (Get-Item "$root/duplicate.stdout.log").Length -eq 0 -and (Get-Content "$root/duplicate.stderr.log" -Raw) -match 'amrId')
    @{RunId=$RunId; BrokerPort=$port; Code=(git rev-parse HEAD); Started=[DateTimeOffset]::Now.ToString('o'); Results=$verifications; Passed=$true} | ConvertTo-Json -Depth 8 | Set-Content "$root/verification.json"
}
finally {
    if ($receiveProcess -and !$receiveProcess.HasExited) {
        # 소유한 수신 관찰 프로세스 정리다. 이 종료를 Ctrl+C 정상 종료 시험으로 집계하지 않는다.
        Stop-Process -Id $receiveProcess.Id -Force
        $receiveProcess.WaitForExit()
    }
    if ($container -match '^[a-f0-9]{64}$') {
        Docker @('logs',$container) | Set-Content "$root/broker.log"
        $null = Docker @('rm','--force',$container)
    }
    Docker @('ps','-a','--no-trunc','--format','{{json .}}') | Set-Content "$root/containers-after.jsonl"
    $verifications | ConvertTo-Json -Depth 5 | Set-Content "$root/checks.json"
}
