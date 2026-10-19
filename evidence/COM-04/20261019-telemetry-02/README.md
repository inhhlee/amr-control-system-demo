# COM04-B Telemetry 검사·Heartbeat 회귀 — 20261019-telemetry-02

- 작성자·실행자: Codex, 2026-10-19. 사용자 설계 승인 후 Telemetry 단계만 구현·자동시험했다.
- 관련 [Issue #18](https://github.com/inhhlee/amr-control-system-demo/issues/18), 선행 [PR #19](https://github.com/inhhlee/amr-control-system-demo/pull/19).

## 1. 목표와 적용 기준

Telemetry 정상 필드·수신 시각을 모델로 보존하고, 계약 위반을 원문 없는 첫 실패로 반환한다. Heartbeat와 공통 입력 검사를 공유하면서 기존 공개 API·판정·실패 Code/Field/Reason을 유지한다. MQTT 없는 검사기 시험과 기존 구독·수명주기의 Fake 회귀 범위다.

| 근거 | 적용 버전·항목 |
| --- | --- |
| 사용자 제공 #18 본문 / 실제 GitHub Issue | COM04-B, Telemetry → Alarm → ACK 단계별 설계 확인. 이번 승인은 Telemetry 구현·자동시험까지 |
| 기능명세 / 요구사항정의 | v0.7 COM-04 / v0.5 R-04 |
| 공통규칙 / 공통데이터명세 | v0.4 C-01·05·06·10 / v0.4 3절, 수신 계약 v3의 Telemetry·공통 경계 |
| 검토 및 결정사항 | v0.5 D-03·D-04. ts 단위 추측·날짜 변환 없음, 개발용 크기·깊이 제한 유지 |
| 과제1 과제계획 / 개발환경 / 개발 진행 절차 | 현재 v0.23 / v0.7 / v0.6. Issue의 과거 계획 버전·A 생성 예정 문구와 차이: A는 이미 존재하고 PR #19가 develop에 반영됨 |

문서 원본은 `C:\process-plan`, 상세 수신 계약은 `C:\practice\src\Amr.Communication\Validation\CONTRACT.md` v3다. 기존 제품 소스·과거 시험 결과를 복사하지 않았다. spec_revision·verification_index는 이 저장소에 없으므로 해당 없음이다.

## 2. 실행 환경과 코드

| 항목 | 실제 값·근거 |
| --- | --- |
| 위치 / 브랜치 | `C:\amr-control-system-demo` / `feature/18-message-validation` |
| 기준 코드 | `0a4de95d8e582bd14cafe337e0d8c5389da67b4a` + 미커밋 #18 변경. 최신 origin/develop 확인·fast-forward 확인 후 git flow feature start로 생성 |
| 기존 변경 | 착수 시 작업 트리 깨끗함. 앞서 정리한 HeartbeatSubscription 주석은 기준 코드에 포함, 이번에는 수정하지 않음 |
| 코드 식별 | [source-version.json](source-version.json)의 전체 제품·시험 소스와 프로젝트·SDK 설정 SHA-256 |
| OS / SDK / 대상 | Windows 10.0.26200 X64 / SDK 10.0.401 / net10.0 |
| 패키지 | MQTTnet 5.2.0.1603, Microsoft.NET.Test.Sdk 17.14.1, xUnit 2.9.3, runner 3.1.4. 버전 변경 없음 |
| 시각 | 2026-10-19 13:47:02~13:47:06 KST. 원본 UTC 시각은 [환경](environment.json)·[빌드](build-result.json)·[시험](test-result.json) |
| Broker / QoS / Retain | 사용하지 않음. 유형별 검사 직접 호출과 기존 Fake 회귀. 실제 메시지 발행·구독 시험 없음 |

## 3. 입력·절차·예상 결과

정확한 JSON·바이트 입력과 예상 값은 [TelemetryValidatorTests.cs](../../../tests/Amr.Communication.Tests/TelemetryValidatorTests.cs), [MessageValidationRegressionTests.cs](../../../tests/Amr.Communication.Tests/MessageValidationRegressionTests.cs)의 시험명으로 [TRX](tests.trx)에 연결한다. 테스트 데이터는 이번 작업에서 작성했다.

| 시험 묶음 | 입력·조건 | 예상 결과 |
| --- | --- | --- |
| TM-VALID | 네 허용 state, 음수 위치, 0 속도, 지수 표기 위치·속도, double 극값, Int64 min/max·0·음수·10자리 | 모든 필드·수신 시각 보존. ts만 정수 표기 제한 |
| TM-FIELDS | 필수 누락·중복, 자료형·허용값·ID 오류, pos 내부 추가/중복/누락, 음수 속도, ±1e309 | 해당 Code·Field, 정상 모델 없음, 다음 정상 입력 성공 |
| TM-BOUNDARY | Topic·ID·UTF-8·JSON·Unicode escape, root 자료형, 65,536/65,537 bytes, 깊이 16/17, 멀티바이트·분할 Payload·256바이트 이후 오류 | 공통 경계에서 거부, 정상 Unicode·분할 입력 보존. 깊이 16은 파싱 후 추가 필드에서 거부 |
| COMMON-REGRESSION | Heartbeat와 Telemetry에 공통 오류 13종, 여러 오류가 함께 있는 입력 | 같은 Code·Field·기존 고정 Reason. 크기→Topic→UTF-8→JSON→필드 순서 유지 |
| HB-REGRESSION | 기존 Heartbeat 검사 86·구독/전달 21·연결 6 | 기존 정상·오류·오류 후 정상·수신자 오류 구분 유지 |

실제 명령은 [build-result.json](build-result.json)·[test-result.json](test-result.json)에 기록했다. 저장소 루트에서 SDK·복원된 패키지를 준비한 뒤 아래로 재현한다. 새 증거 수집 시 `--results-directory`에는 사용하지 않은 실행 ID를 지정한다.

```powershell
dotnet build AmrControlSystem.slnx --no-restore --verbosity minimal
dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-build --no-restore
```

## 4. 실제 결과와 판정

| 구분 | 실제 결과 | 판정·증거 |
| --- | --- | --- |
| 빌드 | 종료 0, 경고 0·오류 0 | 통과 — [build.log](build.log) |
| Telemetry | 101개 통과 | 통과 — [tests.trx](tests.trx) |
| 공통 오류 회귀 | 14개 통과 | 통과 — 같은 TRX |
| 기존 회귀 | Heartbeat 검사 86 + 구독/전달 21 + 연결 6 = 113개 통과 | 통과 — 같은 TRX |
| 전체 자동시험 | 228개 실행·통과, 실패·건너뜀 0, 종료 0 | 통과 — [test.log](test.log)·[test-result.json](test-result.json) |
| 코드·문서 대조 | 제품·시험·설정 24개 파일의 시험 당시 SHA-256과 현재 값 일치, 확인한 로컬 링크 누락 0 | 통과 — [verification.json](verification.json). 제품 재시험과 구분 |

## 5. 문제·미실행과 후속

- [실행 01](../20261019-telemetry-01/README.md)은 obj 쓰기 권한으로 빌드 실패, 시험 미실행. 같은 소스를 승인된 권한으로 실행 02에서 검증했다.
- Alarm·ACK는 미구현이며 다음 단계 설계 확인이 필요하다. 이 결과로 #18 전체 완료조건을 체크하지 않는다.
- 실제 Telemetry MQTT 구독·다중 Topic 분기(COM01-B), 큐(COM01-C), Core 상태 변화·DB·Command·Timeout·자동 재접속은 미실행·범위 밖이다.
- 이번 변경의 실제 Heartbeat Broker 재시험도 수행하지 않았다. 기존 실기 증거는 A 당시 기록이며 이번 통과 수량에 합치지 않는다.
- Git Flow 로컬 feature 접두사·시작점은 feature/·develop이었다. shared .gitflow는 없고 기존 downstream 설정은 rebase이나, 이번 작업은 feature start만 사용했다. 설정 변경·rebase·finish는 수행하지 않았다.

## 6. 종합 결과

Telemetry 검사·모델과 공통 경계 추출, 기존 Heartbeat 자동 회귀는 통과했다. Codex 자체 코드 검토이며 사람의 승인 리뷰는 아니다. 커밋·푸시·PR 생성·Issue 갱신·병합은 수행하지 않았다. #18 전체·COM-04 전체·과제1은 완료로 판정하지 않는다.

업무 요약은 문서 원본 `C:\process-plan\일일업무\2026-10-19.md`에 연결한다. 상세 실행 근거는 이 폴더에서 관리한다.
