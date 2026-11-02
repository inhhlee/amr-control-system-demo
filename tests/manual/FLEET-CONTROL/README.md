# Fleet 명령·장애·응답 시험

GUI·Core 없이 `Amr.Simulator` 독립 프로세스와 Mosquitto, MQTT Explorer만 사용한다. 기준은 2026-11-02 사용자 결정(장비별 독립 실행, 원인 해소 후 명시적 RESET)이다. 현재 GUI의 다중 장비 바인딩 완료를 의미하지 않는다.

## 1. Git Bash에서 실행

저장소 루트에서 다음을 실행한다. 이미 실행 중인 Broker는 재사용한다.

```bash
cd /c/amr-control-system-demo
test -f .env || cp .env.example .env
docker compose --env-file .env -f docker/compose.yaml up -d mosquitto
docker compose --env-file .env -f docker/compose.yaml ps mosquitto
dotnet run --project src/Amr.Simulator -c FleetEngine -- \
  --fleet-config config/simulator.fleet-control.json --mqtt
```

Fleet의 `--mqtt` 기본값은 `127.0.0.1:1884`, Telemetry **200ms**, Heartbeat **1,000ms**다. 변경 시 `--host`, `--port`, `--telemetry-ms`, `--heartbeat-ms`를 붙인다. `dotnet run`의 프로젝트 옵션 뒤 **`--`**가 프로그램 인자 구분자다.

`CommandSessionReady AmrId=AMR_01`, `AMR_02` 두 줄과 초기 IDLE 메시지를 확인한다. 장애 시계는 각 장비의 MQTT 구독 준비 완료 뒤 시작한다. 준비 직후 아래 명령을 보낸다. 시간이 부족하면 종료 후 다시 실행하거나 JSON의 발생/해소 시점을 늘린다.

## 2. MQTT Explorer에서 직접 확인

접속 `127.0.0.1:1884`, 구독 `amr/#`, 발행 **QoS 0 / Retain 해제 / JSON**을 사용한다. 장비별 Topic을 정확히 선택한다. 현재 최종 상태는 매번 새로 수신된 Telemetry와 대조한다.

| 순서 | 조작 | 즉시 확인할 결과 |
| --- | --- | --- |
| 1 | 두 장비의 telemetry/heartbeat 관찰 | AMR_01=(4,2), AMR_02=(0,5), IDLE·속도0·Heartbeat OK |
| 2 | 아래 MOVE를 각각 발행 | 장비별 BUSY·설정 속도·좌표 증가. 같은 cmdId도 장비 ID별로 분리됨 |
| 3 | AMR_02에 STOP 발행 | AMR_02=STOPPED·속도0. M-01에 FAILURE/STOP_INTERRUPTED, S-01에 SUCCESS/STOPPED. AMR_01은 계속 이동 |
| 4 | AMR_02에 새 ID `M-02`로 MOVE 재발행 | AMR_02 이동 재개 |
| 5 | AMR_01 시작 후 30초까지 관찰 | AMR_01=FAULT·속도0·Alarm ACTIVE, M-01에 FAILURE/FAULT_INTERRUPTED. AMR_02 좌표·Heartbeat 계속 갱신 |
| 6 | 30~60초 사이 AMR_01에 R-01 RESET | FAILURE/FAULT_CAUSE_ACTIVE. FAULT·ACTIVE 유지 |
| 7 | `FaultCauseResolved AmrId=AMR_01` 확인 | 원인만 해소됨. FAULT·ACTIVE·정지 좌표 유지 |
| 8 | **새 ID R-02**로 RESET | CLEARED, 같은 좌표에서 IDLE, SUCCESS/RESET_COMPLETED. 기존 MOVE는 재개되지 않음 |
| 9 | 새 ID M-03으로 가까운 목표 MOVE | BUSY 이후 도착 좌표·IDLE·속도0 및 동일 ID SUCCESS/ARRIVED |
| 10 | 터미널 Ctrl+C | 두 장비 `CommandSessionEnded ExitCode=0`, `FleetEnded ExitCode=0` |

AMR_01 Topic **`amr/AMR_01/command`**:

```json
{"cmdId":"M-01","type":"MOVE","ts":1,"param":{"x":1000,"y":2}}
```

AMR_02 Topic **`amr/AMR_02/command`**:

```json
{"cmdId":"M-01","type":"MOVE","ts":1,"param":{"x":1000,"y":5}}
```

AMR_02 STOP, AMR_01 RESET는 각각 해당 장비의 command Topic에 보낸다.

```json
{"cmdId":"S-01","type":"STOP","ts":1,"param":{}}
```

```json
{"cmdId":"R-01","type":"RESET","ts":1,"param":{}}
```

`ts:1`은 계약상 유효한 시험 입력 예시다. 재시험·새 동작에는 새 cmdId를 쓴다. 같은 ID와 같은 내용은 기록된 최종 응답을 재사용하며, 해소 전 실패한 R-01을 재전송해도 새 RESET으로 처리하지 않는다. ACK 대조 키는 **(AMR ID, cmdId)**, 알람 대조 키는 **(AMR ID, code)**다.

