# 하나의 관제 GUI · 공통 맵에서 Fleet 제어

GUI-01~05·CORE-04와 독립 Fleet 실행을 연결한다. trial과 격자 웹 호스트는 필요하지 않다. 기존 단일 장비 `--amr-id`와 인자 없는 Mock 실행은 유지한다.

## Git Bash 실행

Docker Desktop을 실행한 뒤 저장소 루트에서 Broker를 준비한다.

```bash
cd /c/amr-control-system-demo
test -f .env || cp .env.example .env
docker compose --env-file .env -f docker/compose.yaml up -d mosquitto
docker compose --env-file .env -f docker/compose.yaml ps mosquitto
```

GUI를 **한 번만** 실행한다. 수신 전에는 등록 2대·위치 수신 0대로 표시하며 마커를 만들지 않는다.

```bash
dotnet run --project src/Amr.ControlGui -c FleetEngine --no-launch-profile -- \
  --mqtt --host 127.0.0.1 --port 1884 \
  --fleet-config config/simulator.fleet-control.json
```

다른 Git Bash에서 Simulator를 실행한다.

```bash
cd /c/amr-control-system-demo
dotnet run --project src/Amr.Simulator -c FleetEngine -- \
  --fleet-config config/simulator.fleet-control.json --mqtt
```

`CommandSessionReady`가 AMR_01·AMR_02 각각 출력되면 동일 지도와 목록에 두 대가 나타난다. 초기 수신 좌표는 (4,2)/(0,5), 속도0·IDLE다. 설정에서 좌표를 만들어 표시하지 않는다.
GUI의 `--fleet-config`는 `--amr-id`/`--simulator-config`와 동시에 사용할 수 없다. 중복 옵션·중복/잘못된 ID·잘못된 파일은 시작 오류다. GUI는 ID와 기본 목적지(마지막 경유점)를 읽고, 이동·장애 실행은 독립 Simulator가 담당한다.

## 조작과 통과 조건

공통 맵 마커 또는 하단 목록에서 장비를 선택한다. 오른쪽 **선택 AMR 제어 · 공통 맵**에서 대상 ID와 X/Y(m)를 확인하고 **시작 / STOP / RESET**을 누른다. 장비별 별도 실행창은 열지 않는다.

| 단계 | 입력 | 예상 결과 |
| --- | --- | --- |
| 1 | 두 장비 수신 대기 | GUI/MapCanvas 각 하나, 위치 수신2·생존 확인2, ID별 마커/목록 |
| 2 | 01에 (100,2), 02에 (100,5) 시작 | 각 대상 BUSY·좌표/속도 갱신, 명령·목적지 분리 |
| 3 | 두 대 이동 중 01 STOP | 01만 정지, STOP 성공·원 MOVE FAILURE/STOP_INTERRUPTED. 02 이동 지속 |
| 4 | 01 다시 시작 후 장애 시점 | 01 속도0·FAULT·Alarm ACTIVE·원 MOVE FAILURE/FAULT_INTERRUPTED. 02 이동/통신 지속 |
| 5 | 원인 미해소 중 01 RESET | FAILURE/FAULT_CAUSE_ACTIVE, 현재 좌표·FAULT·알람 유지 |
| 6 | 원인 해소 시각까지 대기 | 자동 복구 없이 FAULT·알람 유지 |
| 7 | 01 새 RESET | SUCCESS/RESET_COMPLETED·CLEARED·현재 좌표에서 IDLE. 원 MOVE 자동 재개 없음 |
| 8 | 가까운 목적지로 시작 후 다른 장비 선택 | 원 대상 SUCCESS/ARRIVED·목적 좌표·속도0·IDLE. 다른 선택의 응답에 섞이지 않음 |
| 9 | GUI X 버튼, 이후 Simulator Ctrl+C | GUI 프로세스/모든 소유 연결 종료. Simulator는 GUI 종료와 독립적이며 Ctrl+C로 별도 종료 |

기본 파일의 01 장애는 **구독 준비 30초 후 발생 / 60초 후 원인 해소**다. 시험 시간을 확보하려면 JSON 복사본의 `activateAfterMs`/`resolveAfterMs`를 늘리고 두 실행에 같은 파일을 지정한다. MOVE 시각부터 세는 타이머가 아니다. [실제 시험 복사본](../../../evidence/GUI/20261102-fleet-map-01/live/fleet.json)은 180000/360000ms다.

수신/생존이 확인되지 않은 장비는 제어하지 못한다. 다른 장비의 정상 수신과 별개로 판단한다. 연결 단절에는 마지막 위치를 보존하며 현재 위치로 확정하지 않는다. GUI의 기존 재접속은5초 간격·최대5회다. Simulator 자동 재접속이나 명령 재전송을 뜻하지 않는다.
명령 결과 대기는 기본30초, 긴 시험에는 `--command-timeout-seconds 900`을 지정한다. 기한 경과와 실제 수행 실패는 구분한다.

## 자동시험·실제 MQTT 근거

```bash
dotnet test tests/Amr.ControlGui.Tests -c FleetEngine
dotnet test tests/Amr.Core.Tests -c FleetEngine
```

자동시험은 MQTT 대역의 대상/ACK/알람/연결 격리와 WinForms 바인딩·종료를 확인한다. 독립 프로세스·실제 Broker·화면 조작은 [별도 실행 기록](../../../evidence/GUI/20261102-fleet-map-01/README.md)에서 판정한다.
MQTT Explorer 또는 mosquitto_sub로 command/telemetry/heartbeat/alarm/ack를 관찰하고 **AmrId+cmdId** 최종 ACK와 좌표/상태를 대조한다. 발행 반환만으로 완료를 판정하지 않는다. QoS0/Retain=false를 유지하며 단절·과부하 중 무손실 전달 보장은 없다.

## 선택한 구조와 한계

FleetMqttAmrDataProvider가 단일 MqttAmrDataProvider를 장비별로 소유한다. 고유 ClientId·명령 추적·목적지·Heartbeat·재접속·알람은 독립이며 화면에 하나의 Revision과 **모든 장비의 전체 스냅샷**을 전달한다. 따라서 MainForm의 목록 교체 때 다른 장비가 사라지지 않는다.
AmrChange.Connection은 전체 요약, DeviceConnections는 장비별 연결/신선도다. GUI는 InvokeAsync로 UI 스레드에서만 갱신한다. 폼이 자기 작업·구독 종료를 기다리고 Program이 공급자의 모든 자식 연결·집계 작업을 정리한다.

대안인 단일 MQTT Client+장비 사전은 연결 수를 줄이지만 기존 세션의 라우팅·재접속 수명을 함께 바꾸게 된다. 이번에는 검증된 공급자 재사용을 선택했다. 연결 수와 전체 스냅샷 비용은 장비 수에 따라 증가하며 대규모 처리량·메모리는 미검증이다. 경로 탐색·교통 제어·영속 DB는 별도다.
