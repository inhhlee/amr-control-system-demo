# 가상 AMR

가상 AMR의 이동·경유 순서·장애물 회피와 위치·상태 MQTT 송신을 담당한다. 지도 서버나 브라우저를 참조하지 않는다.

| 폴더 | 역할 |
| --- | --- |
| `Configuration` | 기존 연속 X/Y 이동용 JSON 설정과 검사 |
| `Simulation` | 연속 좌표 이동 계산, 스냅샷, 콘솔 실행 루프 |
| `Navigation` | 격자 최단 경로, 순서 있는 경유점, 장애물 변경, 이동 실행과 MQTT 발행 |
| `Telemetry` | 공통 위치·Heartbeat 메시지 생성과 MQTT 송신 전송 계층 |

## 격자 이동

- `GridPathfinder`: 상하좌우 비용 1의 다익스트라 최단 경로 계산.
- `GridNavigator`: 현재 위치·지도·경유 순서를 관리한다. 경로가 없으면 같은 목표를 유지하고 기다린다. 호출자는 갱신을 직렬화한다.
- `GridSimulation`: Navigator를 잠금으로 보호하고 이동 타이머·발행 주기·송신 Client의 연결부터 해제까지 소유한다. 이벤트는 잠금 밖에서 전달한다.
- `GridTelemetry`: 행·열을 X=열, Y=-행으로 변환하고 BUSY/IDLE 및 현재 속도를 만든다.

호스트는 `Start()` 후 `RunAsync(mqttOptions, cancellationToken)`를 한 번 호출하고 반환까지 기다린다. MQTT 옵션이 null이면 로컬 이동만 실행한다. MQTT를 사용할 때 반환하는 `GridSimulationCompletion`은 최종 정지 메시지의 발행 시각·좌표이며 관제 수신 확인은 아니다. 관제 수신 확인은 [Map 도구](../../tools/Amr.Simulator.Map/README.md)가 수행한다.

지도 호스트는 `RunSessionAsync`로 출발 전부터 통신 세션을 연다. `Start`/`Cancel`/`Reset`은 임무만 변경하고 Ready·Waiting·Completed·Canceled에서도 Heartbeat와 Telemetry를 보낸다. 최종 정지 발행은 `MissionCompleted`로 알리며 초기화 전의 완료 이벤트는 세대 번호로 구분한다. 유한 실행용 `RunAsync`는 기존대로 임무 종료 시 반환한다.

세션 취소 토큰은 이동·발행을 중단하고 실행 Task는 송신 Client 정리까지 기다린다. 네트워크 I/O와 이벤트 알림은 이동 상태 잠금 밖에서 실행한다. MQTT 주기 발행은 단일 루프이므로 발행 지연은 다음 이동·Heartbeat도 늦출 수 있으며 무한 병렬 송신을 추가하지 않았다.

## 실행·시험

기존 콘솔 실행은 연속 좌표 설정을 사용한다.

```powershell
dotnet run --project src/Amr.Simulator -- --config config/simulator.single.json
dotnet test tests/Amr.Simulator.Tests
```

## 수량·장애 조건 설정 (Issue #49)

`--fleet-config config/simulator.fleet.json`으로 여러 AMR의 설정을 한 번에 읽고 실행한다.
`amrs` 배열의 항목 수가 장비 수이며 ID는 각 항목에 직접 적는다. 같은 ID를 두 번
사용하거나 마지막 장비의 값이 잘못된 경우도 전체 실행 전에 거부한다. 기존
`--config config/simulator.single.json`과 지도 도구의 `--fleet-config`는 각각 별도 형식이다.

```powershell
dotnet run --project src/Amr.Simulator -- --fleet-config config/simulator.fleet.json
dotnet run --project src/Amr.Simulator -- --fleet-config config/simulator.fleet.json --mqtt --host 127.0.0.1 --port 1884 --telemetry-ms 100 --heartbeat-ms 1000
```

각 항목에는 기존 `amrId/initialPosition/speedMps/updateIntervalMs/waypoints`와
`faults` 배열을 적는다. 장애가 없으면 `faults: []`다. 장애 조건은 `code`,
`level`(기존 INFO/WARN/ERR/FATAL), `activateAfterMs`, `resolveAfterMs`, 선택
`message`다. 시점은 실행 후 경과 ms인 0 이상의 Int64 정수이며 원인 해소는 발생보다
뒤여야 한다. 문자열·중복/추가 필드·잘못된 UTF-8은 보정하지 않는다.

각 장비에 `mode: "route" | "command"`를 지정한다. 생략 시 route다. Fleet은 두 모드 모두
`AmrDeviceContext`의 공통 실행 루프로 이동·장애·알람·명령 응답을 처리한다. MQTT command는
초기 좌표에서 기다리고, route는 설정 경유점을 한 번 실행한 뒤에도 명령·생존 신호 처리를 유지한다.
로컬 route는 도착하고 남은 장애 이벤트가 없으면 종료한다. 예제 `simulator.fleet.json`의
AMR_002는 이제 300ms에 실제 FAULT가 발생하므로 로컬 실행은 Ctrl+C로 종료한다. RESET 시험에는 MQTT를 사용한다.

