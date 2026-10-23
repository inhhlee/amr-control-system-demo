# 격자 장애물 회피 · 현재 위치 재탐색 검증

- 실행자: Codex. 실행일: 2026-10-22 KST. 저장소: `C:\amr-control-system-demo`.
- 코드 기준: `a8934f0bfed803cee47d7c2e13775ba648e0862a` + 이번 미커밋 변경. 브랜치 `feature/33-simulator-movement`. 정확한 소스 SHA256·환경·시험 수는 [source-version.json](source-version.json).
- 사용자 요청: 제공한 C# 다익스트라와 5×6 맵을 기존 2D 화면에 연결해, 장애물을 추가·제거하면 현재 위치에서 다시 최단 경로를 찾아 이동한다.
- 이번 결과는 로컬 시연 확장이다. 커밋·푸시·Issue 생성·PR·병합은 실행하지 않았다. 기존 #33 완료조건이나 원격 develop의 상태를 변경하지 않는다.

## 1. 목표와 적용 기준

0=통로, 1=벽, 0부터 시작하는 (행, 열), 상하좌우 단위 비용 1을 적용한다. 시작 (0,0), 목적지 (4,5), 고정 벽은 사용자 맵 그대로다. 추가 장애물은 빈 칸 클릭으로 변경하고 현재 위치·출발점·목적지는 보호한다. 길이 없으면 대기하고 길이 열리면 자동 재출발한다. 대기·자동 재출발은 이번 지속 탐색 요청을 구체화한 시연 설계다.

읽은 원본은 `C:\process-plan`의 과제2 과제계획 **v0.12 초안**, 기능명세 **v0.9 AMR-01·02**, 공통규칙 **v0.6 C-02**, 공통데이터명세 **v0.6**, 결정사항 **v0.9 D-02·05·12**다. 이번에 읽은 로컬 버전이며 GitHub #38이 참조하는 더 최신 문서를 확인한 것으로 표시하지 않는다. 기존 계약은 연속 좌표의 경유점 이동이므로 이번 격자를 별도 모드로 추가했다. 제품 직선 이동·MQTT 계약·운행 State/Alarm 정책은 유지한다. 책임님 원문 PDF·미제공 HWP는 이번에 재대조하지 않았다.

AMR-02 이동 계산의 확장 시연이며 새 공식 기능 ID·Issue는 부여하지 않았다. 이 도구에는 `spec_revision`·`verification_index` 추적기가 없어 결과 문서와 소스 manifest로 연결한다. 계획·명세 원본을 코드 저장소에 복제하지 않았다.

## 2. 실행 환경과 절차

- Windows / SDK 10.0.401 / net10.0. 기존 지도 시험 프로젝트와 xUnit 패키지 재사용, 새 패키지 없음.
- C# 서버 `tools/Amr.Simulator.Map`, 기존 화면에 `/?mode=grid` 추가. 격자 이동 간격 600ms, 화면 조회 간격 100ms.
- 실제 HTTP 서버 `http://127.0.0.1:59344`. [전체 지도·경로·이동 로그](../map-live-20261022-090536-464cc32b91a142c58cb614a07ad08336/grid-navigation.jsonl)는 이후 사용자 조작도 추가된다. 이번 자동 HTTP 시험 결과는 별도 JSON으로 보존했다.
- 최초 제한 환경 서버 53497은 소켓 접근 거부로 화면 연결에 실패했다. [첫 서버 입력·로그](../map-live-20261022-090335-45d93e5c60fa4593b92164d2086f672e/)를 보존하고 해당 서버·중단된 시험을 종료한 뒤 허용된 환경에서 재실행했다. 중단된 시험은 통과로 집계하지 않는다.
- 최초 복원의 NU1900(취약성 정보 서버 접근 불가)은 제한 밖 복원 재실행에서 경고 없이 완료됐다. 지도 빌드는 경고 0·오류 0이다.
- 브라우저에서 클릭 변경·대기·제거 후 재출발·모드 선택을 직접 확인했다. 이후 사용자 조작이 확인되어 추가 초기화·이동 조작을 중단하고 현재 상태를 보존했다.

```powershell
dotnet build tools/Amr.Simulator.Map --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test tests/Amr.Simulator.Map.Tests/Amr.Simulator.Map.Tests.csproj --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --logger 'trx;LogFileName=grid-tests.trx' --results-directory evidence/AMR/20261022-grid-01
dotnet test tests/Amr.Simulator.Tests/Amr.Simulator.Tests.csproj --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --logger 'trx;LogFileName=simulator-regression.trx' --results-directory evidence/AMR/20261022-grid-01
pwsh -NoProfile -File tests/manual/AMR01/Verify-GridNavigation.ps1 -BaseUrl http://127.0.0.1:59344 -RunId 20261022-grid-live-01
```

## 3. 입력·예상·실제 결과와 판정

