$ErrorActionPreference = 'Stop'
$repo = 'C:/amr-control-system-demo'
$root = Join-Path $repo 'evidence/GUI/20261102-fleet-map-01/live'
if (Test-Path $root) { throw 'Use a new evidence folder.' }
New-Item -ItemType Directory -Path $root | Out-Null
$docker = 'C:/Users/Admin/AppData/Local/Programs/DockerDesktop/resources/bin/docker.exe'
$container = 'amr-fleet-gui-check-' + [guid]::NewGuid().ToString('N')
& $docker ps --format '{{.Names}} {{.Ports}}' | Set-Content "$root/containers-before.log"
& $docker run --rm -d --pull never --name $container -p '127.0.0.1::1883' --mount "type=bind,source=$repo/tests/manual/FAULT-RESET/mosquitto.conf,target=/mosquitto/config/mosquitto.conf,readonly" eclipse-mosquitto:2.1.2-alpine | Set-Content "$root/container-id.txt"
if ($LASTEXITCODE -ne 0) { throw 'Broker launch failed' }
$mapping = & $docker port $container 1883/tcp
if ($mapping -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Unexpected port' }
$port = [int]$Matches[1]
$config = Get-Content "$repo/config/simulator.fleet-control.json" -Raw | ConvertFrom-Json
# GUI 입력·화면 관찰 시간을 확보한 시험용 복사 설정. 제품 설정은 수정하지 않는다.
$config.amrs[0].faults[0].activateAfterMs = 180000
$config.amrs[0].faults[0].resolveAfterMs = 360000
$config.amrs[0].waypoints[0].x = 1000
$config.amrs[1].waypoints[0].x = 1000
$config | ConvertTo-Json -Depth 8 | Set-Content "$root/fleet.json"
$observer = Start-Process $docker -ArgumentList @('exec',$container,'mosquitto_sub','-h','127.0.0.1','-p','1883','-V','mqttv5','-t','amr/#','-v','-W','1200') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$root/mqtt.log" -RedirectStandardError "$root/mqtt.err"
$guiArgs = @('--mqtt','--host','127.0.0.1','--port',"$port",'--fleet-config',"$root/fleet.json",'--command-timeout-seconds','900')
$gui = Start-Process "$repo/src/Amr.ControlGui/bin/FleetEngine/net10.0-windows/Amr.ControlGui.exe" -ArgumentList $guiArgs -WorkingDirectory $repo -PassThru
@{container=$container;port=$port;guiId=$gui.Id;guiStartTicks=$gui.StartTime.ToUniversalTime().Ticks;observerId=$observer.Id;observerStartTicks=$observer.StartTime.ToUniversalTime().Ticks;guiArguments=$guiArgs;started=[DateTimeOffset]::Now.ToString('o')} | ConvertTo-Json -Depth 5 | Set-Content "$root/owned.json"
Get-Content "$root/owned.json"
