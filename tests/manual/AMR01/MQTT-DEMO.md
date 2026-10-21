# Simulator MQTT 송신과 기존 관제 수신 (#38)

단일 AMR이 실제로 계산한 위치·속도·운행 상태를 MQTT로 보내고 기존 관제 콘솔이 검사·수신한다. MQTT 옵션을 생략하면 기존 독립 이동·도착 실행을 유지한다.

## 빌드와 직접 실행

저장소 루트에서 기존 의존성을 복원·빌드한다.

```powershell
Set-Location C:\amr-control-system-demo
dotnet restore AmrControlSystem.slnx
dotnet build AmrControlSystem.slnx --no-restore
```

준비된 Broker의 호스트·포트로 관제 콘솔을 먼저 실행한다. 아래 1884는 예시이므로 실제 Broker에 맞춘다.

```powershell
dotnet run --no-build --project src/Amr.Communication.Console -- --host 127.0.0.1 --port 1884 --show-payload
```

별도 창에서 같은 Broker로 Simulator를 실행한다.

```powershell
dotnet run --no-build --project src/Amr.Simulator -- --config config/simulator.single.json --mqtt --host 127.0.0.1 --port 1884 --telemetry-ms 200 --heartbeat-ms 1000
```

기본 경로는 (-1,0)→(1,0)→(1,2), 속도1m/s다. 최종 관제 Received의 AMR_001·(1,2)·IDLE·Speed=0을 확인한다. 송신 Published 반환만으로 실제 수신·동작 완료를 판정하지 않는다. 시연 뒤 관제 창에서 Ctrl+C로 종료한다.

## 격리 Broker로 실제 검증

Windows·PowerShell7·실행 중인 Docker 엔진과 로컬 eclipse-mosquitto:2.1.2-alpine 이미지가 필요하다. 도구는 이미지를 자동으로 내려받지 않는다. Docker는 PATH에서 찾으며 필요하면 -DockerPath <docker.exe 절대 경로>를 지정한다.

```powershell
pwsh -NoProfile -File tests/manual/AMR01/Verify-MqttTelemetry.ps1 -RunId my-mqtt-001
```

매번 새 RunId를 지정한다. 도구는 loopback 임의 포트·실행 소유 라벨·임시 파일 시스템의 Broker를 만들고, 종료 시 자신이 만든 프로세스·Broker만 정리한다. 기존 컨테이너의 ID·실행 상태 보존을 확인한다. Docker 접근 권한이 있는 실행 환경에서 사용한다.

정상/변경/처음부터 도착, Retain 잔존 없음, 긴 위치 갱신 대기 중 독립 송신·Ctrl+C, Broker 단절, 최초 접속 실패를 검증한다. 단절·접속 실패의 종료1은 예상 결과이며 정상 도착·사용자 취소의 종료0과 구분한다. 마지막 PASS와 evidence/AMR/<RunId>/verification.json을 확인한다. 원본 입력·로그를 보존하며 실행 ID를 덮어쓰지 않는다.

## 계약과 한계

- MQTT 모드에는 host·port·telemetry-ms·heartbeat-ms를 모두 명시한다. 주기는 양수 정수 ms이며 위치 계산의 updateIntervalMs와 별개다. 200/1000ms는 시험값이다.
- 불변 위치 스냅샷 하나를 주기 송신 작업이 읽는다. 출발·주기·최종 도착을 보내고 이동·송신 작업을 기다려 연결을 정리한다. 도착 후 계속 살아 있는 장비 서비스는 이번 범위가 아니다.
- MQTT5·QoS0·Retain=false·새 Client ID·CleanStart·SessionExpiry=0. 재시도·자동 재접속·전달 보장은 추가하지 않는다. 발행 반환도 Delivery=Unconfirmed이며 취소/실패 도중 이미 전송됐을 수 있어 Delivery=Unknown이다.
- 최초 접속 실패는 이동 시작 없이 종료1, 연결 후 단절·발행 실패·정리 실패도 종료1이다. 사용자 취소는 도착과 다른 로그 및 종료0이다. 최종 발행 완료 후 늦은 취소는 완료 원인을 덮어쓰지 않는다.
- 보존 구현의 개발용 연결·발행 제한은 유지한다. Core 버퍼 상한·명령 결과/공급자 종료 Timeout 수치를 이번에 확정한 것이 아니다.
- MQTT 수신 지도·격자 탐색·Core/DB·명령/ACK·다중 AMR은 후속이다. [보존 MQTT 근거](https://github.com/inhhlee/amr-control-system-demo/tree/a8934f0bfed803cee47d7c2e13775ba648e0862a/evidence/AMR/20261021-mqtt-01)와 [이번 분리·회귀](../../../evidence/AMR/20261022-mqtt-split-01/README.md)를 구분한다.