**실패 기준:** 잘못된 장비가 움직임, 원인 해소만으로 알람 해제, RESET 시 좌표 이동, 도착 전 SUCCESS, 다른 장비의 FAULT/연결 종료로 정상 장비까지 중단, 정상 연결에서 최종 ACK 누락. 마지막 항목은 cmdId와 실제 수신 로그를 함께 보존한다.

## 3. 설정과 실행 구조

`amrs` 항목 수가 장비 수다. 중복 ID·잘못된 마지막 장비도 네트워크 연결 전에 전체 거부한다.

| JSON 필드 | 적용 |
| --- | --- |
| `amrId` | 고유 장비 ID, Topic·payload에 동일하게 적용 |
| `mode` | `route` / `command`, 생략 시 기존 `route` |
| `initialPosition`, `speedMps`, `updateIntervalMs` | 초기 X/Y, 양수 m/s, 양수 Int64 ms |
| `waypoints` | 필수 1개 이상. route는 시작 시 순서대로 한 번 실행, command는 MOVE 목적지 사용 |
| `faults` | 필수 배열. `[]`는 장애 없음 |
| `faults[].activateAfterMs`, `resolveAfterMs` | 구독 준비 후 단조 시계 경과 ms, 0 이상 Int64, 해소 > 발생 |
| `faults[].code`, `level`, `message` | 알람 코드·INFO/WARN/ERR/FATAL·선택 설명. 모든 fault 항목은 이동 중단 장애 |

모드를 섞어 실행할 수 있다. MQTT route도 MOVE/STOP/RESET을 받으며 BUSY MOVE는 거부한다. 경로 도착 자체는 외부 명령이 아니므로 가짜 ACK를 만들지 않는다. STOP/FAULT 이후 초기 경로를 자동 재개하지 않는다. 로컬 `--fleet-config`는 route만 허용하며 도착하고 남은 장애 시나리오가 없으면 종료한다. 로컬 FAULT는 원인 해소 후에도 유지되므로 Ctrl+C로 끝낸다. 복구 시험은 MQTT를 사용한다.

`Program → FleetApplication → 장비별 AmrDeviceContext → CommandDrivenAmr + FaultScenarioRunner + 전용 Channel + MQTT 세션`.

- 장비마다 Task, 상태·명령 결과 기록, 큐, 단조 시계 기준점, CTS, ClientId/연결을 소유한다.
- MQTT 콜백은 검사·큐 적재만 수행한다. 도메인 상태는 해당 장비의 루프에서만 변경한다.
- 연결 실패·큐 초과·발행 실패는 해당 장비를 종료하고 형제 장비는 유지한다. 마지막 종료 시 실패가 한 번이라도 있으면 프로세스 종료 코드 1이다. 자동 재접속은 없다.
- Ctrl+C는 모든 장비에 취소를 전달하고 콜백 해제 → 구독 해제 → 연결 종료·Dispose → 큐 정리까지 기다린다. 발행·구독 해제 제한은 각각 3초다.
- 큐 용량 128, 한 tick 최대 명령 처리 128, 장비별 요청 기록 128(STOP용 마지막 한 자리 예약). 세션 내 기록이며 영속 저장·만료 제거는 없다.
- QoS0 / Retain=false다. 정상 수신한 최종 ACK와 단절·과부하 상황의 전달 보장은 구분한다. 큐 초과 시 미처리 명령 ACK, 세션 종료 중 ACK 도착을 보장하지 않는다.

장비별 연결은 실패 격리·수명 관리가 단순하지만 장비 수만큼 연결 자원이 필요하다. ThreadPool·CPU·콘솔·Broker는 공유하므로 하드 실시간 또는 무제한 Fleet 성능 보장이 아니다. 단일 연결 다중 Topic 방식은 연결 수를 줄이는 대신 전송 실패의 영향 범위와 라우팅 수명을 함께 관리해야 한다. 성능 측정 후 필요할 때 검토한다.

## 4. 자동 검증

```bash
dotnet test tests/Amr.Simulator.Tests -c FleetEngine
pwsh -NoProfile -File tests/manual/FLEET-CONTROL/verify.ps1
```

스크립트는 GUI/Core를 실행하지 않는다. 캐시된 Mosquitto 이미지와 실행 중인 Docker Linux 엔진을 사용하고, 임시 loopback 포트의 시험용 Broker만 만든다. 명령 모드 2대+경로 모드 1대, 명령/ACK 10쌍, fault/RESET, 정상 Ctrl+C, 특정 장비 ClientId 중복 연결을 통한 단일 단절·형제 장비 생존까지 검사한다. 생성한 컨테이너·프로세스만 정리하며 기존 서비스를 유지한다. `-Evidence <새 폴더>`로 근거 위치를 지정한다.

자동 대역 시험은 큐 초과·동시 콜백·지연 송신·장비별 시계·종료 정리를 추가 확인한다. 대역 시험을 실제 Broker 과부하 성능 시험으로 집계하지 않는다. [실행 근거](../../../evidence/AMR/20261102-fleet-engine-01/README.md).
