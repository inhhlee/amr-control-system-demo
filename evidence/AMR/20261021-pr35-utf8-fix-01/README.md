# PR #35 / Issue #32 — UTF-8 오류 거부 수정·회귀 검증

## 1. 목표와 적용 기준

- 실행자: Codex AI 보조 구현·시험. 사용자 직접 확인·사람의 승인 리뷰와 구분한다.
- 대상: [PR #35](https://github.com/inhhlee/amr-control-system-demo/pull/35), 관련 Issue #32 / 과제2·AMR-01·AMR2-F05.
- 적용 기준: 과제2 과제계획 v0.10 초안, 기능명세 v0.9 초안 AMR-01, 공통데이터명세 v0.6 초안 §4.1, 결정사항 v0.9 초안 D-12·10/21 ID 보완. 공유 URL 없는 원본은 C:\process-plan에서 관리한다.
- 완료조건: 잘못된 설정을 자동 보정 없이 AMR 생성 전에 이유와 함께 거부하고 오류 종료한다. 정상 UTF-8은 BOM 유무와 무관하게 기존 값을 보존한다.
- 문제: 기존 로더는 BOM 자동 감지 과정에서 엄격한 UTF-8 디코더가 바뀌어, 잘못된 바이트를 U+FFFD로 보정하고 변경된 ID로 정상 초기화했다.
- 수정: 파일을 바이트로 읽고 맨 앞 UTF-8 BOM만 건너뛴 뒤 `UTF8Encoding(false, true)`로 해석한다. `DecoderFallbackException`은 기존 `$file` 오류로 전달한다.
- 추적: 별도 spec_revision/verification_index는 없으며 Git 기준 커밋, 소스별 Git blob·SHA256, DLL 해시, 실행별 TRX·로그를 연결한다.

## 2. 실행 환경과 코드 버전

| 항목 | 실제 값 |
| --- | --- |
| 실행 ID | 20261021-pr35-utf8-fix-01, 콘솔 before-01 / after-01 |
| 실행 위치 | C:\amr-control-system-demo |
| OS / SDK / Runtime | Windows 10.0.26200 / SDK 10.0.401 / .NET 10.0.12 |
| PowerShell / 시험 | 7.6.5 / net10.0, Test.Sdk 17.14.1, xUnit 2.9.3, adapter 3.1.4 |
| 기준 커밋 | c9d917a78280346cf5dfcc15dbe871383cf56ad9 |
| 수정 전 | 기준 커밋의 제품 소스 + 새 시험·수동 검증 스크립트. `source-before.json` |
| 수정 후 | 같은 기준 + 로더 수정 + 같은 새 시험·스크립트. `source-after.json` |
| 코드 식별 | 두 manifest의 GitBlob은 Git 정규화된 소스 식별값, SHA256은 실행 당시 작업 파일의 원시 바이트 식별값. 게시 커밋의 blob과 대조한다. |
| 수정 전 제품 DLL | 968575956C0CA8719C1B42837C099A61BCF48563BE5F198DB10413377474A943 |
| 수정 후 제품 DLL | EAFF7D7AB5E547FD796935F3B844B7091CDA525777F75EC886DB896C3DCF3D4A |
| 시험 DLL, 전후 동일 | 58888E1E4C816AC571BCAA09DCC679AB17C88D0DE316CB6660E322402A5CAF71 |
| 수정 전 자동시험 | 2026-10-21 15:59:10 ~ 15:59:11 KST |
| 수정 후 자동시험 | 2026-10-21 16:00:26 ~ 16:00:27 KST |
| 수정 전 / 후 콘솔 | 15:59:12 ~ 15:59:13 / 16:00:28 ~ 16:00:29 KST, 같은 날짜 |
| 외부 구성요소 | 실제 Simulator 자식 프로세스. MQTT·Broker·GUI·Core·DB·통신 대역 없음 |

정확한 시작·종료 시각, 명령과 DLL 해시는 `before-build-retry.json`, `before-regression.json`, `after-build.json`, `after-full.json`, 콘솔별 `verification.json`에 보관했다. 두 단계 모두 제품을 다시 빌드했다. 수정 전 DLL은 앞선 리뷰의 빌드 산출물과 같다고 주장하지 않으며, 제품 소스가 기준 커밋과 같다는 사실을 blob으로 대조한다.

별도 로컬 솔루션의 도구 등록 수정은 사용하지 않도록 전용 시험 프로젝트를 빌드했다. 기존 솔루션 파일·PR 초안의 작업 파일과 초안의 기존 스테이징은 보존하며 이번 커밋 대상에서 제외한다. `baseline.json`에 시작 시 식별값을 보관했다.

## 3. 거부 회귀시험의 입력과 절차

회귀시험은 이번 결함이 나중에 다시 생기면 시험이 실패하도록 남기는 자동 검사다. **잘못된 입력에 프로그램이 종료 코드 1을 반환하는 것이 예상 결과이며, 그 경우 시험은 통과한다.** 시험 실행 도구 자체의 종료 코드는 모든 검사가 통과할 때 0이다.

### 실제로 넣은 바이트

1. 정상 설정의 `amrId`는 `AMR_001`이다. 문자열 안의 첫 바이트 `A`(`41`)만 `C3`으로 바꾼다. 다음 `M`(`4D`)과 나머지 JSON 구조·필수값은 유지한다.
2. UTF-8에서 `C3` 다음에는 `80`~`BF` 범위의 연속 바이트가 와야 한다. `4D`는 이 범위가 아니므로 `C3 4D`는 잘못된 UTF-8이다. 근거: [RFC 3629 §4](https://www.rfc-editor.org/rfc/rfc3629#section-4).
3. 같은 오류 본문으로 두 파일을 만든다. 한 파일은 그대로 두고, 다른 파일은 맨 앞에 UTF-8 BOM `EF BB BF`만 붙인다. 문자열로 `�`를 저장하면 이미 정상 UTF-8이 되므로, 시험은 `WriteAllBytes`로 실제 잘못된 바이트를 기록한다.
4. 정상 UTF-8도 BOM 없는 파일과 있는 파일을 각각 실행한다. 오류 파일뿐 아니라 정상 입력의 기존 동작도 확인한다. 로더 자동시험에서는 `AMR_한글`도 그대로 읽는지 확인한다.

최초 리뷰와 이번 콘솔 회귀 입력의 오류 파일 SHA256은 동일하다.

| 콘솔 입력 | SHA256 |
| --- | --- |
| 정상, BOM 없음 | 503C9C4596B796D41623E6EA8D60B3BD4349D54CC9A80A0179745434295E9828 |
| 정상, BOM 있음 | 065B7855E657041BBF5283E1991D18A6CE518D4319EC814A9610E6255925A8AC |
| 오류, BOM 없음 | 68AC7023966ECD846B47C635B3A1367E6DB5C22B59C46FFB3183A63625E1D1EE |
| 오류, BOM 있음 | A9632FF2F36CBDD4DB51262912436F955443376B2340CE130F0409D0C8A4659D |

이 네 종류는 수정 전후 파일 해시가 모두 일치한다. `changed.json`은 PowerShell 해시테이블의 X/Y 속성 출력 순서가 달라 원시 해시가 다르지만, 값과 검증 조건은 동일하다. 모든 입력·출력을 실행별로 보존했다. 각 증거 폴더의 `.gitattributes`는 줄바꿈 자동 변환을 꺼서 원시 바이트를 유지한다.

### 무엇을 검사하는가

| 단계 | 시험 이름·절차 | 거부 통과 기준 |
| --- | --- | --- |
| 로더 | `InvalidUtf8InId_WithOrWithoutBom_IsRejectedAsFileError(false/true)` | `ConfigurationException`, Field=`$file`, 인코딩 오류 이유 일치 |
| Application | `InvalidUtf8InId_WithOrWithoutBom_HasErrorExitAndNoInitialization(false/true)` | 반환 코드 1, stdout 빈 문자열, stderr의 항목·이유 정확히 일치 |
| 실제 프로세스 | `Verify-Settings.ps1`의 `invalid-utf8`, `invalid-utf8-bom` | OS 프로세스 종료 1, stdout 빈 문자열, stderr에 `$file`과 인코딩 오류 이유 |
| 정상 보존 | 로더의 정상 BOM 2건, 콘솔의 정상 2건 | 값 보존·정상 종료 0. 콘솔은 IDLE·현재 속도 0·초기화 완료 출력 |

로더가 설정을 반환하기 전에 예외를 던지고, Application은 `LoadAsync` 성공 뒤에만 `new SimulatedAmr`를 호출한다. 따라서 로더의 거부 검사와 실행 흐름 대조로 생성 전 거부를 확인했다. 출력 없음만으로 객체 생성 여부를 단독 증명한 것으로 표시하지 않는다.

### 다시 실행하는 방법

저장소 루트에서 실행한다. 빌드가 성공한 뒤 시험하며, 콘솔 스크립트의 RunId는 기존 폴더와 겹치지 않아야 한다.

```powershell
dotnet build tests/Amr.Simulator.Tests/Amr.Simulator.Tests.csproj --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test tests/Amr.Simulator.Tests/Amr.Simulator.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~WithOrWithoutBom'
dotnet test tests/Amr.Simulator.Tests/Amr.Simulator.Tests.csproj --no-build --no-restore
$runId = 'utf8-check-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
pwsh -NoProfile -File tests/manual/AMR01/Verify-Settings.ps1 -RunId $runId
$LASTEXITCODE
```

첫 빌드 전에 패키지 복원이 필요한 새 환경에서는 프로젝트를 복원한다. 이번 실행에서는 기존 복원 결과를 재사용했다. 첫 test 명령은 관련 6건, 두 번째는 전체 88건이다. 앞의 6건은 88건에 포함되므로 합산하지 않는다. 위 안내에서 둘 다 실행할 필요는 없으며, 이번 수정 후에는 전체 88건을 한 번 실행했다. 이번 실제 TRX 저장 옵션·VSTEST_CONNECTION_TIMEOUT=15는 실행 JSON에 기록돼 있다.

개별 오류 파일의 실제 실행도 가능하다.

```powershell
dotnet src/Amr.Simulator/bin/Debug/net10.0/Amr.Simulator.dll --config evidence/AMR/20261021-pr35-utf8-after-01/invalid-utf8-bom.json
$LASTEXITCODE
```

예상 stderr와 종료값은 다음과 같다. stdout에는 초기화 출력이 없어야 한다.

```text
ConfigurationRejected Field=$file Reason=설정 파일의 문자 인코딩을 읽을 수 없습니다.
1
```

## 4. 실제 결과와 판정

| 확인 | 수정 전 | 수정 후 | 근거 |
| --- | --- | --- | --- |
| 관련 자동시험 6건 | 4 통과, BOM 오류의 로더·Application 2 실패. 시험 실행 종료 1 | 전체 88건 안에서 해당 6건 모두 통과 | `before-regression.trx`, `after-full.trx` |
| 전체 Simulator 자동시험 | 수정 전 전체 실행은 하지 않음 | 88 통과, 실패 0, skip 0, 시험 실행 종료 0 | `after-full.log`·TRX |
| 정상 UTF-8, BOM 없음 | 종료 0, 정상 초기화 | 종료 0, 정상 초기화 | before/after `normal.stdout.log` |
| 정상 UTF-8, BOM 있음 | 종료 0, 정상 초기화 | 종료 0, 정상 초기화 | before/after `normal-utf8-bom.stdout.log` |
| 오류 UTF-8, BOM 없음 | 생성 출력 없음, `$file`, 종료 1 | 동일하게 거부, 종료 1 | before/after `invalid-utf8.stderr.log` |
| 오류 UTF-8, BOM 있음 | **�MR_001로 생성, 종료 0 — 실패** | **생성 출력 없음, 인코딩 오류, 종료 1 — 통과** | before/after `invalid-utf8-bom.stdout.log`·stderr.log |
| 전체 실제 콘솔 15건 | 14 통과, 오류 BOM 1 실패 | 15 통과, 스크립트 종료 0 | [수정 전](../20261021-pr35-utf8-before-01/verification.json), [수정 후](../20261021-pr35-utf8-after-01/verification.json) |
| 프로젝트 빌드 | 최초 MSB3491 쓰기 권한 오류. 권한 승격 재시도는 경고/오류 0 | 같은 권한으로 경고/오류 0 | `before-build.log`, `before-build-retry.log`, `after-build.log` |

88건은 기존 83건에서 정상 BOM Fact 1건을 BOM 유무 Theory 2건으로 확장하고, 로더·Application 오류 거부 4건을 추가한 결과다. 실제 콘솔은 기존 12건에 정상 BOM 1건·오류 UTF-8 2건을 추가해 15건이다. 자동시험과 실제 프로세스 시험은 서로 다른 실행 방식이며 건수를 합쳐 하나의 시험군으로 표시하지 않는다.

## 5. 수정 판단·대안·예상 PR 질문

- 선택한 방식: 바이트 읽기 → UTF-8 BOM 건너뛰기 → 엄격한 해석. BOM 자동 감지의 디코더 교체를 피하고 JSON 파싱 전에 오류를 거부한다. `throwOnInvalidBytes=true`의 의미는 [Microsoft UTF8Encoding 문서](https://learn.microsoft.com/en-us/dotnet/api/system.text.utf8encoding.-ctor?view=net-10.0)에 근거한다.
- 비용·한계: 전체 입력 바이트 배열과 문자열이 함께 존재해 파일 크기에 비례하는 추가 메모리가 필요하다. 현재 단일 설정 파일의 명확한 오류 처리를 우선했다. 성능·메모리 벤치마크는 실행하지 않았다. 지원 경로는 UTF-8이며 UTF-16/32 자동 감지 지원을 요구사항으로 간주하지 않는다.
- 대안: 엄격한 StreamReader에서 BOM 처리와 자동 감지를 직접 제어하면 전체 바이트 배열을 줄일 수 있다. 대신 디코더·BOM·자원 수명을 더 주의해서 관리해야 한다. 대규모 설정이나 메모리 제한이 요구되면 입력 크기 근거와 함께 재검토한다.
- 수명·취소: 파일 읽기는 `ReadAllBytesAsync`가 소유·종료하며 기존 취소 토큰을 전달한다. JSON 문서 Dispose, 생성 전 취소 검사, Program의 이벤트 해제 책임을 유지했다. 새 공유 상태·소켓·백그라운드 작업은 없다.

예상 질문과 답변:

1. **새 시험이 실제 결함을 잡는가?** 제품 수정 전 같은 시험을 실행했을 때 BOM이 있는 오류 사례 2건이 실패했다. 같은 오류 입력의 실제 프로세스도 종료 0을 반환했다. 수정 후 같은 시험 코드와 동일한 인코딩 입력이 통과한다.
2. **정상 BOM까지 거부해서 통과한 것 아닌가?** 정상 BOM/no-BOM 파일과 한글 ID 로더 시험이 모두 통과한다. 오류만 선별해서 거부한다.
3. **모든 인코딩·실제 장비 연동이 검증됐는가?** 아니다. 이번 회귀는 재현된 `C3 4D` 조합을 BOM 유무로 비교하며, 기존 잘못된 바이트 시험도 유지한다. 가능한 모든 UTF-8 오류 조합을 전수 시험하거나 MQTT·장비 영향을 검증한 결과는 아니다.

## 6. 종합 결과와 남은 범위

- P2 재현 결함을 수정했고, 표준 자동시험 88건·실제 콘솔 15건이 통과했다. 실패 원본·입력·출력·해시를 남겨 수정 전후를 비교할 수 있다.
- #32의 정상 설정·초기 IDLE/현재 속도 0·변경 설정·음수 좌표·오류 거부 조건은 이 시험 범위에서 충족한다. 이동·도착·루프·실제 Ctrl+C는 #33·#34 후속 범위와 구분한다.
- 기존의 표준 VSTest 환경 실패는 과거 기록으로 보존한다. 이번 표준 VSTest는 성공했으므로 현재 미검증으로 표시하지 않는다.
- 전체 솔루션 빌드, 기존 통신 자동시험, NuGet 복원·취약성 조회, 실제 키보드 Ctrl+C는 이번에 실행하지 않았다. 사용자 직접 실행 결과도 별도로 받지 않았다.
- 게시 대상은 이번 로더·시험·스크립트·README와 새 실행 근거다. 기존 `d518356` 이력, 별도 #6 보관 브랜치, 로컬 솔루션 수정·기존 PR 초안·앞선 리뷰 폴더는 보존한다. 이 문서를 포함한 커밋을 같은 PR #35에 반영하며 병합·Issue 종료는 수행하지 않는다.
- 계획·일일업무 원본은 이번 코드/PR 수정 범위에서 갱신하지 않는다. 문서 담당 원본에 연결할 근거는 PR #35와 이 검증결과이며, 문서 원본 반영 완료로 표시하지 않는다.
