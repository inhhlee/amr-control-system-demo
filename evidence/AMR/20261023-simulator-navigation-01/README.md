# AMR 격자 이동의 Simulator 분리 검증

- 실행·작성: Codex, 2026-10-23 KST. 사용자가 승인한 기존 시연의 구조 변경과 실제 MQTT·GUI 회귀 검증.
- 코드: `codex/preserve-grid-map-20261023`, 기준 커밋 `7ae3e0473eb59376882de85f9c75adf2d3353804` + 이번 미커밋 변경. [전체 소스 SHA-256](source-hashes.json), [추적·신규 파일 변경](tracked-changes.patch).
- 환경: Windows 10.0.26200, .NET SDK 10.0.401, PowerShell 7.6.5, MQTTnet 5.2.0.1603. [확인값](environment.json).
- 적용 기준: 사용자 10/23 요청(이동·경유점·장애물 회피는 Simulator, Map은 지도·관제 수신), 현재 설정과 Map README. 기본 명세는 기능명세 v0.9 초안 AMR-01/02/05 일부, 공통데이터명세 v0.6 초안, 결정사항 v0.9 초안 D-02/05/12, 과제2 과제계획 v0.12 초안. 기본 계획의 연속 좌표 첫 실행본과 사용자 추가 격자 시연을 구분하며, 정식 과제 전체 완료로 집계하지 않는다.

## 변경과 책임

| 위치 | 변경 후 책임 |
| --- | --- |
| `src/Amr.Simulator/Navigation/GridNavigator.cs` | 지도·현재 위치·경유 순서·대기·장애물 변경 |
| `src/Amr.Simulator/Navigation/GridPathfinder.cs` | 다익스트라 최단 경로. 기존 계산을 namespace 변경과 함께 이동 |
| `src/Amr.Simulator/Navigation/GridSimulation.cs` | 상태 잠금, 이동 타이머, 발행 주기, 송신 Client 연결·발행·해제 |
| `src/Amr.Simulator/Navigation/GridTelemetry.cs` | 발행 좌표·속도·상태 변환 |
| `tools/Amr.Simulator.Map/Control/GridMapSession.cs` | Simulator 호출, 관제 수신·표시 상태, 최종 정지 메시지 수신 확인 |
| `tools/Amr.Simulator.Map/Control/GridControlConnection.cs` | 관제 수신 Client만 소유 |
| `tools/Amr.Simulator.Map/Control/GridMapCoordinates.cs` | 수신 좌표를 지도 칸으로 변환·범위 검사 |

Map의 기존 Navigation 폴더는 Control로 정리했다. 사용되지 않던 이전 `MapSession.cs`와 `app.js`를 제거하고, 현재 `grid.js`·설정 파일·HTTP 경로·MQTT Topic/Payload는 유지했다. 구판 세션만 생성하던 시험 1개는 제거했으며 동일한 현재 세션의 수신 전 위치 없음은 `GridMqttTests.ReadyHasNoReceivedPosition`과 실제 첫 화면으로 검증했다. 경로·경유 이동 시험은 Simulator 시험 프로젝트로 옮기고 Map 설정 시험은 Map에 남겼다.

## 실제 시험

| 구분 | 입력·예상 | 실제 결과 | 근거 |
| --- | --- | --- | --- |
| 빌드 | Map과 참조 Simulator 빌드 | 경고 0·오류 0 | [빌드](build.log), 최종 Control 이름의 빌드는 Map 시험 로그 |
| Simulator 표준 자동 시험 | 기존 이동·설정·송신 회귀, 격자 계산·순서, Map 없이 실행·우회, 송신 단절·취소 정리 | 197 통과, 실패/건너뜀 0 | [TRX](simulator-authorized.trx), [로그](simulator-tests-authorized.log) |
| Map 표준 자동 시험 | 수신 전/미수신 위치, 잘못된 메시지, 종료 대기, 최종 수신 확인, 설정 | 63 통과, 실패/건너뜀 0 | [TRX](map-authorized.trx), [로그](map-tests-authorized.log) |
| 실제 격리 Broker 연동 | 정상 9회, 이동 중 우회 15회, 후순위 경유점 선통과, 경유점 차단 대기·해제, 전면 차단·재개, 불량 입력, 취소·초기화, Broker 단절·최초 실패, 송신/수신/API 일치 | 15개 검증 항목 통과 | [실제 MQTT 판정](../refactor-mqtt-20261023/verification.json), [관찰값](../refactor-mqtt-20261023/samples.jsonl), [실행 환경](../refactor-host-20261023/session.json) |
| 실제 브라우저 + 기존 Broker | 출발 후 이동1회에 (2,2) 추가, W1→W2→G, 수신 좌표와 표시 일치 | 15회 이동 = 독립 BFS 최소15. W2 첫 통과9회는 완료 아님. W1/W2/G를 11/13/15회에 방문. 위치47·Heartbeat10·거부0. 최종 X5/Y-4→행4/열5, IDLE·속도0 | [GUI 판정](../20261023-refactor-ui-02/verification.json), [최종 화면](../20261023-refactor-ui-02/final-clean.png), [DOM](../20261023-refactor-ui-02/dom-samples.json), [원본 송수신](../20261023-refactor-ui-02/run-events.jsonl) |