장비마다 상태 머신·큐·시계·CTS·ClientId/연결을 소유한다. 한 장비의 연결 실패·과부하는 해당
장비만 종료하고 정상 장비는 계속 실행한다. 전체 Ctrl+C 시 모든 정리를 기다린다.
단절·과부하 중 ACK 전달과 자동 재접속은 보장하지 않는다.

```bash
dotnet run --project src/Amr.Simulator -c FleetEngine -- --fleet-config config/simulator.fleet-control.json --mqtt
```

Fleet MQTT 기본값은 127.0.0.1:1884, Telemetry 200ms, Heartbeat 1000ms다.
[설정·Git Bash·MQTT Explorer·자동 시험 안내](../../tests/manual/FLEET-CONTROL/README.md)를 따른다.
기존 `Verify-FleetSettings.ps1`은 유한 경로 실행을 전제한 과거 시험이며 현재 MQTT 상시 실행 시험은
`tests/manual/FLEET-CONTROL/verify.ps1`을 사용한다. #50/#51 게시·리뷰·전체 완료 판정은 별도다.

격자 화면은 `dotnet run --project tools/Amr.Simulator.Map`으로 실행한다. 이 호스트가 본 프로젝트의 `GridSimulation`을 호출한다. 격자 CLI나 별도 프로세스 간 명령 프로토콜을 새로 추가하지 않았다.

## 명령을 기다리는 단일 AMR 실행 (#58·#51 일부)

```powershell
dotnet run --project src/Amr.Simulator -- --config config/simulator.single.json --command-mode --mqtt --host 127.0.0.1 --port 1884 --telemetry-ms 200 --heartbeat-ms 1000
```

자동 경로 모드와 달리 초기 좌표에서 IDLE로 기다린다. MOVE를 받으면 현재 위치에서 지정한 목적지로 직선 이동하고 도착 시 최종 SUCCESS를 보낸다. 이동 중 추가 MOVE는 거부한다. STOP은 현재 좌표에서 정지하고 원래 MOVE의 FAILURE/중단 사유와 별도 STOP의 SUCCESS를 보낸다. 새 MOVE는 멈춘 위치에서 다시 출발한다.

대기·도착·정지·장애에도 Telemetry/Heartbeat를 계속 보낸다. 같은 요청 ID/내용은 동작을 다시 실행하지 않고 기존 최종 ACK를 사용한다. Fleet의 장비별 `mode: "command"`도 같은 엔진을 사용한다. Broker가 끊기면 해당 AMR 실행은 종료하므로 복구 후 직접 재실행한다. [GUI와 함께 실행·검증](../../tests/manual/GUI-CONTROL/README.md).

## 단일 명령 모드의 장애·RESET (#50·#51 일부)

`--config config/simulator.fault-reset.json --command-mode --mqtt`로 장애 복구를 시험한다.
기존 단일 설정에 선택 `faults` 배열을 추가했다. 생략/빈 배열이면 이전 정상 명령 모드와 같다.
각 항목의 형식·검사기는 위 fleet 장애 설정과 공유한다. 단일 자동 경로 모드에서는
장애 설정을 조용히 무시하지 않고 실행을 거부한다.

- `FaultScenarioRunner`: 명령 Topic 구독 준비 후 Stopwatch를 시작한다. 시점은 이 기준의 경과 ms이며, 시계 변경에 영향받지 않는다. 호출이 늦어져도 장애 시점을 지나서 이동시키지 않는다. 도착과 발생이 정확히 같으면 도착을 먼저 확정한 뒤 장애를 적용한다.
- `CommandDrivenAmr`: `faults`의 항목은 작업을 중단시키는 장애 주입이다. 등급은 Alarm의 표시 등급이며, 일반 WARN만 발행하는 비중단 시나리오와 구분한다. 발생 시 FAULT·속도 0·ACTIVE 및 진행 MOVE의 `FAILURE / FAULT_INTERRUPTED`를 생성한다.
- 원인 해소 시 `FaultCauseResolved`를 기록하며 FAULT와 알람은 유지한다. 모든 발생 원인이 해소된 뒤 **새 ID의 RESET**을 받아야 `CLEARED → IDLE Telemetry → SUCCESS / RESET_COMPLETED ACK`를 발행한다. 현재 좌표를 유지하고 이전 MOVE를 자동 재개하지 않는다.
- 미해소 RESET은 `FAILURE / FAULT_CAUSE_ACTIVE`, BUSY RESET은 `FAILURE / BUSY_RESET_REJECTED`, 장애가 없는 상태는 `FAILURE / NOT_IN_FAULT`다. STOP은 장애 중에도 정지를 확인하지만 FAULT를 해제하지 않는다.
- 같은 알람 코드의 겹친 원인을 시나리오 인덱스로 따로 추적한다. ACTIVE 상태를 Tick마다 재발행하지 않으며, 같은 코드가 해제 후 다시 발생하면 새 ACTIVE를 생성한다.
- MQTT 콜백은 검사·bounded queue 접수만 한다. 상태·시나리오·명령은 단일 실행 루프가 소유한다. 큐 128, 명령 결과 기록 128(기존 STOP 예약 1자리), QoS0·Retain=false·세션 내 중복 억제 정책을 유지한다. 발행 지연은 다음 Tick을 늦출 수 있으며 하드 실시간 정지 보장은 아니다.

[Git Bash 실행 명령·GUI 조작·Pass/Fail·자동 E2E](../../tests/manual/FAULT-RESET/README.md)
