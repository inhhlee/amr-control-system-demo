# #33 이동 계산·상태 전이·도착 정지 — 20261021-movement-01

## 1. 목표와 적용 기준

- 수행자: Codex AI 구현·시험 보조. 범위는 구현·시험까지이며 제품 커밋·푸시·PR 생성·Issue 종료는 수행하지 않는다.
- 대상: [Issue #33](https://github.com/inhhlee/amr-control-system-demo/issues/33), 과제2 / AMR-02 일부 / AMR2-F03·F04. 설정·초기화 AMR-01·F01·F02 초기화 부분·F05도 회귀 검증한다.
- 선행: [PR #35](https://github.com/inhhlee/amr-control-system-demo/pull/35)의 develop 병합 커밋 `3257f5ee88041cbc03827256b155f5e481c7aed0`. #32는 실제 GitHub에서 closed/completed다.
- 적용 원본: C:\process-plan의 과제2 과제계획 **v0.12 초안**, 기능명세 **v0.9 초안 AMR-02**, 공통데이터명세 **v0.6 초안 §4.2·4.3**, 결정사항 **v0.9 초안 D-02·D-05·D-12·10/21 경과 시간 보완**.
- 차이: Issue 게시 당시 계획 v0.9·기능 v0.8·데이터 v0.5보다 위 최신 승인 보완을 적용한다. 계획 v0.12의 #32 open 표시는 실제 completed 종료와 차이가 있어 GitHub의 완료 상태·선행 병합을 기준으로 착수했다. 원본 문서를 이번에 수정하지 않았다.
- 기존 `Simulation/SimulatedAmr.cs`에 동기 계산 `Advance(double elapsedSeconds)`·진행 첨자·완료 여부를 추가한다. 새 파일 `tests/Amr.Simulator.Tests/MovementTests.cs`에 실제 대기 없는 시험을 추가한다. README와 이 실행 근거를 갱신한다.
- 이동은 순서 있는 경유점의 직선 구간, 속도는 m/s, 시간은 초다. 도달 후 남은 시간은 다음 구간에 사용하고 최종 위치는 정확한 목표 좌표로 정지한다. 같은 위치 경유점은 시간 소모 없이 통과한다.
- 0초는 유지, 음수·NaN·양/음 무한대는 계산 전에 거부하고 위치·State·현재 속도·진행 첨자·완료 여부를 보존한다. 기존 승인 정책을 구체화한 .NET API는 `ArgumentOutOfRangeException(ParamName=elapsedSeconds, 이유)`다. 설정 파일 오류와 별개이며 순수 계산에서 Console I/O를 수행하지 않는다.
- 실제 타이머·실행 루프·이동 로그·키보드 Ctrl+C는 #34 범위다. MQTT·다중 AMR·장애·명령·DB·GUI는 구현하지 않았다. 별도 spec_revision/verification_index는 없으며 Issue·명세 버전·Git 기준과 소스/DLL 해시로 추적한다.

## 2. 환경·브랜치·보존

| 항목 | 실제 값 |
| --- | --- |
| 작업 위치 | C:\amr-control-system-demo |
| 브랜치 | feature/33-simulator-movement |
| 생성 방법 | git-flow-next 2.0.0의 git flow feature start 33-simulator-movement |
| 기준 커밋 | 최신 origin/develop과 같은 3257f5ee88041cbc03827256b155f5e481c7aed0 |
| 작업 상태 | 기준 커밋 + 이번 미커밋 구현·시험·README·근거. 기존 별도 로컬 변경은 포함 대상과 구분 |
| OS / SDK / Runtime | Windows 10.0.26200 / SDK 10.0.401 / .NET 10.0.12, net10.0 |
| 시험 구성 | Microsoft.NET.Test.Sdk 17.14.1 / xUnit 2.9.3 / adapter 3.1.4 |
| 최종 빌드 | 2026-10-21 16:56:11~16:56:13 KST, 종료 0·경고/오류 0 |
| 최종 자동시험 | 같은 날 16:56:30~16:56:31 KST, 표준 VSTest |
| 실제 콘솔 회귀 | 같은 날 16:56:32~16:56:33 KST, Simulator 15개 별도 프로세스 |
| 제품 DLL SHA256 | A3AF258E3DF7E15068E0B846E57A32E27151B61CD7C659CBA8C14F948EF3DE61 |
| 시험 DLL SHA256 | 8E06B87DDB887EA7F411EA0F7846801BA0EAD35E4EDB9C26C09DAE67058284CE |
| 외부 시스템·대역 | Broker·MQTT·GUI·Core·DB 없음. 이동 시험은 같은 프로세스에서 시간 직접 주입 |

정확한 시각·명령·버전은 `environment.json`, `build-final.json`, `tests-final.json`과 콘솔 `verification.json`에 보관한다. 빌드는 이미 확인된 생성물 쓰기 권한 제한을 고려해 허용된 환경에서 수행했다. `--no-restore`로 기존 복원 결과를 사용했으며 새 패키지·SDK·서비스를 설치하지 않았다. 이번 빌드에는 NU1900이 발생하지 않았다. 직전 별도 NuGet 복원 성공과 이번 제품 시험을 하나의 결과로 합산하지 않는다.

기존 변경은 원본 26개 파일의 원시 바이트·Git 인덱스를 임시 폴더에 백업하고 `stash --include-untracked`로 보존했다. 로컬 develop을 fast-forward하고 새 feature를 만든 뒤 `stash apply --index`와 원본 파일 복사로 작업 파일·스테이징을 복원했다. Git 정규화로 줄바꿈이 달라진 파일도 원본 SHA256으로 되돌렸다. 보존용 stash는 삭제하지 않았다.

- 백업 폴더: `C:\Users\Admin\AppData\Local\Temp\amr-issue33-preserve-d0dca88a8b3a4da7a71fc13e6f0fe2c3`
- 보존용 stash: `1fffc4b4fef06b7e63d95f84724f5f2fc96ddc0b`
- 별도 솔루션 tools 등록, #32 PR 초안의 기존 스테이징·작업 파일, 앞선 리뷰·병합 폴더를 보존한다. 새 #33 파일을 스테이징하지 않는다. `preservation.json`에서 최종 대조한다.
- `source-sha256.json`은 제품·시험·설정·도구·솔루션의 작업 파일 SHA256과 Git blob 식별값을 기록한다. 솔루션 항목은 보존된 사용자 수정으로 별도 표시했다. 실제 빌드는 솔루션 대신 Simulator 시험 프로젝트를 지정했다.

작업 도중 외부 Git 조작이 있었다. reflog에서 16:54:29 KST의 feature/33 → develop 전환과 이후 develop 재전환을 확인했고, 최종 확인 때 #33 브랜치가 삭제돼 있었다. HEAD와 작업 파일은 같은 `3257f5e` + 이번 변경으로 유지됐다. 따라서 콘솔 실행 기록의 WorkingBranch는 develop이며, 시작 시 environment.json의 feature/33 표기와 구분한다. 17:02:32 KST에 같은 커밋에서 `git switch -c feature/33-simulator-movement`로 요청한 브랜치를 복원했다. 시험한 소스와 DLL 해시가 동일함을 다시 대조했으며 제품 시험을 불필요하게 반복하지 않았다. 이 복원은 최초 Git Flow 생성과 별개의 보존 조치다. 원본 reflog는 `branch-events.log`에 보관한다.

## 3. 시험 입력·명령·예상

```powershell
dotnet build tests/Amr.Simulator.Tests/Amr.Simulator.Tests.csproj --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test tests/Amr.Simulator.Tests/Amr.Simulator.Tests.csproj --no-build --no-restore --logger 'trx;LogFileName=movement-final.trx' --results-directory evidence/AMR/20261021-movement-01
pwsh -NoProfile -File tests/manual/AMR01/Verify-Settings.ps1 -RunId 20261021-movement-console-01
```

위 값은 이번 실행 ID다. 재실행할 때는 TRX 출력 폴더와 RunId에 새 값을 사용한다. VSTEST_CONNECTION_TIMEOUT=15는 시험 자식 프로세스 환경에 지정했으며 이동 계산에는 실제 대기나 환경 시각 의존을 넣지 않았다.

| 시험 | 입력·예상 | 실제 확인 방법 |
| --- | --- | --- |
| F03 축·음수·대각선 | (0,0)→(1,0), 1m/s·0.5초 → (0.5,0); (0,0)→(3,4), 1m/s·2.5초 → (1.5,2), BUSY·설정 속도 | 축·대각선 6개 Theory, 각 축 절대 오차 ≤0.000001m·상태·속도 assertion |
| F04 잔여 시간 | (0,0)→(1,0)→(1,1), 1m/s·1.5초 → (1,0.5), +0.5초 → 정확한 (1,1)·IDLE·0 | RemainingTime 시험 |
| F04 순서·여러 구간 | (1,0)→(1,1)→(2,1), 2.5초 → (1.5,1), 목표 첨자 2 | OneUpdate 시험 |
| 정확한 중간 도착 | 위 2구간에 1초 → (1,0), 다음 구간 대기 중 BUSY·속도 1 | ExactIntermediateArrival 시험 |
| 같은 경유점 | 처음·중간·마지막에 중복점; 전부 초기 위치인 경로 1000개 | 시간 소모 없음, 순서 유지, 불필요한 BUSY 없음·완료 |
| 도착 정지 | 목표(-2,-3), 속도 2·0.5초 후 매우 큰 추가 시간 | 목표 좌표 정확히 일치, IDLE·0·완료 유지, 설정 속도 보존 |
| F03 0초 | 초기·이동 중·도착 후 각각 0·-0 | 위치·상태·속도·진행·완료 스냅샷 동일, 3건 |
| F03 잘못된 시간 | 위 세 상태 각각 -0.1·NaN·±Infinity | ParamName·이유·전체 스냅샷 보존, 12건 |
| 비교 오차와 도착 구분 | 목표 직전 0.0000005m | 오차 범위 안이어도 아직 BUSY·미완료, 추가 시간 후 완료 |
| 반복 갱신 | 3-4-5 구간·후속 구간에 총 4초를 1/10/1000회로 분할 | 예상 (0,4), 순서·상태 유지, 3건 |
| 독립 상태·설정 | 두 AMR이 같은 읽기 전용 설정 공유, 하나만 갱신 | 다른 AMR 유지·설정 속도·경로·초기값·주기 보존 |
| 수치 범위 보호 | 1e±200의 3-4-5 구간, ±double.MaxValue, subnormal 거리·속도, 표현 불가능한 총 거리·아주 짧은 구간 시간 | 유한한 부분 이동·순서·정확한 목표 정지. 극단값 시험은 정규화 비교를 사용하며 일반 AMR의 1e-6m 기준을 변경한 것이 아님 |
| 반올림 회귀 | 1 바로 다음 double을 중간 목표로 사용, 부분 이동이 그 점으로 반올림된 뒤 +0.25초 | (약1.25,0)·BUSY, 최종 (2,0)으로 잘못 건너뛰지 않음 |
| 기존 #32 회귀 | 설정·초기화·UTF-8 BOM/no-BOM 거부·파일·출력·취소 88건 | 기존 검사 유지 |
| 실제 콘솔 | 정상·변경·BOM 정상 3건, 설정·파일·UTF-8 오류 12건 | 정상 초기화·종료0 / 오류는 생성 출력 없이 항목·이유·종료1 |

좌표 표의 실제 판정은 원본 시험 코드의 assertion과 최종 TRX 결과에 근거한다. 표의 예상 좌표를 별도로 수집한 실측 로그인 것처럼 표시하지 않는다. 이동 과정은 실제 시계 대기 없이 `Advance`에 경과 시간을 직접 전달했다.

## 4. 실제 결과·완료조건

| 실행 | 실제 결과 | 판정·근거 |
| --- | --- | --- |
| 최초 빌드·자동시험 | 빌드 경고/오류 0, 당시 128건 모두 통과 | build.log·movement-initial.trx |
| 추가 반올림 경계 시험 | 1건 실패. 예상 X≈1.25인데 X=2, 오차0.75로 경로를 건너뜀 | rounding-before.trx·log·json. 실패 보존 |
| 최종 빌드 | 경고0·오류0·종료0 | build-final.log·json |
| 최종 표준 VSTest | **129건 통과·실패0·skip0·종료0** | movement-final.trx·tests-final.log·json·test-summary.json |
| 최종 시험 구성 | 기존 설정·초기화 88건 + 새 이동 41건 | TRX 개별 결과 집계와 test-summary.json |
| 실제 초기화 회귀 | **15건 모두 통과** | [verification.json](../20261021-movement-console-01/verification.json) 및 입력·stdout/stderr |

최초 128건과 최종 129건은 대부분 같은 시험이므로 합산하지 않는다. 추가 1건은 결함 재현 뒤 수정한 회귀시험이며 최종 129건에 포함된다. 기존 in-process 83건·앞선 #32의 88건도 이번 수치에 다시 더하지 않는다. 실제 콘솔 15건은 이동 루프 검증이 아니라 기존 초기화 동작의 회귀 검증이다.

| #33 완료조건 | 구현·근거 | 결과 |
| --- | --- | --- |
| 시간 주입·중간 위치·BUSY·현재 속도 | Advance와 축/대각선 Theory 6건 | 충족 |
| 경유점 순서·잔여 시간 | 다음 목표 첨자·도달 시간 차감, RemainingTime·OneUpdate | 충족 |
| 동일 위치·여러 경유점 | SkipReachedWaypoints, 중복/다중 구간·반올림 회귀 | 충족 |
| 정확한 최종 좌표·IDLE·0 | 도달 시 Position=target, 정확한 도착·추가 갱신 시험 | 충족 |
| 전체 같은 경로·도착 후 유지 | 생성 시 같은 선행점 건너뛰기, IsCompleted, 정지 유지 시험 | 충족 |
| 설정 보존·시험·버전·미검증 기록 | 읽기 전용 Settings 재사용, 공유 설정 격리 시험·이 근거·manifest | 충족 |

이는 로컬 구현·시험 완료 판단이며 Issue #33의 체크박스나 GitHub 상태를 변경한 결과가 아니다.

## 5. 수정·설계 판단·예상 PR 질문

### 계산 방식

경과 시간을 구간별로 소비한다. 이동 중은 기존 문자열 BUSY·설정 속도를 사용하고 최종 도착 시 IDLE·0으로 돌아간다. 원래 설정과 경유점 목록은 바꾸지 않는다. 현재 위치·목표 첨자·이동 여부만 보관하며 속도·상태는 설정과 이동 여부에서 읽어 서로 다른 값으로 어긋나는 상태를 줄였다.

모든 입력 좌표·속도의 유한값을 허용하는 기존 설정 계약을 유지한다. `sqrt(dx*dx+dy*dy)`나 `speed*time`을 직접 만들면 유한 입력끼리도 중간 값이 범위를 넘을 수 있어 거리의 유효숫자와 2의 지수를 분리했다. 사용한 기본 라이브러리의 의미: [Math.ILogB](https://learn.microsoft.com/en-us/dotnet/api/system.math.ilogb?view=net-10.0), [Math.ScaleB](https://learn.microsoft.com/en-us/dotnet/api/system.math.scaleb?view=net-10.0). 보간 결과는 해당 구간 끝점 사이로 제한하며, 도착 판정에 임의 반경을 쓰거나 입력 설정을 보정하지 않는다.

대안은 제한된 좌표·속도 범위에서 직접 거리/속도 공식을 쓰는 방식이다. 코드가 짧아 학습·유지보수가 쉽지만 현재 승인 계약에는 Map 범위 제한이 없어 중간 계산의 overflow·underflow를 별도로 다뤄야 한다. 현재 방식은 private 계산 보조 메서드로 한정하고 새 패키지나 별도 엔진 추상화를 추가하지 않았다. 표현 범위 한계까지 제거하거나 실수 계산을 무한 정밀도로 보장하는 방식은 아니며, 성능·메모리 벤치마크도 수행하지 않았다. 운영 범위가 확정되면 단순화 가능성을 재검토한다.

### 구현 중 발견·수정한 결함

부분 이동 결과가 부동소수점 반올림으로 중간 목표와 정확히 같아지면 다음 갱신의 거리 계산이 0이 됐다. 그 결과 NaN이 계산 경로에 들어가 남은 구간을 건너뛰었다. 목표가 `Math.BitIncrement(1d)`인 입력으로 실제 assertion 실패를 확인했다. 최종 구현은 양수 갱신 시작 시에도 기존 동일 위치 건너뛰기를 적용해 0거리 계산을 막는다. 0초·잘못된 시간은 그 전에 검사하므로 기존 상태 보존 계약을 유지한다.

실패 시 제품 소스는 `rounding-before.SimulatedAmr.cs.txt`, 식별값은 `rounding-before-source.json`, 원본 실패는 `rounding-before.trx`에 남겼다. 최종 129건에서 같은 회귀시험이 통과했다.

### 수명·동시성·결합도

동기 계산만 수행하고 새 Task·스레드·큐·Lock·소켓·이벤트 구독을 만들지 않았다. 같은 AMR의 갱신과 읽기는 호출자가 순차 실행해야 한다. 설정 객체는 계속 읽기 전용이며 서로 다른 AMR 인스턴스의 이동 상태를 공유하지 않는다. Console의 취소 토큰·이벤트 수명은 기존 #32 그대로이고 계산 호출의 실제 시간·취소·종료 연결은 #34에서 맡는다.

예상 질문:

1. **왜 0초에도 동일 위치 경유점을 진행시키지 않나?** 최신 승인 기준이 0초의 위치·State·현재 속도 유지다. 생성 시 이미 같은 선행점을 건너뛰며, 이후 같은 위치 처리는 양수 갱신에만 수행한다. 시험은 진행 첨자·완료 여부까지 함께 유지되는지 확인한다.
2. **같은 점만 있는 경로는 언제 완료하나?** 생성 시 이미 모든 목표에 도달했으므로 IDLE·현재 속도0으로 완료한다. 원래 경유점 설정은 그대로 남기며 불필요한 BUSY를 만들지 않는다.
3. **콘솔 실행으로 실제 이동을 봤나?** 아니다. 이번 41건은 직접 시간 주입 계산 검증이고 실제 콘솔 15건은 초기화 회귀다. 실제 시간·이동 로그·Ctrl+C·정상 종료와 설정 변경에 따른 이동 실행 차이는 #34에서 검증한다.

## 6. 남은 범위와 인계

- #33 구현·시험까지 완료했다. 새 feature의 HEAD는 기준 `3257f5e` 그대로이며 제품 커밋·푸시·PR 생성·리뷰 게시·병합·Issue 종료는 진행하지 않았다. 보존용 stash는 제품 변경 커밋과 구분한다.
- #34는 이 모델의 `Advance(초)`, `Position`, `State`, `CurrentSpeedMps`, `IsCompleted`, `NextWaypointIndex`를 사용해 실제 실행 루프를 연결할 수 있다. 타이머·실제 경과 시간 측정·취소·로그 순서의 책임은 그 단계에서 구현한다.
- 이번에 전체 솔루션·기존 통신 768건을 다시 빌드·시험하지 않았다. 통신 코드를 수정하지 않았으며 해당 결과를 새 통과 수치로 사용하지 않는다.
- 실제 MQTT·GUI·DB·장비·키보드 Ctrl+C·대규모 성능·사용자 직접 실행은 미검증이다. 극단 수치 시험을 가능한 모든 double 조합의 전수 검증으로 확대하지 않는다.
- 관련 업무 기록 원본은 C:\process-plan\일일업무\2026-10-21.md다. 원본 계획·일일업무는 이번 코드 작업에서 갱신하지 않았으며, 인계 시 이 근거와 #33의 로컬 미커밋 상태를 연결한다. 코드 저장소에 계획 원본을 중복 작성하지 않는다.