자동 시험 260건과 실제 MQTT 15개 검증 항목, 브라우저 실행1개를 구분한다. API 459회·DOM 4개 관찰값은 시험 건수에 추가하지 않는다. 브라우저의 검증 대상 이동은 10:49:27~10:49:37 KST이며 DOM의 수신 좌표·현재 AMR 칸·이동 흔적을 동일 실행의 발행/수신 로그와 대조했다.

MQTT 시험은 Mosquitto `eclipse-mosquitto:2.1.2-alpine`의 별도 소유 컨테이너(호스트 포트61500)를 사용했다. 시험 후 해당 컨테이너와 서버만 정리했다. 사용자 지도는 새 코드로 `http://127.0.0.1:59344/?mode=grid`에서 계속 실행 중이며 기존 Broker 1884를 사용한다. 이미지 ID·PID·시각은 각 환경 기록에 있다.

실행 명령:

```powershell
dotnet test tests/Amr.Simulator.Tests/Amr.Simulator.Tests.csproj --no-restore
dotnet test tests/Amr.Simulator.Map.Tests/Amr.Simulator.Map.Tests.csproj --no-restore
pwsh -NoProfile -File tests/manual/AMR01/Start-MqttMap.ps1 -RunId refactor-host-20261023
pwsh -NoProfile -File tests/manual/AMR01/Verify-GridMqttMap.ps1 -SessionRunId refactor-host-20261023 -RunId refactor-mqtt-20261023
pwsh -NoProfile -File tests/manual/AMR01/Stop-MqttMap.ps1 -RunId refactor-host-20261023
```

재실행 시 새 RunId를 사용한다. 이번 표준 시험은 단일 빌드 노드·빌드 서버 미사용 옵션과 TRX 출력 위치를 추가했다. GUI는 버튼 클릭과 DOM·화면을 관찰했으며 API 스크립트만의 시험으로 대체하지 않았다.

## 실패·제한 기록

- 첫 자동 시험은 제한 환경에서 VSTest가 testhost에 90초 내 연결하지 못해 중단됐다. [Simulator 초기 로그](simulator-tests.log)·[Map 초기 로그](map-tests.log)·초기 TRX를 보존했다. 제한 밖의 표준 시험으로 다시 실행해 통과했으며 시험을 생략하거나 기대값을 완화하지 않았다.
- [첫 GUI 실행](../20261023-refactor-ui-01/automation-note.txt)은 범위가 넓은 `1회` 선택자가 경로 계산 횟수를 가리켜 첫 이동 전 장애물을 추가했다. 제품은 15회 도착했지만 이동1회 후 추가의 근거로 사용하지 않았다. `#grid-moves`로 범위를 한정한 새 실행 `20261023-refactor-ui-02`가 요청 조건을 통과했다.
- 기존 통신 라이브러리 소스는 바꾸지 않았다. 통신 전체 자동 시험, Command/ACK, 실제 로봇, 다중 AMR·장시간 운용은 이번에 실행하지 않았다.
- 커밋·푸시·PR·병합은 하지 않았다. 기존 미추적 증거와 사용자 파일은 보존했다.

## 설계 판단과 검토 질문

같은 프로세스에서 Simulator API를 호출하되 송신·수신 MQTT Client를 분리하는 방식을 선택했다. 별도 프로세스로 나누는 대안은 장애 격리에는 유리하지만 시작·취소·장애물 명령용 프로세스 간 계약과 배포 관리가 추가된다. 이번 폴더·책임 분리에는 필요하지 않아 구현하지 않았다. 다익스트라 계산은 유지하며 성능·메모리 벤치마크를 새로 수행하지 않았다. 프로세스 격리나 여러 실행 호스트가 필요할 때 별도 서비스 구성을 검토한다.

1. **Simulator만으로 실행 가능한가?** `GridSimulationTests`가 Map 프로젝트 참조 없이 로컬 이동과 발행 전송 대역을 사용해 우회·순서·정지를 검증한다. 실제 MQTT 송신은 Map 호스트에서 같은 Simulator와 실제 전송 계층을 호출해 검증했다. 새 격자 CLI는 추가하지 않았다.
2. **최종 발행 성공을 도착 수신 성공으로 오인하지 않는가?** Simulator는 발행 시각·좌표만 반환한다. Map은 동일 시각·좌표·IDLE·속도0의 관제 수신을 확인하고 수신 연결 정리가 끝나야 완료를 표시한다. QoS0 발행만으로 전달을 보장하지 않는다.
3. **잠금·취소·종료 책임은 어디에 있는가?** Simulator 잠금은 이동·지도 변경, Map 잠금은 수신·표시 상태를 보호한다. Simulator 이벤트는 잠금 밖에서 호출해 역순 잠금 대기를 피한다. Map이 실행 전체 취소 토큰과 Task 대기를 소유하고 Simulator는 송신 Client, Map은 수신 Client의 정리를 소유한다. 취소 요청만으로 종료 처리하지 않으며 정리 중 재출발을 거부하는 자동·실제 시험이 통과했다.
