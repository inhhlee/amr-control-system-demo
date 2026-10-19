# COM04-B Alarm 준비 — 공통 필수/선택 필드 검사

- 실행 ID: `20261019-alarm-fields-01`, 실행자 Codex, 2026-10-19.
- 범위: 단계별 학습·구현 요청에 따라 Alarm 모델·결과 클래스 이후 공통 필드 구성 검사를 확장했다. AlarmValidator 전체 구현은 다음 단계다.
- 적용 계약: Issue #18 COM04-B, 기능명세 v0.7 COM-04, 요구사항정의 v0.5 R-04, 공통규칙 v0.4 C-01·C-05·C-06·C-10, 공통데이터명세 v0.4 수신 계약 v3, 결정사항 v0.5 D-03·D-04. 사용자 제공 계약의 Alarm 필수 5개·선택 message 및 추가/중복 키 거부 범위를 적용했다.

## 코드·환경

- 작업 위치: `C:\amr-control-system-demo`, 브랜치 `feature/18-message-validation`.
- 기준 커밋: `0a4de95d8e582bd14cafe337e0d8c5389da67b4a` + 미커밋 변경. [파일별 SHA256](source-version.json), [작업 트리](git-status.log).
- 환경: Windows, .NET SDK 10.0.401, net10.0. 실제 SDK·OS·런타임은 [environment.log](environment.log)에 보존했다. 패키지 버전은 코드 스냅샷의 프로젝트 파일 기준이다.
- 기존 실행에서 확인한 obj 쓰기 권한 제한 때문에 이번 빌드·시험은 권한을 높여 실행했다. 이전 Telemetry 실행의 실패/성공 기록은 보존했다.

## 변경·시험 입력과 예상

- [MessageValidation.cs](../../../src/Amr.Communication/Validation/Common/MessageValidation.cs): `CheckFields`가 허용 필드와 필수 필드를 별도로 받는다. 기존 `CheckRequiredFields`는 같은 목록을 두 인수로 전달하므로 Heartbeat·Telemetry 호출과 오류 우선순위가 유지된다. 링크는 후속 폴더 정리의 현재 위치이며, 실행 당시 경로·해시는 source-version.json에 보존했다.
- [프로젝트 파일](../../../src/Amr.Communication/Amr.Communication.csproj): 공통 내부 함수를 직접 시험하도록 `Amr.Communication.Tests`에만 internal 접근을 허용했다. 공개 API로 바꾸지 않았다.
- 사용자 작성 AlarmMessage 및 앞선 단계의 AlarmValidationResult도 이번 전체 빌드에 포함했다.
- 입력 원문과 예상값: [MessageFieldValidationTests.cs](../../../tests/Amr.Communication.Tests/MessageFieldValidationTests.cs). TRX의 시험명으로 결과와 연결한다.

| 입력·조건 | 예상 | 실제·판정 |
| --- | --- | --- |
| Alarm 필수 필드 5개, message 생략/포함 | 필드 구성 정상, 오류 없음 | 2건 통과 |
| id/ts/code/level/alarmState 각각 누락 | MissingField 및 해당 JSON 경로 | 5건 통과 |
| message 중복, Unicode escape로 표현한 message 중복, id 중복 | DuplicateField, 필수 누락보다 우선 | 3건 통과 |
| 허용하지 않은 secret, 대소문자가 다른 Message | UnexpectedField, 원문 없는 고정 Reason | 2건 통과 |
| 기존 Heartbeat·Telemetry·수명주기·공통 오류 | 기존 판정과 Code·Field·Reason 유지 | 기존 228건 통과 |

## 실행 결과·재현

- 솔루션 빌드: 경고 0, 오류 0, 종료 코드 0. [build.log](build.log), [명령·종료 코드](build-result.json).
- 자동시험: 전체 240, 통과 240, 실패 0, 건너뜀 0. [test.log](test.log), [TRX](tests.trx), [명령·실행 시각](test-result.json).
- 재현은 저장소 루트에서 아래 명령을 실행한다. 재실행 증거는 기존 폴더를 덮어쓰지 말고 새 실행 ID로 저장한다.

```powershell
dotnet build AmrControlSystem.slnx --no-restore --verbosity minimal
dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-build --no-restore
```

## 완료 범위·남은 작업

공통 필드 구성 검사 확장과 기존 자동 회귀만 확인했다. 선택 필드가 존재할 때의 자료형·null·공백 검사, Alarm level/alarmState 허용값 검사 및 정상 Alarm 모델 반환은 미구현이다. 따라서 Alarm 입력 검증 완료나 #18 전체 완료로 판정하지 않는다. 실제 MQTT·다중 Topic·큐·Core·GUI는 실행하지 않았다. 커밋·푸시·PR·Issue 갱신도 수행하지 않았다.
