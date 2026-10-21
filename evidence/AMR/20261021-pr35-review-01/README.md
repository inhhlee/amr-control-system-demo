# PR #35 / Issue #32 리뷰·검증 — 20261021-pr35-review-01

## 1. 목표와 적용 기준

- 실행자: Codex AI 보조 리뷰. 사람의 승인 리뷰가 아니다.
- 대상 PR: https://github.com/inhhlee/amr-control-system-demo/pull/35
- 리뷰 코드: c9d917a78280346cf5dfcc15dbe871383cf56ad9. base develop e4bd66acc6612c6d5206b3ef3929c7f7640a40c3.
- 기준: Issue #32의 완료조건 6개, 과제2 과제계획 v0.10 초안, 기능명세 v0.9 초안 AMR-01, 공통데이터명세 v0.6 초안 4.1, 결정사항 v0.9 초안 D-12·10/21 ID 보완. 문서 원본은 C:\process-plan이며 이번에 수정하지 않았다.
- 확인 범위: JSON 설정·오류 거부·초기 AMR·출력·취소/자원 소유, 전용 시험과 실제 실행 근거. 이동·실행 루프·MQTT·GUI는 #32의 범위 밖이다.
- 별도 spec_revision/verification_index 추적기는 없으므로 PR/Issue·문서 버전·Git blob과 DLL 해시로 연결했다.
- 리뷰 결과: **재현한 P2 결함 1건으로 수정 필요**. 제품 코드는 수정하지 않았다.

## 2. 실행 환경과 증거 버전

| 항목 | 실제 값 |
| --- | --- |
| 실행 위치 | C:\amr-control-system-demo |
| OS / SDK / Runtime | Windows 10.0.26200 / SDK 10.0.401 / .NET 10.0.12 |
| 시험 프로젝트 | net10.0 / Test.Sdk 17.14.1 / xUnit 2.9.3 / adapter 3.1.4 |
| 프로브 결과 파일 작성 시각 | 2026-10-21T15:50:51.9560805+09:00 — 파일 메타데이터. 프로세스별 시작/종료 시각은 별도로 수집하지 않음 |
| 표준 시험 시작/종료 | 2026-10-21T15:51:11.3832361+09:00 ~ 15:51:12.8415276+09:00 |
| 제품 DLL SHA256 | BBBED90D09DB908F170A8BCAF5E54EE74CC5B81A45AF83399698E7B144ABDA12 |
| 시험 DLL SHA256 | AA4B2011E6B18C47BC96443E3F44E89FD0427053FB6131EC6F6A0B5BB96DFB56 |
| 버전 대조 | 두 DLL 모두 기존 environment.json과 일치. publication.json의 102개 파일 blob이 현재 커밋과 모두 일치 |
| 로컬 별도 변경 | 솔루션 tools 등록·PR 본문 초안은 커밋 밖에 유지. 제품/시험/도구 소스와 설정에는 커밋 대비 변경 없음 |
| 외부 시스템·대역 | Broker·MQTT·GUI·Core·DB 사용 없음. 네 인코딩 프로브는 실제 Simulator DLL을 각각 별도 프로세스로 실행 |
| CI | 해당 head의 GitHub check/status 없음. 이를 CI 성공으로 표시하지 않음 |

이번 리뷰의 표준 시험은 이미 빌드된 DLL로 실행했다. DLL·소스 버전 대조를 수행했으며 솔루션 전체 빌드·패키지 복원·기존 통신 자동시험을 새로 실행하지 않았다.

## 3. 입력과 절차

기존 config/simulator.single.json의 UTF-8 바이트를 정상 입력으로 사용했다. 잘못된 입력은 amrId의 첫 ASCII 바이트 A(0x41)를 0xC3으로 바꿔 만들었다. 다음 바이트 M(0x4D)은 UTF-8 연속 바이트가 아니므로 잘못된 UTF-8이다. BOM 사례는 동일한 바이트 앞에 EF BB BF만 덧붙였다.

| 사례 | 입력 파일 | 예상 |
| --- | --- | --- |
| 정상 UTF-8 | valid-utf8.json | ID AMR_001로 초기화·종료 0 |
| 정상 UTF-8 + BOM | valid-utf8-bom.json | 같은 값으로 초기화·종료 0 |
| 잘못된 UTF-8 | invalid-utf8.json | 설정 파일 오류·생성 출력 없음·종료 1 |
| 잘못된 UTF-8 + BOM | invalid-utf8-bom.json | 설정 파일 오류·생성 출력 없음·종료 1 |

프로브 실행 형식:

