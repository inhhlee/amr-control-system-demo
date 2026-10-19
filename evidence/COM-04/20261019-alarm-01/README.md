# COM04-B Alarm 검사·기존 회귀 — 20261019-alarm-01

- 실행자: Codex, 2026-10-19. 사용자 단계별 설계 확인 및 구현 요청에 따라 Alarm 검사까지 수행했다.
- 코드: `feature/18-message-validation`, 기준 `0a4de95d8e582bd14cafe337e0d8c5389da67b4a` + 미커밋 변경. [실행 시 소스 SHA256](source-version.json), [작업 트리](git-status.log).
- 환경: Windows, .NET SDK 10.0.401, net10.0, MQTTnet 5.2.0.1603, xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1. [실제 환경](environment.log). 기존 obj 쓰기 제한으로 빌드·시험은 권한을 높여 실행했다.

## 적용 계약과 변경

사용자 제공 Issue #18 COM04-B 계약, 기능명세 v0.7 COM-04, 요구사항정의 v0.5 R-04, 공통규칙 v0.4 C-01·C-05·C-06·C-10, 공통데이터명세 v0.4 3절·수신 계약 v3, 결정사항 v0.5 D-03·D-04의 Alarm·공통 입력 경계를 적용했다. 공유 URL 없는 문서 원본은 C:\process-plan에서 관리하며 원본 코드·과거 시험 기록을 이번 완료 근거로 복사하지 않았다.

- [AlarmValidator.cs](../../../src/Amr.Communication/Validation/Alarm/AlarmValidator.cs): 필수/선택 필드·id·ts·code·level·alarmState·message 순서 검사와 정상 모델 반환.
- [MessageValidation.cs](../../../src/Amr.Communication/Validation/Common/MessageValidation.cs): code와 message가 공유하는 문자열 자료형·공백 검사 추가. 기존 Heartbeat·Telemetry 검사 경로는 유지했다.
- [AlarmMessage.cs](../../../src/Amr.Communication/Models/AlarmMessage.cs): 기존 모델 필드를 유지하고 값 보존·업무 처리 경계를 주석으로 명시했다.
- [공통 회귀 시험](../../../tests/Amr.Communication.Tests/MessageValidationRegressionTests.cs): 기존 14건에 Alarm 결과를 함께 대조하도록 확장했다. 기존 예상 오류는 그대로 유지했다.

## 입력·예상·실제

정확한 입력·예상값은 [AlarmValidatorTests.cs](../../../tests/Amr.Communication.Tests/AlarmValidatorTests.cs)에 정의했다. TRX의 시험명과 코드 해시로 실행 결과를 연결한다.

| 입력·조건 | 예상 | 실제·판정 |
| --- | --- | --- |
| level 4종 × alarmState 2종 × message 생략/포함 | 16개 조합 모두 정상, 모든 필드·수신 시각 보존 | 16건 통과 |
| Int64 최소·최대·0·음수·10자리 ts | 자동 변환 없이 보존 | 5건 통과 |
| 필수 5필드 누락 / 허용 6필드 중복 | MissingField / DuplicateField 및 JSON 경로 | 11건 통과 |
| id·ts·code·level·alarmState·message의 잘못된 자료형·값 | 해당 필드 오류, 정상 모델 없음, 다음 정상 입력 처리 | 46건 통과 |
| 추가·대소문자 오류 필드 / escape 중복 키 | UnexpectedField / DuplicateField, 원문 없는 오류 | 5건 통과 |
| Topic 오류 / Unicode ID·문자열 | Topic 거부 / 동일 ID만 허용, 정규화·Trim 없이 보존 | 13건 통과 |
| JSON 문법·Unicode escape·root 자료형·UTF-8 오류 | 공통 오류 규격으로 거부 | 17건 통과 |
| 65,536/65,537 bytes·다중 바이트·깊이 16/17 | 크기·깊이 경계 유지 | 1건 내 경계별 assertion 통과 |
| 256 bytes 이후 입력·분할 버퍼 | 전체 입력 검사, 분할된 Unicode·숫자 해석 | 2건 통과 |
| 다중 오류 우선순위·호출 간 선택 필드 상태 | 첫 오류 반환, 이전 message가 다음 모델에 남지 않음 | 1건 통과 |
| 기존 Heartbeat·Telemetry·공통 필드·수명주기 | 기존 결과 유지, Alarm 공통 오류 Code·Field·Reason 대조 | 기존 240건 통과 |

## 실행 결과·재현

- 빌드: 경고 0·오류 0, 종료 코드 0. [build.log](build.log), [명령·종료 코드](build-result.json).
- 자동시험: 전체 357개, 통과 357, 실패 0, 건너뜀 0. Alarm 신규 117개 + 기존 240개. [test.log](test.log), [TRX](tests.trx), [명령·완료 시각](test-result.json).
- [소스 해시·시험 수량 검증](verification.json).

저장소 루트에서 다음 명령으로 재현한다. 증거 수집 시 새 실행 ID를 사용하고 기존 로그·TRX를 덮어쓰지 않는다.

```powershell
dotnet build AmrControlSystem.slnx --no-restore --verbosity minimal
dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-build --no-restore
```

## 완료 범위와 미검증

Alarm 계약 검사·정상 모델·실패 결과 반환과 기존 자동 회귀까지 완료했다. 실제 MQTT 구독·다중 Topic 분기는 COM01-B, 큐는 COM01-C 범위다. AMR 주행 상태 변경·알람 업무 처리·Core·GUI·DB는 구현하거나 실행하지 않았다. 이 결과를 실제 Alarm Broker 연동 완료로 표시하지 않는다.

ACK는 미구현이며 다음 단계에서 선택 id/reason·SUCCESS/FAILURE 보존 설계를 확인한다. #18 전체·COM-04 전체·과제1 완료로 판정하지 않는다. 커밋·푸시·PR·GitHub Issue 갱신·병합은 수행하지 않았다. 과거 Telemetry·Alarm 준비·폴더 정리 실행 증거는 보존했다.
