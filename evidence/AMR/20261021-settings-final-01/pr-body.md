## 관련 Issue

- 관련 Issue: #32
- 총괄 Issue: #28
- 대상 브랜치(base): develop
- 작업 브랜치(head): feature/32-simulator-settings
- 과제·기능 ID: 과제2 / AMR-01 / 과제2/1
- 명세·계획 근거: 과제2 과제계획 v0.10 초안, 기능명세 v0.9 초안 AMR-01, 공통데이터명세 v0.6 초안 4절, 결정사항 v0.9 초안 D-12·10/21 ID 보완. 원본에 공유 URL이 없어 문서명·버전·항목으로 식별한다.
- 이번 요청은 변경 분리와 기존 PR 갱신까지다. #32·#28은 open으로 유지하고 병합·Issue 종료를 수행하지 않는다.

## 변경 분리와 이력 보존

이 PR은 **Draft** 상태를 유지한다. 선행 #6 자료의 분리는 완료했으며, 최종 파일 차이에는 #32 관련 변경만 남았다.

- 원격 base: develop `e4bd66acc6612c6d5206b3ef3929c7f7640a40c3`.
- 원격 head: feature/32-simulator-settings `c9d917a78280346cf5dfcc15dbe871383cf56ad9`.
- 이력: `e4bd66a → 20950a2 → d518356 → 99c5087 → c9d917a`. 기존 `d518356`과 로컬 develop·feature/28 브랜치를 보존했다. 원격 develop을 변경하거나 이력을 재작성하지 않았다.
- 이전 #6 재리뷰·병합 확인 자료 192개 파일은 [codex/preserve-6-review-evidence](https://github.com/inhhlee/amr-control-system-demo/tree/codex/preserve-6-review-evidence) 브랜치의 원래 커밋 `20950a2`에 별도로 보존했다. 이번 #32 제품 변경·시험으로 집계하지 않는다.
- [분리 커밋 c9d917a](https://github.com/inhhlee/amr-control-system-demo/commit/c9d917a78280346cf5dfcc15dbe871383cf56ad9)는 그 192개 자료만 #32 브랜치의 최종 파일 목록에서 제외한다. GitHub의 develop 대비 파일 차이에서 evidence/COM-03 항목은 0개다.
- 실제 PR 비교는 **4개 커밋·104개 파일**이다. [최종 비교](https://github.com/inhhlee/amr-control-system-demo/compare/e4bd66acc6612c6d5206b3ef3929c7f7640a40c3...c9d917a78280346cf5dfcc15dbe871383cf56ad9). 기존 이력을 보존했으므로 커밋 목록에는 `20950a2`가 남지만, 해당 #6 자료의 추가분은 최종 파일 차이에서 상쇄됐다.
- `99c5087`의 #32 변경 103개 파일과 선행 `d518356`의 Simulator 프로젝트 골격이 현재 파일 차이를 구성한다. 골격의 솔루션·Program 변경은 #32 변경과 같은 파일에 겹치므로 고유 파일 수는 104개다.
- 분리 전후 #32 소스·시험·설정·README·실행 근거는 변경되지 않았다. 게시 명세의 102개 파일 Git blob 해시가 모두 일치한다. 이번 분리 단계에서 제품 빌드·시험을 재실행하지 않았다.
- 로컬의 별도 `AmrControlSystem.slnx` 도구 프로젝트 등록 수정과 PR 초안의 기존 스테이징 상태를 보존했다. 분리 전 로컬 파일은 별도로 백업하고 해시를 대조했으며, PR 초안의 작업 파일만 현재 설명으로 갱신한다. 게시된 솔루션은 시험 당시 버전이고 보조 도구는 별도 프로젝트 명령으로 빌드한다.
- 기존 environment.json의 feature/28·미커밋·브랜치 생성 차단 표시는 **시험 당시 기록**이다. publication.json은 [최초 게시 커밋 99c5087](https://github.com/inhhlee/amr-control-system-demo/blob/99c5087e087ffd261c57d49d3bedd4a51759dc52/evidence/AMR/20261021-settings-final-01/publication.json)의 준비본을 식별하며, 현재 PR head는 위 분리 커밋이다.

## 변경 내용

- 해결할 문제와 변경 후 동작: Hello, World! 골격에서 JSON 설정을 읽고 검사한 뒤 AMR 1대를 초기 위치·IDLE·현재 속도0으로 생성해 출력하는 독립 초기화 프로그램으로 변경한다. 오류는 생성 전에 항목·이유를 stderr에 기록하고 종료1, 정상 초기화는 종료0이다.
- Configuration/: 필수값·자료형·ID·유한 숫자·양수 속도·정수 ms·경로 검사. 누락·null을 기본값으로 보정하지 않고 추가·중복 필드도 거부한다.
- Simulation/: Position2D·초기 SimulatedAmr, 읽기 전용 설정과 순서 있는 경유점 보관.
- Program.cs·SimulatorApplication: --config 입력, 사용 설정·초기값 출력, 오류/취소·이벤트 수명 관리.
- config/simulator.single.json: 음수 초기 좌표를 포함한 필수 설정 예시.
- tests/Amr.Simulator.Tests/·솔루션: 전용 xUnit 시험 프로젝트 등록.
- tests/manual/AMR01/Verify-Settings.ps1: 실제 별도 Simulator 프로세스의 정상·변경·오류 입력 확인.
- tools/RunSimulatorTests/: 로컬 소켓이 제한된 환경에서 동일 xUnit 엔진을 프로세스 안에서 실행하는 보조 도구. 표준 VSTest 성공으로 표시하지 않는다.
- README·evidence/AMR/: 실행 방법, 코드/설정 해시, 입력·예상/실제·판정·실패 이력·미검증 범위를 연결한다.
- 이동·도착(#33), 실행 루프·이동 중 Ctrl+C(#34), MQTT·다중 AMR·장애·명령/ACK·GUI·Core·DB는 후속 범위다.

## 실제 시험 결과

- 시험 당시 코드 버전: d5183566cfba8ced1cdd24218efc48f09472397d + 당시 미커밋 #32 변경. 해당 준비본을 99c5087e087ffd261c57d49d3bedd4a51759dc52로 커밋·게시했다.
- 실행 일시·환경: 2026-10-21 KST / Windows 10.0.26200 / SDK10.0.401 / net10.0 / xUnit2.9.3.
- 실행 위치: 이 코드 저장소. Broker·GUI·Core·MQTT 대역 없이 설정·초기화만 확인.
- 시험 당시 소스·프로젝트·설정·도구 17개 SHA256은 source-sha256.json에 보관했다. 이번 PR 준비에서는 게시 명세의 102개 파일이 커밋의 Git blob 해시와 모두 일치하고 로컬·원격 head가 같음을 대조했다. 별도 로컬 솔루션 수정은 비교에서 분리했다.
- 아래는 2026-10-21에 이미 실행한 결과다. 커밋·게시·이번 PR 준비 단계에서 제품 시험을 재실행하지 않았다. 근거 링크는 게시 커밋으로 고정한다.
- 상세 근거: [검증 README](https://github.com/inhhlee/amr-control-system-demo/blob/99c5087e087ffd261c57d49d3bedd4a51759dc52/evidence/AMR/20261021-settings-final-01/README.md), [소스 SHA256](https://github.com/inhhlee/amr-control-system-demo/blob/99c5087e087ffd261c57d49d3bedd4a51759dc52/evidence/AMR/20261021-settings-final-01/source-sha256.json), [실행 환경·DLL 해시](https://github.com/inhhlee/amr-control-system-demo/blob/99c5087e087ffd261c57d49d3bedd4a51759dc52/evidence/AMR/20261021-settings-final-01/environment.json).

| 구분 | 명령·절차와 입력·조건 | 예상 결과 | 실제 결과 | 판정 | 증거 링크·경로 |
| --- | --- | --- | --- | --- | --- |
| 솔루션 빌드 | dotnet build AmrControlSystem.slnx --no-restore -m:1 -nr:false -p:UseSharedCompilation=false | 오류 없이 빌드 | 경고0·오류0 | 통과 | [빌드 로그](https://github.com/inhhlee/amr-control-system-demo/blob/99c5087e087ffd261c57d49d3bedd4a51759dc52/evidence/AMR/20261021-settings-final-01/solution-build.log) |
| 보조 도구 빌드 | dotnet build tools/RunSimulatorTests/RunSimulatorTests.csproj --no-restore -m:1 -nr:false -p:UseSharedCompilation=false | 동일 xUnit 엔진 실행 도구 빌드 | 오류0·NU1900 경고1 | 빌드 통과 / 취약성 조회 미검증 | [도구 빌드 로그](https://github.com/inhhlee/amr-control-system-demo/blob/99c5087e087ffd261c57d49d3bedd4a51759dc52/evidence/AMR/20261021-settings-final-01/runner-build.log) |
| xUnit 프로세스 내 실행 | RunSimulatorTests.dll로 같은 xUnit 엔진의 Simulator 시험 실행 | 모든 사례 통과, 0건/skip/엔진 오류 실패 처리 | 83건·실패0·skip0·엔진 오류0 | 통과 | [83건 결과](https://github.com/inhhlee/amr-control-system-demo/blob/99c5087e087ffd261c57d49d3bedd4a51759dc52/evidence/AMR/20261021-settings-final-01/tests.json) |
| 실제 초기화 | 예시 설정·변경 설정을 같은 DLL로 실행 | 설정·초기값·경로 일치, IDLE·현재속도0, 종료0 | 정상2건 일치 | 통과 | [정상 출력](https://github.com/inhhlee/amr-control-system-demo/blob/99c5087e087ffd261c57d49d3bedd4a51759dc52/evidence/AMR/20261021-settings-console-02/normal.stdout.log), [변경 출력](https://github.com/inhhlee/amr-control-system-demo/blob/99c5087e087ffd261c57d49d3bedd4a51759dc52/evidence/AMR/20261021-settings-console-02/changed.stdout.log) |
| 실제 오류 거부·전체 프로세스 | 누락·ID·속도·정수주기·좌표·문법·파일 오류10건 및 정상2건 | 오류는 생성 출력 없이 항목·이유와 종료1, 정상은 종료0 | 12/12 통과 | 통과 | [입력·예상·실제·판정](https://github.com/inhhlee/amr-control-system-demo/blob/99c5087e087ffd261c57d49d3bedd4a51759dc52/evidence/AMR/20261021-settings-console-02/verification.json) 및 같은 폴더의 stderr.log |
| 표준 VSTest | dotnet test tests/Amr.Simulator.Tests/Amr.Simulator.Tests.csproj | testhost 연결 후 시험 실행 | 최초 실행은 90초 접속 timeout·0건. 후속 진단은 collector 로컬 소켓 권한10013으로 중단 | 미검증 / 환경 제한 | [실패 TRX](https://github.com/inhhlee/amr-control-system-demo/blob/99c5087e087ffd261c57d49d3bedd4a51759dc52/evidence/AMR/20261021-settings-unit-01/simulator-initial.trx), [원본 진단 ZIP](https://github.com/inhhlee/amr-control-system-demo/blob/99c5087e087ffd261c57d49d3bedd4a51759dc52/evidence/AMR/20261021-settings-unit-02/failure-logs.zip) |

### 직접 확인 방법

~~~powershell
dotnet run --project src/Amr.Simulator -- --config config/simulator.single.json
$LASTEXITCODE
~~~

정상 출력은 AmrInitialized의 X=-1·Y=0·State=IDLE·CurrentSpeedMps=0, InitializationCompleted이며 종료0이어야 한다. 시험용 복사본에서 초기 좌표·속도·경로를 하나씩 바꿔 다시 실행한다. speedMps 누락/-1, updateIntervalMs=100.5, amrId=AMR/001은 생성 출력 없이 항목·이유와 종료1이어야 한다.

## 완료조건

아래 구현 완료조건은 보관된 Codex 실행 근거로 확인했다. 표준 VSTest·사용자 직접 실행이나 develop 반영 완료를 의미하지 않는다.

- [x] 정상 JSON의 ID·초기 좌표·설정 속도·경로·갱신 주기를 읽고 AMR1대를 생성한다. — xUnit·normal 프로세스
- [x] 초기 IDLE·현재 속도0과 사용 설정을 출력한다. — normal/changed stdout
- [x] 초기 좌표·속도·경로를 바꿔 같은 소스의 재실행 결과에 반영한다. — changed 프로세스
- [x] 누락·잘못된 값·파일/JSON 오류를 생성 전에 이유와 함께 거부하고 오류로 종료한다. — xUnit·오류 프로세스10건
- [x] 음수 좌표를 허용하고 설정 속도·현재 속도를 구분한다. — 정상/변경·자동시험
- [x] 필요한 시험·실제 초기화·코드/설정 버전·미검증을 README·실행 근거에 연결한다. — evidence/AMR/20261021-settings-final-01/README.md·source-sha256.json·environment.json

### 사용자 직접 확인

사용자의 실행 결과를 아직 전달받지 않아 다음 항목은 미체크다.

- [ ] 정상 실행의 초기값·IDLE·현재 속도0·종료0 직접 확인
- [ ] 초기 좌표·속도·경로 변경 반영 직접 확인
- [ ] 필수값 누락·음수 속도·소수 주기·잘못된 ID의 거부·종료1 직접 확인

## 미검증 항목과 남은 작업

| 항목 | 미검증·미충족 사유 또는 해당 없음 근거 | 영향 | 후속 확인 방법·관련 Issue |
| --- | --- | --- | --- |
| 이력 보존 | 기존 d518356을 보존해 선행 커밋 목록은 유지 | 최종 파일 차이는 #32 관련 104개이며 #6 자료는 보관 브랜치로 분리 완료 | 리뷰에서는 최종 파일 차이와 원래 커밋 목록을 구분 |
| 로컬 별도 수정 | 솔루션의 tools 등록과 PR 초안은 커밋 밖에 보존 | 현재 작업 트리와 게시 소스가 완전히 같지는 않음 | 리뷰·재현 기준은 99c5087. 별도 수정은 자동으로 함께 커밋하지 않음 |
| 사용자 직접 확인 | 실행 결과 미전달 | 사용자 확인으로 체크하지 않음 | 위 직접 확인 절차와 실제 출력·종료 코드 전달 |
| 표준 VSTest | testhost 로컬 소켓 권한10013 | 어댑터/testhost 통신 경로 미검증 | 허용된 일반 환경에서 dotnet test |
| 실제 키보드 Ctrl+C | 초기화 취소 토큰만 자동시험 | 실제 입력·이동 중 종료 미검증 | #34에서 실제 실행 확인 |
| NuGet 취약성 정보 | 보조 도구 복원 시 서버 접근 불가 NU1900 | 취약성 조회 성공으로 표시하지 않음 | 연결 가능한 환경에서 복원·확인 |
| 기존 통신 자동시험 | 이번 #32 단계에서 재실행하지 않음 | 기존 768건을 이번 통과 건수에 합산하지 않음 | 통신 변경 또는 추가 회귀 필요 시 별도 실행 |
| 문서 원본 반영 | C:\process-plan 쓰기 제한 | 계획·일일업무 원본은 실제 진도 미반영 | 권한 있는 문서 작업에서 이 근거로 갱신 |
| 이동·MQTT 등 | 이번 설정·초기화 범위 밖 | 과제2 전체 완료 아님 | #33·#34 및 후속 MQTT 작업 |
| 리뷰·병합·종료 | 이번 요청은 변경 분리·PR 갱신까지 | develop 반영·Issue 종료 미수행 | PR 검토 후 별도 요청 단계 |
