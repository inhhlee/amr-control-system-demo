# 한 AMR의 경유점 순차 이동·장애물 우회·MQTT 수신 지도 검증

- 실행자: Codex. 실행일: 2026-10-22 KST.
- 요청: 같은 AMR이 경유점을 순서대로 방문하다가 장애물이 생기면 최단 경로로 우회한다. 이동 좌표는 MQTT로 보내고 관제가 받은 좌표로 지도에 표시한다.
- 코드: `a8934f0bfed803cee47d7c2e13775ba648e0862a` + 이번 미커밋 변경, `feature/33-simulator-movement`. 앞선 로컬 작업을 보존하고 이어 구현했다. [파일 SHA256·환경·집계](source-version.json), [실행한 소스 80개 스냅샷](source.snapshot.zip).
- 단계: 로컬 구현·시험·실행 안내·오늘 기록 연결. 커밋·푸시·새 Issue·PR·병합은 미수행이며 최신 develop 반영과 구분한다.
- 최종 Git 점검에서 별도로 `AmrControlSystem.slnx`에 지도 시험 프로젝트 등록1줄이 추가된 것을 확인했다. 이번 편집 도구로 변경한 파일이 아니며 작성 주체는 확인하지 않았다. 되돌리지 않고 보존했고 이번 시험은 전용 프로젝트를 직접 지정해 실행했다.

## 1. 적용 기준과 완료조건

사용자의 이번 통합 요청과 로컬 원본 과제2 과제계획 v0.12, 기능명세 v0.9 AMR-02·05, 공통데이터명세 v0.6의 기존 Telemetry·Heartbeat 계약, 결정사항 v0.9 D-02·05·12를 참조했다. 더 최신 원격 문서·원문 PDF/HWP를 이번에 확인한 것으로 표시하지 않는다. 새 공식 기능 ID·Issue·추적기의 spec_revision은 부여하지 않고 이 결과와 소스 manifest로 추적한다.

이번 시연 기준은 기존 5×6 맵·상하좌우 비용1, **S (0,0) → W1 (2,3) → W2 (4,3) → G (4,5)**다. 순서 고정 경유점을 설정에서 읽고 현재 목표까지만 최단 경로를 계산한다. 뒤 순서의 경유점을 먼저 지나도 현재 목표를 건너뛰지 않는다. 현재 목표 칸이나 접근 경로가 막히면 같은 목표로 대기하고 제거 후 다시 이동한다. 모든 경유점을 방문한 최종 도착만 완료다.

위 경유점·격자 임무 형식은 이번 요청을 구체화한 시연 선택이다. 기존 제품 CLI의 연속 좌표 설정·State/Alarm·MQTT 메시지 계약은 유지했다. 지도 도구는 `config/simulator.grid.json`을 사용하며 기존 `simulator.single.json`을 자동 변환하지 않는다. 실제 건물 지도·운영 경유점 정책을 확정한 것이 아니다.

## 2. 변경 범위와 실행 환경

| 항목 | 내용 |
| --- | --- |
| 이동 모델 | `Navigation/GridNavigator.cs`: 순서 있는 목표 목록·현재 목표·방문 순서/이동 횟수. 기존 목표 하나인 시험 입력도 유지 |
| 설정 | 새 `GridMissionSettings.cs`·`config/simulator.grid.json`: AMR ID·출발점·순서 있는 경유점. 마지막 항목이 목적지. 필수/중복/추가 필드·정수·범위·벽·UTF-8 검사 |
| 실행 통합 | `Program.cs`에서 GridNavigationSession 하나만 생성. `/api/state`와 `/api/grid/state`, 두 start/cancel 주소 모두 같은 세션에 연결 |
| 화면 | 모드 선택과 별도 경유점 화면을 제거하고 W1·W2·G·현재 목표·목표까지 거리·경유 진행을 한 화면에 표시. 기존 `/?mode=grid`도 같은 화면 |
| MQTT | 설정 ID `AMR_001` 유지. 위치/흔적은 기존 관제 구독·검사·AMR별 큐를 통과한 Telemetry만 사용. 경로·경유 진행은 계산값으로 구분 |
| 환경 | Windows / SDK10.0.401 / net10.0 / 기존 MQTTnet·xUnit. 새 패키지 없음 |
| 이동·좌표 | 이동600ms, 1칸=1m, X=열·Y=-행. 명목상 이동 속도1/0.6m/s. 대기·도착 IDLE·속도0 |
| 실제 시험 | 소유 격리 Broker64497·서버64499, Telemetry200ms·Heartbeat1000ms. [환경 원본](../20261022-mission-mqtt-host-01/session.json) |
| 사용자 서버 | 기존 Broker127.0.0.1:1884·지도59344 재사용. 교체 전 [상태](before-restart.json) 저장·추가 장애물 [복원](ready-restored.json). 서버는 실행 상태로 인계 |
| 화면 확인 | 사용자가 같은 주소의 통합 W1·W2·G 화면이 보인다고 응답. 자동 브라우저의 시각 검사·새 스크린샷은 미실행이며 사용자 확인·HTTP 화면/상태 검증과 구분 |