~~~powershell
dotnet src/Amr.Simulator/bin/Debug/net10.0/Amr.Simulator.dll --config evidence/AMR/20261021-pr35-review-01/invalid-utf8-bom.json
~~~

표준 시험 실행:

~~~powershell
$env:VSTEST_CONNECTION_TIMEOUT = '15'
dotnet test tests/Amr.Simulator.Tests/Amr.Simulator.Tests.csproj --no-build --no-restore --logger 'trx;LogFileName=simulator-review.trx' --results-directory evidence/AMR/20261021-pr35-review-01
~~~

실제 실행은 위 환경 변수를 시험 자식 프로세스에만 지정했다. 시험 프로세스 제한 30초·각 프로브 제한 10초이며 시간 초과는 없었다. stdout/stderr를 동시에 읽고 소유한 프로세스의 종료를 기다린 뒤 Dispose했다.

## 4. 실제 결과와 완료조건 대조

| 확인 | 예상·실제·판정 | 근거 |
| --- | --- | --- |
| 정상 두 프로브 | 모두 ID AMR_001·IDLE·현재속도 0, 종료 0 — 통과 | encoding-probes.json, valid-*.stdout.log |
| 잘못된 UTF-8, BOM 없음 | ConfigurationRejected Field=$file, stdout 비어 있음, 종료 1 — 통과 | invalid-utf8.stderr.log |
| 잘못된 UTF-8, BOM 있음 | ID가 �MR_001로 바뀐 AMR 생성·InitializationCompleted, stderr 비어 있음, 종료 0 — **실패** | invalid-utf8-bom.stdout.log, encoding-probes.json |
| 이번 표준 VSTest | 종료 0, 실행 83·통과 83·실패 0·skip 0 — 통과 | simulator-review.trx, vstest-sandbox.json·stdout/stderr.log |
| 기존 프로세스 검증 | 정상 2·오류 10 = 12건 통과. 이번에는 재실행하지 않음 | ../20261021-settings-console-02/verification.json |
| 기존 빌드 | 솔루션 경고/오류 0, 도구 빌드 NU1900 경고 1. 이번 실행 결과 아님 | ../20261021-settings-final-01/solution-build.log·runner-build.log |

기존 프로세스 내 xUnit 83건과 이번 표준 VSTest 83건은 같은 시험 사례이므로 서로 다른 166개 시험으로 합산하지 않는다. 새 인코딩 프로브는 4건 중 3건 통과·1건 실패이며 기존 83건에 포함돼 있지 않다. 이전 VSTest 실패 기록은 보존한다. 이번에는 같은 연결 제한이 재현되지 않았다.

| #32 완료조건 | 코드·시험 대조 | 리뷰 판단 |
| --- | --- | --- |
| 정상 JSON 로드·AMR 1대 생성 | Loader·Settings·SimulatedAmr, 정상 프로브·표준 시험 | 확인한 정상 범위 충족 |
| IDLE·현재 속도 0·설정 출력 | 초기 모델·Application, 정상 출력 및 ApplicationTests | 충족 |
| 초기 좌표·속도·경로 변경 반영 | ChangedSettings 시험 및 기존 changed 실제 출력 | 확인한 범위 충족. 이동은 후속 |
| 잘못된 값·파일/JSON 거부, 자동 보정 없음 | 일반 오류는 시험 통과. BOM + 잘못된 UTF-8은 변경된 ID로 정상 생성 | **보완 필요 — P2 결함** |
| 음수 좌표·설정/현재 속도 구분 | 정상 모델·출력·자동시험 | 충족 |
| 시험·실제 실행·버전·미검증 연결 | 기존 manifest·환경·실행 기록, 이번 TRX·실패 프로브 | 기존 연결 확인. 이번 실패·새 VSTest 통과는 이 로컬 리뷰 근거로 추가 |

## 5. 문제와 수정 방향

### [P2] BOM 파일에서도 잘못된 UTF-8을 거부해야 함

