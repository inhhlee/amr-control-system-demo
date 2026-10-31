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

이 단계는 **장애 설정 로드·검사까지**다. 로그에 `Execution=PendingAlarmImplementation`을
표시하며 실제 장애/알람 전이는 #50, MQTT 명령 수신과 최종 ACK는 #51에서 연결한다.
원인 해소가 즉시 알람 해제를 뜻하는지는 아직 확정하지 않았다.

장비별 실행 루프·불변 송신 스냅샷·ClientId/연결을 따로 소유한다. 한 연결 실패 시
이번 fleet 실행은 실패로 종료하고 다른 소유 작업도 취소·정리를 기다린다. 한 장비가
먼저 도착하면 그 장비의 유한 실행/송신은 끝나며 마지막 장비 종료까지 프로세스가
기다린다. 장시간 명령 대기 세션은 이 단계의 기능이 아니다.

실제 MQTT 재현은 `pwsh -NoProfile -File tests/manual/AMR01/Verify-FleetSettings.ps1 -RunId <새ID>`를
사용한다. Debug 빌드된 Simulator/통신 콘솔과 실행 중인 Docker Linux 엔진, 캐시된
`eclipse-mosquitto:2.1.2-alpine`이 필요하다. 관찰용 수신 프로세스의 강제 정리를
Ctrl+C 정상 종료 검증으로 집계하지 않는다.

격자 화면은 `dotnet run --project tools/Amr.Simulator.Map`으로 실행한다. 이 호스트가 본 프로젝트의 `GridSimulation`을 호출한다. 격자 CLI나 별도 프로세스 간 명령 프로토콜을 새로 추가하지 않았다.
