# 과제2 결과 — AMR 한 대의 출발부터 도착·정지까지

**설정한 출발 위치에서 경유점을 지나 목적지에 도착하고 멈추는 시나리오를 실제 실행으로 확인했다.**

책임님께 드릴 설명:

> 가상 AMR 한 대가 (-1, 0)에서 출발해서 (1, 0)을 지나 (1, 2)에 도착하는 시연입니다. 이동 중에는 속도 1로 움직이고, 도착하면 속도 0으로 멈춥니다. 설정 파일에서 출발 위치·속도·경로를 바꿔도 같은 프로그램으로 동작하는 것을 확인했습니다. 다음 단계는 이 위치 정보를 관제 프로그램으로 보내는 작업입니다.

[직접 입력할 명령과 시연 순서](../../../tests/manual/AMR01/README.md) · [기본 경로의 실제 로그](../20261021-runner-console-03/normal.stdout.log)

## 실제로 확인한 결과

| 보여드릴 내용 | 실제 결과 | 근거 |
| --- | --- | --- |
| 출발 준비 | AMR_001, (-1, 0), IDLE, 현재 속도 0 | [기본 실행](../20261021-runner-console-03/normal.stdout.log) |
| 이동·경유점 통과 | 좌표가 변하고 BUSY·속도 1, (1, 0) 통과 관찰 2.038초 | 같은 로그의 AmrMoved·WaypointReached |
| 도착·정지 | (1, 2), IDLE·속도 0, 관찰 4.020초, 종료 코드 0 | [실제 실행 판정](../20261021-runner-console-03/verification.json) |
| 설정만 바꿔 재실행 | (5, 3) 출발 → (5, 4) → (7, 4), 속도 2, 1.565초 뒤 정상 종료 | [변경 설정](../20261021-runner-console-03/changed.json) · [로그](../20261021-runner-console-03/changed.stdout.log) |
| 잘못된 설정 거부 | 속도 0 입력을 이동 시작 전에 거부, 이유 출력·종료 코드 1 | [오류 로그](../20261021-runner-console-03/invalid.stderr.log) |
| 이동 없는 경로 | 모든 점이 출발 위치와 같으면 BUSY 없이 완료 | [로그](../20261021-runner-console-03/stationary.stdout.log) |
| 이동 중 사용자 취소 | SimulationCanceled, 완료 표시 없음, 실제 프로세스 종료 0. 신호 후 종료 관찰 16.9517ms | [취소 로그](../20261021-runner-console-03/cancel.stdout.log) · [판정](../20261021-runner-console-03/cancel.result.json) |
| 매우 긴 갱신 간격 중 취소 | Int64 최대 ms 입력에도 대기 오류 없이 취소·종료 0, 관찰 19.9365ms | [판정](../20261021-runner-console-03/long-interval.result.json) |

취소는 Windows CTRL_C_EVENT를 제품 프로세스의 격리 콘솔에 보낸 실제 OS 신호 시험이다. 사용자가 키보드로 직접 누른 기록과 구분한다. 취소 로그의 좌표·속도·상태는 마지막 계산값이며 STOP 명령 수행은 아니다. 위 시간은 이번 PC의 관찰값이며 고정 시간 보장이 아니다.

## 빌드·자동시험과 적용 기준