- 코드 위치: src/Amr.Simulator/Configuration/SimulatorSettingsLoader.cs:19.
- 발생 조건: UTF-8 BOM이 있고 JSON 문자열 안에 잘못된 UTF-8 바이트가 있는 설정 파일.
- 실제 영향: 엄격한 UTF-8 디코더를 지정했는데도 잘못된 문자가 U+FFFD로 대체된다. 이 문자는 ID 검사에서 허용되므로 사용자가 지정하지 않은 ID로 AMR을 생성하고 종료 0을 반환한다. #32의 자동 보정 없는 오류 거부 조건에 어긋난다.
- 근거: 같은 잘못된 본문은 BOM 없을 때 거부되지만 EF BB BF를 붙이면 �MR_001로 생성된다. 제품 DLL·시험 소스를 변경하지 않은 상태에서 실제 프로세스로 재현했다.
- 원인 설명: BOM 자동 감지가 지정한 디코더를 기본 UTF-8 디코더로 바꾸는 경로와 일치한다. 참고로 확인한 [.NET v10.0.0 StreamReader 구현](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/StreamReader.cs#L456)은 UTF-8 BOM 감지 후 Encoding.UTF8을 선택하고 디코더를 다시 만든다. 실제 결함 판정은 이 저장소의 .NET 10.0.12 실행 결과에 근거한다.
- 시험 누락: ConfigurationTests.cs:139의 정상 BOM 시험과 :167의 잘못된 인코딩 시험은 각각 존재하지만, BOM과 잘못된 바이트를 결합한 거부 시험이 없다.
- 우선 수정: 허용한 BOM을 명시적으로 처리한 뒤 엄격한 디코딩을 유지하고, 로더·Application/실제 실행에서 해당 회귀 사례를 추가한다. 정상 BOM 파일은 계속 성공하고, 잘못된 바이트는 ID로 보정되기 전에 파일 오류·종료 1로 거부돼야 한다.

가능한 대안:

1. 바이트로 읽고 UTF-8 BOM만 명시적으로 건너뛴 뒤 strict UTF-8 GetString을 사용한다. 오류 처리 경계가 분명하고 코드가 작다. 현재 전체 문자열 읽기에 비해 입력 크기만큼의 바이트 배열이 추가로 남을 수 있다.
2. StreamReader에서 BOM 탐지·엄격 디코더를 직접 제어한다. 별도 전체 바이트 배열을 줄일 수 있지만 BOM 소비와 디코더 선택·취소·Dispose 처리를 더 주의해야 한다. UTF-8 외 인코딩 지원이 필요하면 허용 범위부터 명시해야 한다.

두 대안을 실제 수정·성능 시험한 것은 아니다. 현재 설정 크기에서는 명확한 디코딩 경계를 우선하는 것이 합리적이며, 파일 크기 제한이나 새로운 설정 패키지를 이번 리뷰에서 요구하지 않는다.

예상 PR 질문:

1. 83건 통과인데 왜 수정이 필요한가? 정상 BOM과 잘못된 인코딩의 조합이 빠져 있어 기존 사례가 결함을 감지하지 못한다. 실제 별도 프로세스에서 재현했다.
2. 현재 후속 기능에도 문제가 발생했는가? 이번에 확인한 영향은 잘못된 ID의 초기 생성·성공 종료다. MQTT 식별 오류나 실제 장비 영향은 시험하지 않았으며 확인된 피해로 확대하지 않는다.
3. 수정 완료는 어떻게 판정하는가? 위 네 프로브의 정상 2건은 그대로 성공하고 잘못된 2건은 생성 출력 없이 오류 1로 거부돼야 한다. 새 회귀시험과 표준 전용 시험이 모두 통과해야 한다.

설정·경유점은 읽기 전용과 방어 복사로 보관되고 JsonDocument는 Dispose된다. Program이 CTS와 Ctrl+C 이벤트를 소유하며 RunAsync를 기다린 뒤 이벤트를 해제하는 구조를 확인했다. 이번 초기화 경로에는 공유 가변 큐·소켓·구독·백그라운드 루프가 없다. 실제 키보드 Ctrl+C·동시성 부하 시험을 수행한 것으로 표시하지 않는다.

## 6. 종합 결과와 남은 검증

- 수정이 필요한 P2 결함 1건. 그 외 검토 범위에서 추가 결함을 확인하지 못했다.
- 표준 VSTest의 기존 미검증 항목은 이번 실행의 83건 통과 근거를 확보했다. PR 본문의 과거 설명은 별도 갱신 대상이다.
- 실제 키보드 Ctrl+C·NuGet 취약성 정보 조회·기존 통신 회귀는 이번에 미실행. 이동·MQTT·GUI 등은 #32 범위 밖이다.
- 제품 소스·시험 코드는 수정하지 않았다. 이 리뷰 디렉터리만 새로 작성했으며 기존 로컬 솔루션 수정·PR 초안·스테이징을 보존했다.
- GitHub에 리뷰 댓글이나 본문 수정은 게시하지 않았고, 커밋·푸시·병합·Issue 종료도 수행하지 않았다. PR #35는 Draft/open이며 #32는 open 상태다.
- 이 문서는 로컬 리뷰 근거다. C:\process-plan의 일일업무·계획 원본은 이번 요청에서 수정하지 않았다.