| 구분 | 입력·예상 | 실제 결과 | 판정·근거 |
| --- | --- | --- | --- |
| 신규 자동시험 | 잘못된 맵·좌표·벽·동일점·도달 불가·이동·재탐색·중단·초기화 | 신규 27건 통과. 300개 지도와 BFS 대조는 그중 1건 | 통과 [TRX](grid-tests.trx) |
| 기존 지도 자동시험 | MQTT 수신 모델의 기존 투영 결과 유지 | 9건 통과. 실제 Broker 시험과 구분 | 통과 [TRX](grid-tests.trx) |
| 기존 Simulator 자동시험 | 설정·이동·루프·MQTT 대역 시험 유지 | 151건 통과 | 통과 [TRX](simulator-regression.trx) |
| 실제 HTTP 기본 | 같은 맵에서 최단 거리 9, 도착 (4,5) | 9회 이동, Completed, 남은 거리 0 | 통과 [도착](../20261022-grid-live-01/original-arrival.json) |
| 실제 HTTP 이동 중 추가 | 이동 시작 후 (2,2)를 막고 해당 칸을 피해서 아래로 우회 | 11회 이동, 해당 칸 통과 0, Completed | 통과 [우회](../20261022-grid-live-01/moving-detour.json) |
| 실제 HTTP 완전 차단 | (0,1) 이동 후 (1,1)를 막고 1.3초 뒤에도 정지 | Waiting, 거리 null, 이동 1, 위치 (0,1) 유지 | 통과 [대기](../20261022-grid-live-01/blocked-waiting.json) |
| 실제 HTTP 자동 복귀 | 장애물 제거 후 Start를 다시 보내지 않음 | 자동 재출발, 9회 이동, Completed | 통과 [자동 재출발](../20261022-grid-live-01/automatic-resume.json) |
| 실제 HTTP 중단 | Cancel 뒤 800ms 동안 위치·이동 수 유지 | Canceled, 이동 수 유지 | 통과 [중단](../20261022-grid-live-01/cancel.json) |
| 실제 HTTP 초기화 | 원래 맵·출발점·거리 9 복원 | Ready, 이동 0, 거리 9 | 통과 [초기화](../20261022-grid-live-01/reset.json) |
| 실제 HTTP 입력·API | 헤더 없는 변경 403, 잘못된 편집 5종 400, 기존 API 유지 | 예상과 일치 | 통과 [10개 시나리오 집계](../20261022-grid-live-01/verification.json) |
| 브라우저 | 같은 벽 배치·클릭 변경·대기·제거 후 이동·기존 화면 확인 | 직접 확인. 사용자 추가 조작은 자동 시험 수에 합산하지 않음 | 통과 [대기 화면](ui-waiting.png) · [복수 장애물 화면](ui-blocked-multiple.png) · [마지막 저장 화면](ui-current.png) |

## 4. 설계 판단과 예상 PR 질문

`Navigation/GridPathfinder.cs`는 제시된 다익스트라와 같으며 같은 거리에서는 이웃 방문 순서를 유지한다. `GridNavigator.cs`는 현재 위치부터 한 칸만 이동하고 경로가 없으면 Waiting으로 전환한다. `GridNavigationSession.cs`의 같은 잠금에서 지도 편집·재탐색·이동·스냅샷 읽기를 처리한다. 편집이 먼저 확정된 벽으로 그 이후 이동이 들어가지 않으며 현재 칸의 장애물 추가는 거부한다. 이 잠금은 단일 서버 프로세스·세션 안의 보장이고 여러 프로세스·실제 장비 사이의 배제를 보장하지 않는다. 타이머·취소 요청·실제 Task 종료 대기·로그 해제는 세션이 소유한다. 도착·명시적 중단 뒤에는 이동하지 않고 자동 복귀는 Waiting 상태에만 적용한다.

현실적인 대안은 BFS다. 같은 이동 비용이면 힙 없이 O(V)로 계산하고 메모리는 둘 다 O(V)라 구현을 유지하기 쉽다. 이번에는 사용자 알고리즘을 적용해 탐색 한 번 O(V log V)·O(V)이며 30칸을 이동 때마다 다시 계산한다. 큰 지도라면 변경 시에만 재탐색하거나 A*를 검토할 수 있지만 성능 비교는 미실행이다. 비용을 확장할 때는 음수가 아닌 비용과 거리 표시·실제 이동 시간을 함께 정의해야 한다.

1. **기존 Simulator에 바로 넣지 않은 이유는?** 기존 계약은 m 단위 연속 좌표·경유점이고 이번 입력은 격자 행·열이다. 별도 모드로 연결해 기존 계약을 보존했다. 제품 통합 전 셀 크기·좌표 대응·MQTT 송신·정지 상태 계약이 필요하다.
2. **장애물 추가와 이동의 충돌은?** 같은 잠금으로 편집과 이동 직전 탐색을 순서대로 실행하고 현재 칸 편집을 거부한다. 브라우저가 이전 상태여도 서버에서 재검사한다. 실제 센서 지연·제동 거리는 이 잠금으로 해결되지 않는다.
3. **항상 최단 거리로 도착하는가?** 계산 당시의 지도와 현재 위치부터는 최단이다. 이미 이동한 거리나 미래 장애물 변화를 거슬러 최적화하지 않는다. 실제 결과는 기본 9회·중간 변경 11회이며 장애물이 계속 변할 때 도착을 보장하지 않는다.

## 5. 남은 검증과 반영 단계

별도 격자 모드의 로컬 구현·자동시험·실제 HTTP·브라우저 확인은 통과했다. 실제 MQTT 경로 송신, 건물 지도, 로봇 크기·충돌 여유, 센서·제동, 명령/ACK, Core/DB, 다중 AMR, 대규모·장시간 운영, 서버 종료의 별도 프로세스 시험은 미검증이다. 기존 통신 768건·전체 솔루션·실제 Broker는 이번에 다시 실행하지 않았다. 기존 경유점 화면은 표시와 상태 API를 확인했고 이동 전체를 브라우저에서 다시 실행하지 않았다.

최신 develop에 반영할 때 현재 보존 브랜치의 변경을 해당 기능 Issue·작업 브랜치·PR로 분리해야 한다. 공식 계획·명세의 범위 변경은 담당 원본에서 처리한다. 과제2 전체 완료·develop 반영 완료로 표시하지 않는다.