- 실행자: Codex. Windows 10.0.26200, .NET SDK 10.0.401, net10.0. 패키지는 기존 xUnit 2.9.3·Test.Sdk 17.14.1을 유지했다.
- 코드: `71160f412624f829f8b5673c409da5a4accf202c` + 이번 미커밋 변경. 브랜치 `feature/33-simulator-movement`. [현재 소스·설정 해시](source-sha256.json), [실행 환경·DLL 해시](../20261021-runner-console-03/verification.json).
- 기준: 과제2 과제계획 v0.12 초안, 기능명세 v0.9 AMR-01·AMR-02 일부, 공통데이터명세 v0.6 §4, 결정사항 v0.9 D-02·D-05·D-12 및 10/21 승인 보완. 원본은 C:/process-plan. Issue #34의 게시 당시 문서 버전·Hello World 설명보다 현재 코드와 위 승인 보완을 적용했다.
- [#34](https://github.com/inhhlee/amr-control-system-demo/issues/34)는 #33 PR 병합 후 feature/34-simulator-runner 착수를 전제로 한다. 조회 시 [PR #36](https://github.com/inhhlee/amr-control-system-demo/pull/36)은 open·미병합이다. 이번 사용자의 명시적인 구현·실행 요청에 따라 현재 체크아웃에서 로컬 연결 검증만 수행했다. 브랜치 전환·커밋·푸시·PR 수정·병합·Issue 완료 처리는 하지 않았다. 정식 반영은 선행 PR 반영 확인 후 별도 단계다.

| 검증 종류 | 명령·입력 | 예상 | 실제·판정 |
| --- | --- | --- | --- |
| 전용 빌드 | dotnet build tests/Amr.Simulator.Tests/Amr.Simulator.Tests.csproj --no-restore -m:1 -nr:false -p:UseSharedCompilation=false | 빌드 성공 | [최종 로그](build-final.log): 경고 0·오류 0, 통과 |
| 표준 자동시험 | dotnet test tests/Amr.Simulator.Tests/Amr.Simulator.Tests.csproj --no-build --no-restore | 설정·이동·루프 관련 회귀 성공 | [TRX](runner.trx) · [로그](tests.log): 135건 통과, 실패·건너뜀 0. 기존 129건에 신규 루프 6건 포함 |
| 실제 콘솔 시나리오 | pwsh -NoProfile -File tests/manual/AMR01/Verify-Run.ps1 -RunId 20261021-runner-console-03 | 위 6개 별도 프로세스 성공 | 20:54:41~20:54:49 KST, 6건 통과. [판정](../20261021-runner-console-03/verification.json) |
| 기존 설정 콘솔 회귀 | pwsh -NoProfile -File tests/manual/AMR01/Verify-Settings.ps1 -RunId 20261021-runner-settings-01 | 정상·변경·BOM 정상 3건 및 잘못된 입력 12건 | [판정](../20261021-runner-settings-01/verification.json): 15건 통과. 정상 입력은 이번 코드에서 이동 완료까지 실행 |

자동시험 135건과 실제 시나리오 6건·설정 회귀 15건은 서로 다른 검증 종류다. 전체 솔루션·기존 통신 768건은 이번에 재실행하지 않았다. 통신 제품 코드는 변경하지 않았다.

## 완료조건과 근거 연결

| #28 원래 조건 | AMR2 시험 ID | 이번 근거 |
| --- | --- | --- |
| 1. 독립 실행·정상 종료 | 실제 도착·취소 | normal/cancel/long-interval 프로세스 로그·종료 코드 |
| 2. 설정 읽기·초기 생성 | F01 | normal 초기 출력, 설정 자동시험 및 실제 15건 회귀 |
| 3. 초기 좌표·속도·경로 변경 | F02 | changed 설정·초기/이동/완료 로그 |
| 4. 오류 설정 시작 전 거부 | F05 | invalid 및 설정 회귀 12개 오류 입력 |
| 5. 시간·경유점 진행·도착 후 유지 | F03·F04 | 이번 TRX의 기존 MovementTests 41건, 실제 정상 이동, 신규 루프의 실제 경과 시간 주입 시험 |
| 6. 시험·규칙·버전·한계 연결 | F01~05 | 이 문서, 소스 해시, TRX, 실제 입력·원본 로그·판정 |

#34의 7개 구현 완료조건은 위 근거에 연결했다. 설정 간격 대기와 단조 시계는 ISimulationClock/SystemSimulationClock, 순차 실행·실제 종료는 SimulationRunner 및 Ctrl+C 프로세스 시험, 최종 회귀·설정 변경·오류 처리는 위 결과에 대응한다. 온라인 체크박스와 PR 상태는 변경하지 않았다.

## 구현 방식과 검토할 점

`SimulationRunner` 한 곳에서 대기 → 실제 경과 시간 측정 → `Advance` → 로그를 차례대로 수행한다. `Stopwatch` 단조 시각으로 구간 시간을 측정하므로 PC 시각 변경을 이동 시간으로 쓰지 않는다. 이전 갱신 뒤 출력에 걸린 시간도 다음 갱신에 포함한다. 한 번에 여러 경유점을 지나면 통과 이벤트를 순서대로 기록한다. 이벤트의 ObservedElapsedSeconds는 통과를 관찰한 갱신 시각이며 정확한 물리적 통과 시각은 아니다.

별도 Task·스레드·큐·타이머 이벤트를 생성하지 않고 대기를 await하므로 위치 갱신은 겹치지 않는다. Program의 기존 Ctrl+C 구독을 재사용하고, 루프 반환 후 finally에서 구독을 해제하고 CancellationTokenSource를 정리한다. Int64 갱신 간격은 Task.Delay의 제한에 맞춰 나누어 기다리므로 기존 설정 계약을 축소하지 않는다. 메모리는 현재 AMR과 설정 경로·한 번의 대기를 유지하며 로그를 제품 내부에 누적하지 않는다.

대안인 PeriodicTimer는 고정된 주기 일정에 맞춰 깨우기에 편리하지만 현재 한 대의 순차 실행에는 취소 가능한 Delay가 단순하다. 지금 방식은 출력 시간이 다음 대기에 더해져 실제 갱신 간격이 늘 수 있으며, 이동량은 실제 경과 시간으로 보정한다. 다중 AMR·고정 주기 성능 요구나 느린 로그 출력이 생기면 스케줄링·출력 분리를 다시 검토해야 한다. 이번에는 실시간성·부하·장시간 성능을 검증하지 않았다.

예상 리뷰 질문:

- **왜 설정 간격을 그대로 이동 시간에 넣지 않는가?** 깨어나는 시간이 항상 정확하지 않아 실제 경과 시간을 사용한다. TestSimulationClock으로 100ms 설정에 2.5초가 지난 경우의 중간 좌표·도착을 검증했다.
- **Ctrl+C 뒤에도 갱신이 남지 않는가?** 대기를 취소하고 반환을 기다리는 단일 루프다. 기다린 직후 토큰을 확인하며 자동시험과 실제 프로세스 종료를 확인했다. 이미 수행 중인 동기 계산·출력은 즉시 강제 중단하지 않는다.
- **취소 로그의 BUSY는 정지 실패인가?** 마지막 계산값이다. 취소는 시뮬레이터 실행 종료이며, 목적지 도착·STOP 명령 수행과 별도로 표시한다. 명령 정지·재개 정책은 후속 범위다.

## 실패 이력과 남은 일

- 최초 빌드는 기존 obj 폴더 접근 제한으로 실패했다([로그](build.log)). 승인된 권한으로 다시 빌드했고 자동시험의 xUnit 권고 1건도 수정해 최종 경고·오류 0을 확인했다. [중간 빌드](build-authorized.log)는 보존했다.
- [콘솔 01](../20261021-runner-console-01/)은 시험 도구가 아직 빈 출력 파일을 읽어 실패했다. [콘솔 02](../20261021-runner-console-02/)는 비대화형 부모의 Ctrl+C 무시 플래그가 자식에 상속돼 취소 시험이 시간 초과했다. 도구에서 자식 생성 전에 해당 플래그를 해제한 뒤 콘솔 03에서 통과했다. 두 실패 실행의 소유 프로세스는 finally에서 종료했다. 이 수정으로 제품 코드를 바꾸지 않았다.
- 기존 설정 시험의 변경 경로는 약 10.25초 걸린다. 초기화만 하던 때의 10초 제한을 이동 실행에 맞춰 20초로 조정하고 정상 사례에 SimulationCompleted 검사도 추가했다. 제품 오류를 숨기기 위한 도착 조건 완화는 하지 않았다.
- Git 인덱스·HEAD·브랜치와 기존 솔루션·PR 초안 변경은 유지했다. [보존 확인](preservation.json)에서 시작 때 해시와 비교했다. 작업 중 별도 작업으로 보이는 기존 DEMO 문서·화면·생성기 4개와 루트 README의 안내 문구가 바뀌어 해당 파일의 바이트 동일성은 성립하지 않는다. 이번 작업에서는 이 4개 파일을 수정하지 않았고 최신 내용을 그대로 유지했다. 루트 README에도 새 과제2 절만 삽입했으며 바뀐 기존 안내 문구를 되돌리지 않았다.
- **남은 기능:** MQTT Heartbeat/Telemetry 송신, 명령 수신·MOVE/STOP/RESET·ACK, 다중 AMR·장애·Alarm, GUI·Core·DB 연동. 이번 결과는 단일 AMR 첫 실행본이며 과제2 전체 완료가 아니다.
- **남은 절차:** #33 PR 리뷰·병합 확인, #34 정식 브랜치 정리·리뷰·커밋·푸시·PR. 이번 요청대로 커밋·푸시는 하지 않았다.


## 2026-11-01 보관 위치 정리

이 실행의 정상 중간 결과/일회성 도구 1개는 [정리 목록](../../curation/20261101.json)에 원래 경로·해시·고정 Git 링크를 기록하고 작업 폴더에서 제외했다. 당시 수치·실패/미검증 판정은 원문 그대로이며 원본 전체는 [정리 전 고정 버전](https://github.com/inhhlee/amr-control-system-demo/tree/ae71f452f27a65ec2fee68fee60496a7a5ca3d6c/evidence/AMR/20261021-runner-01)에서 확인한다. 과거 manifest의 파일 목록·해시는 당시 스냅샷이며 현재 폴더 전체 검사 입력으로 사용하지 않는다. 당시 도구를 재현할 때는 해당 고정 버전의 자료를 사용한다.