관제 수신기는 지도 서버 프로세스 안에서 기존 통신 라이브러리를 사용한다. 구독·검사·큐를 거친 실제 MQTT 수신이며 별도 Core·DB·전체 관제 GUI의 완료가 아니다. 같은 Broker에서 같은 AMR ID로 다른 프로그램이 동시에 위치를 발행하지 않는 단일 시연 조건을 유지한다.

실행과 재검증:

```powershell
dotnet run --no-build --project tools/Amr.Simulator.Map -- --port 59344 --mqtt-host 127.0.0.1 --mqtt-port 1884 --telemetry-ms 200 --heartbeat-ms 1000
dotnet test tests/Amr.Simulator.Map.Tests/Amr.Simulator.Map.Tests.csproj
pwsh -NoProfile -File tests/manual/AMR01/Start-MqttMap.ps1 -RunId <새-호스트ID>
pwsh -NoProfile -File tests/manual/AMR01/Verify-GridMqttMap.ps1 -SessionRunId <새-호스트ID> -RunId <새-시험ID>
pwsh -NoProfile -File tests/manual/AMR01/Stop-MqttMap.ps1 -RunId <새-호스트ID>
```

## 3. 입력·예상·실제 결과·판정

| 구분 | 입력·예상 | 실제 결과·판정 | 증거 |
| --- | --- | --- | --- |
| 빌드·자동시험 | 기존55건과 순서/설정/단일 MQTT 임무24건 | 전용 프로젝트 빌드 및 최종79건 통과. 초기78건 뒤 0회 이동 MQTT 완료 시험1건을 추가한 재실행이며 합산하지 않음 | [최종 TRX](mission-tests-02.trx)·[로그](test-02.log), [첫 실행](mission-tests.trx) |
| 정상 순서 | W1→W2→G, 9회 이동, 마지막 정지 수신 | 방문 이동 횟수5→7→9, 같은 AMR_001, Completed·IDLE·속도0, 통과 | [기본 도착](../20261022-mission-mqtt-live-01/normal-arrival.json) |
| 이동 중 우회 | 이동 후 (2,2) 차단. W2를 지나도 W1 먼저 방문해야 함 | 9회 이동 시 W2 위치를 지나지만 방문0·현재 목표W1 유지, 통과 | [후순위 통과 미완료](../20261022-mission-mqtt-live-01/later-waypoint-crossed-not-credited.json) |
| 우회 순서 완료 | W1 방문 뒤 W2로 되돌아가 최종 도착 | W1/W2/G 방문11→13→15회, 차단 칸 통과0, 수신 흔적16칸, 최종 정지 수신 확인, 통과 | [우회 도착](../20261022-mission-mqtt-live-01/moving-detour.json) |
| 다음 경유점 차단 | 출발 전에 W2를 막음. W1은 방문하고 W2 순서를 유지하며 정지 | W1 위치·이동5·목표W2·Waiting 유지, 관제 IDLE·속도0 수신, 통과 | [차단 대기](../20261022-mission-mqtt-live-01/blocked-next-waypoint-keeps-order.json) |
| 목표 차단 해제 | W2 장애물 제거 후 Start 재요청 없이 이동 | 같은 순서로9회 도착·최종 정지 수신, 통과 | [자동 복귀](../20261022-mission-mqtt-live-01/waypoint-unblocked-resume.json) |
| 단일 세션·화면 | 기존/격자 API가 같은 AMR. 다른 시작 주소로 중복 실행 불가 | 같은ID·source, 중복 시작409, 응답 HTML에 경유 목록과 통합 화면만 존재, 통과 | [API·화면 확인](../20261022-mission-mqtt-live-01/single-session-api-and-screen.json) |
| MQTT 유지·오류 | 완전 차단 시 송신 유지·제거 후 복귀, 불량/타 AMR 격리, 취소/초기화 정리, 단절/최초 실패 구분 | 모두 통과. 단절 시 마지막 수신 위치 유지·추가 이동0, 최초 실패는 이동0·표시 위치 없음 | [15개 집계](../20261022-mission-mqtt-live-01/verification.json) |
| 송신↔관제↔지도 | API의 수신 좌표를 실제 관제 모델·발행 Payload와 대조 | 489개 상태 표본 대조 통과. 발행153·원시 수신154 중 추가1개는 반 칸 좌표 시험 입력으로 지도 투영에서 거부됨 | [대조 집계](../20261022-mission-mqtt-live-01/sender-receiver-api-equality.json)·[표본](../20261022-mission-mqtt-live-01/samples.jsonl) |
| 설정·경계 | 누락/중복/추가 필드, 잘못된 ID·좌표·벽·빈 경로·UTF-8 거부, BOM 허용·입력 복사·중복 경유점 | 관련 자동시험 통과. 현재 위치와 같은 연속 목표는 추가 이동 없이 순서대로 처리 | [최종 TRX](mission-tests-02.trx) |
| 정리 | 소유 시험 서버 정상 종료·Broker 제거·기존 컨테이너 유지 | 종료0·포트 닫힘·소유 Broker 제거·기존 Broker 유지, 통과 | [cleanup](../20261022-mission-mqtt-host-01/cleanup.json) |
| 사용자 실제 서버 | AMR_001·경유 순서·목적지 수신 | 방문5→7→9, 목적지(4,5), Telemetry29, Completed·최종 수신 정지 확인 | [인계 스냅샷](handoff-state.json) |

