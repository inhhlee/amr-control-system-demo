# 과제2 위치·상태를 보내고 관제에서 받기

책임님께는 이렇게 설명한다.

> “시뮬레이터가 실제로 움직이면서 위치와 상태를 보냈고, 관제 프로그램이 같은 값을 받았습니다. 마지막에는 목적지 (1, 2), 정지 상태 IDLE, 속도 0을 받았습니다.”

이번 확인 경로는 **시뮬레이터 → 시험용 MQTT Broker → 기존 관제 수신 콘솔**이다. 현재 2D 지도는 기존 직접 연결 방식을 유지한다. 관제가 받은 좌표를 지도에 연결하는 작업은 다음 단계다.

## 1. 저장된 이번 결과부터 보기

PowerShell에서 실행한다. 아래 명령은 저장된 결과를 보여 주며 새 시뮬레이션을 실행하지 않는다.

```powershell
Set-Location C:\amr-control-system-demo
$received = Get-Content .\evidence\AMR\20261021-mqtt-live-02\default.received.json -Raw | ConvertFrom-Json
$received | Where-Object Kind -eq telemetry | ForEach-Object {
    [pscustomobject]@{
        AMR = $_.AmrId
        X = $_.Model.Position.X
        Y = $_.Model.Position.Y
        '속도(m/s)' = $_.Model.Speed
        상태 = $_.Model.State
    }
} | Format-Table -AutoSize
```

처음 `(-1, 0) · IDLE · 속도 0`, 중간 `BUSY · 속도 1`, 마지막 `(1, 2) · IDLE · 속도 0`을 보여 준다. 수신 좌표는 실제 이동 로그에 있는 값과 대조해 확인했다.

- [실제 관제 수신 원본](../../../evidence/AMR/20261021-mqtt-live-02/receiver.stdout.log)
- [실제 시뮬레이터 실행·송신 원본](../../../evidence/AMR/20261021-mqtt-live-02/default.stdout.log)
- [최종 결과 문서](../../../evidence/AMR/20261021-mqtt-01/README.md)

## 2. 새로 실행해서 확인하기

Docker Desktop이 실행 중이고 `eclipse-mosquitto:2.1.2-alpine` 이미지가 있어야 한다. 스크립트는 이미지를 자동 설치하거나 기존 Broker를 사용하지 않는다. `RunId`는 매번 새 이름을 쓴다.

```powershell
Set-Location C:\amr-control-system-demo
dotnet build tests/Amr.Simulator.Tests
dotnet build src/Amr.Communication.Console
pwsh -NoProfile -File .\tests\manual\AMR01\Verify-MqttTelemetry.ps1 -RunId my-mqtt-demo-01
```

마지막에 `PASS: actual Simulator -> isolated Broker -> validated control receiver`가 나오면 통과다. 이 실행은 정상 경로뿐 아니라 설정 변경·취소·Broker 종료·접속 실패까지 확인한다. **Broker 종료 시험의 종료 코드 1은 예상 결과**이며 정상 이동 종료 코드 0과 구분한다. 원본은 `evidence/AMR/my-mqtt-demo-01/`에 남는다. 시험용 Broker와 프로세스는 종료 후 정리된다.

## 3. 두 PowerShell 창에서 직접 시연하기

아래는 새 격리 Broker를 켜 놓고 직접 실행하는 순서다. 다른 창에서 실행 중인 자동 검증과 섞지 않는다. 기존 1883·1884 포트는 사용하지 않는다.

첫 번째 창에서 Broker를 만들고 관제를 켠다.

```powershell
Set-Location C:\amr-control-system-demo
$docker = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'
$brokerConfig = (Resolve-Path .\evidence\AMR\20261021-mqtt-live-02\broker.conf).Path
$demoName = 'amr-show-' + [guid]::NewGuid().ToString('N')
$brokerId = & $docker run -d --pull never --name $demoName --label "amr.manual.owner=$demoName" --publish '127.0.0.1::1883' --tmpfs /mosquitto/data --tmpfs /mosquitto/log --mount "type=bind,source=$brokerConfig,target=/mosquitto/config/mosquitto.conf,readonly" eclipse-mosquitto:2.1.2-alpine
if ($LASTEXITCODE -ne 0 -or $brokerId -notmatch '^[a-f0-9]{64}$') { throw 'Broker 생성 실패' }
$mapping = & $docker port $brokerId 1883/tcp
$demoPort = [int]($mapping -replace '^127\.0\.0\.1:', '')
Write-Host "두 번째 창에서 사용할 포트: $demoPort"
dotnet run --no-build --project .\src\Amr.Communication.Console -- --host 127.0.0.1 --port $demoPort --show-payload
```

`Subscribed`와 `WaitingForShutdown`이 나오면 준비됐다. 두 번째 PowerShell 창에서 **아래 `<표시된 포트>`를 첫 창의 숫자로 바꿔** 실행한다.

```powershell
Set-Location C:\amr-control-system-demo
dotnet run --no-build --project .\src\Amr.Simulator -- --config .\config\simulator.single.json --mqtt --host 127.0.0.1 --port <표시된 포트> --telemetry-ms 200 --heartbeat-ms 1000
$LASTEXITCODE
```

두 번째 창에는 이동·도착과 `TelemetryPublished`, 첫 번째 창에는 `Validation=Accepted`와 `Received`가 나온다. 마지막 `Received` 중 Telemetry의 `Position={X:1,Y:2}`, `Speed=0`, `State=IDLE`을 확인한다. 송신 창의 `Published`만으로 관제 수신을 판정하지 않는다.

끝나면 첫 번째 창에서 **Ctrl+C**로 관제를 종료하고, 같은 창에서 이번 Broker만 정리한다.

```powershell
$owner = & $docker inspect $brokerId --format '{{index .Config.Labels "amr.manual.owner"}}'
if ($owner -ne $demoName) { throw '이번에 만든 Broker가 아닙니다.' }
& $docker rm -f -v $brokerId
```

## 동작 범위

- `--mqtt`를 생략하면 기존 독립 이동 명령과 2D 시연 도구의 실행 흐름을 유지한다.
- MQTT 모드에는 호스트·포트·Telemetry 주기·Heartbeat 주기를 모두 명시한다. 200ms·1000ms는 이번 시연값이며 운영 기본값으로 확정한 값이 아니다. 위치 계산의 `updateIntervalMs`와 별개다.
- 출발 위치를 먼저 보내고 주기적으로 최신 계산 값을 보낸 뒤 도착 상태를 마지막으로 보낸다. 이동이 끝나면 송신도 끝나고 연결을 정리한다. 도착 후 계속 살아 있는 장비 서비스는 이번 범위가 아니다.
- QoS 0, Retain=false, MQTT 5, 새 Client ID·CleanStart·SessionExpiry=0. 발행 반환은 전송 측 결과이며 수신 보장·재시도·자동 재접속은 제공하지 않는다.
- 이번 수신 대상은 기존 관제 **콘솔의 검사·AMR별 큐·수신 처리**다. Core·DB 저장, MQTT 좌표를 지도에 표시하기, 명령/ACK·다중 AMR·전체 장애 복구는 후속이다.
