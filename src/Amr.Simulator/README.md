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

취소 토큰은 실행 중인 이동·발행을 중단한다. 실행 Task가 끝날 때까지 송신 Client 정리를 기다리며, 정리 중 재출발·초기화를 허용하지 않는다. `Cancel()`은 이동 상태를 취소하고, 호스트는 토큰 취소와 Task 대기도 함께 수행한다. 네트워크 I/O와 이벤트 알림은 이동 상태 잠금 밖에서 실행한다.

## 실행·시험

기존 콘솔 실행은 연속 좌표 설정을 사용한다.

```powershell
dotnet run --project src/Amr.Simulator -- --config config/simulator.single.json
dotnet test tests/Amr.Simulator.Tests
```

격자 화면은 `dotnet run --project tools/Amr.Simulator.Map`으로 실행한다. 이 호스트가 본 프로젝트의 `GridSimulation`을 호출한다. 격자 CLI나 별도 프로세스 간 명령 프로토콜을 새로 추가하지 않았다.