실제 원본은 [계산·발행·관제 수신 로그](../mission-live-20261022-093706-c6aadc9b44da4ca2aac887947b74a006/grid-navigation.jsonl)와 [Broker 로그](../20261022-mission-mqtt-host-01/broker.log)에 있다.

기존 제품 `src/`·Simulator151건·통신768건은 이번에 변경·재실행하지 않았다. 해당 과거 근거를 이번79건과 합산하지 않는다. JS 구문 검사·Git diff 공백 검사·소스 SHA256 검사도 통과했으며 제품 실행 시험과 구분한다.

## 4. 설계 판단과 대안

한 AMR의 이동 모델에 `NextWaypointIndex`를 두고 현재 순서의 목표만 탐색한다. 실제로 그 칸에 도착했을 때만 인덱스를 진행한다. 우회 중 다른 경유점 좌표를 지나도 순서 밖의 방문으로 완료 처리하지 않는다. 연속 중복 목표는 같은 위치에서 추가 이동 없이 진행한다. 미래 목표가 막혀 있어도 현재 목표를 먼저 방문하고, 그다음 목표가 된 시점에 대기한다.

계산·편집·경유 순서·수신 모델은 같은 세션 잠금으로 처리하고 MQTT I/O는 잠금 밖에서 기다린다. 기존 취소·실제 Task 종료·연결 정리·최종 수신 확인 책임을 유지했다. 지도 상태의 `position`·흔적은 수신 전용이고 경유 진행은 계산 결과다. 프로세스 안의 잠금이 여러 장비의 충돌이나 QoS0 유실을 해결하지 않는다.

현재처럼 격자 이동과 순서 있는 경유점을 하나로 합치면 같은 좌표·장애물 모델에서 현재 위치를 보존해 재탐색할 수 있다. 대안은 기존 연속 좌표 Simulator에 격자 우회 경로를 여러 직선 구간으로 전달하는 방식이다. 부드러운 이동·설정 속도를 재사용할 수 있지만 좌표 변환·이동 도중 경로 교체·취소 책임을 추가로 정해야 한다. 이번에는 구현·성능 비교하지 않았다.

탐색 한 번은 O(V log V)·O(V), 설정/방문 기록은 경유점 수에, 이동/수신 흔적은 이동 횟수에 비례한다. 작은30칸 시연에서 이해하기 쉬운 순서 보장을 선택했다. 대규모·장시간이라면 경로 재계산 빈도·흔적 보관 상한·네트워크 지연과 이동 분리를 검토해야 한다.

## 5. 예상 PR 질문

1. **우회가 왜15회인가?** W1 이전에 W2를 먼저 지나도 W1을 건너뛰지 않는다. W1 방문 후 W2로 되돌아가므로 목표 하나만 보던 이전11회 우회와 조건이 다르다. 실제 방문11→13→15와 후순위 통과 시 방문0을 확인했다.
2. **정말 같은 AMR·같은 실행인가?** 설정ID AMR_001과 GridNavigationSession 하나를 사용하고 모든 시작/상태 주소가 그 세션으로 들어간다. 다른 주소로 시작해도409이며, 세 경유 구간과 최종 Telemetry의 ID가 유지된다. 같은 ID의 외부 프로그램 동시 발행은 허용하지 않는 시연 조건이다.
3. **지도 표시가 계산 위치를 대신 쓰지는 않는가?** 위치·흔적은 관제의 검사·큐를 통과한 수신 모델만 사용한다. 수신 차단/최종 미수신 시험과 기존55건을 회귀했고, 실제489개 표본을 발행·수신 로그와 대조했다. 경유 진행은 MQTT ACK가 아닌 계산 상태로 명시한다.

## 6. 남은 범위와 인계

통합 화면은 사용자가 직접 표시를 확인했고 자동 시각 검증은 미실행이다. 실제 로봇·센서·제동·연속 이동·명령/ACK·Core/DB·여러 AMR의 충돌·대규모/장시간·실제망 복구는 남아 있다. 자동 재접속·재전송은 없다.

현재 결과는 코드 저장소의 로컬 통합 구현과 해당 시험 범위다. 최신 develop에 정식 반영할 때 해당 기능 Issue·브랜치·PR로 분리하고 공식 계획·명세 범위는 담당 원본에서 갱신한다. 과제2 전체·정식 반영 완료로 표시하지 않는다. [사용 안내](../../../tools/Amr.Simulator.Map/README.md), [격자 임무 설정](../../../config/simulator.grid.json).
