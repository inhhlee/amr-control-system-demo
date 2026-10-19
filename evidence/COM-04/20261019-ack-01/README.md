# COM04-B ACK 검사·네 유형 회귀 — 20261019-ack-01

- 실행자: Codex, 2026-10-19. 사용자 승인 설계에 따라 ACK 구현·자동시험을 수행했다.
- 코드: `feature/18-message-validation`, 기준 `0a4de95d8e582bd14cafe337e0d8c5389da67b4a` + 미커밋 변경. [실행 시 파일 해시](source-version.json), [작업 트리](git-status.log).
- 환경: Windows, .NET SDK 10.0.401, net10.0, MQTTnet 5.2.0.1603, xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1. [실제 환경](environment.log). 이전에 확인한 obj 쓰기 제한 때문에 빌드·시험은 권한을 높여 실행했다.

## 계약·변경

Issue #18 COM04-B, 기능명세 v0.7 COM-04, 요구사항정의 v0.5 R-04, 공통규칙 v0.4 C-01·C-05·C-06·C-10, 공통데이터명세 v0.4 3절·수신 계약 v3, 결정사항 v0.5 D-03·D-04 및 사용자 제공 ACK 계약을 적용했다. 공유 URL 없는 원본 문서는 C:\process-plan에서 관리한다. C:\practice의 코드나 과거 통과 기록을 완료 근거로 복사하지 않았다.

- [AckValidator](../../../src/Amr.Communication/Validation/Ack/AckValidator.cs): 필수 cmdId/result/ts, 선택 id/reason 검사. Topic 자체 검사는 항상 수행한다.
- [AckMessage](../../../src/Amr.Communication/Models/AckMessage.cs): 선택 필드 생략과 문자열·Int64·수신 시각 보존. 생략 id를 Topic 값으로 채우지 않는다.
- [AckValidationResult](../../../src/Amr.Communication/Validation/Ack/AckValidationResult.cs): 계약에 맞는 SUCCESS/FAILURE 모두 정상 모델과 IsValid=true로 반환한다.
- [공통 오류 회귀](../../../tests/Amr.Communication.Tests/MessageValidationRegressionTests.cs): 기존 14건을 네 유형의 Code·Field·Reason과 최초 오류 우선순위 대조로 확장했다. 기존 세 유형 구현과 공통 검사 코드는 변경하지 않았다.

이전 Alarm 실행 스냅샷과 비교하면 AlarmValidator.cs의 줄바꿈만 LF에서 CRLF로 바뀌어 있었다. LF로 정규화한 해시가 이전 해시와 일치하며 내용은 동일하다. 현재 파일을 보존했고 이번 빌드·시험과 source-version.json은 현재 CRLF 파일 기준이다. 이 차이는 verification.json에 별도로 기록했다.

## 시험 입력·예상·실제

정확한 JSON·바이트 입력과 기대값은 [AckValidatorTests.cs](../../../tests/Amr.Communication.Tests/AckValidatorTests.cs)에 정의했고 TRX 시험명·소스 해시로 실제 실행과 연결한다.

| 입력·조건 | 예상 | 실제·판정 |
| --- | --- | --- |
| SUCCESS/FAILURE × id 생략/포함 × reason 생략/포함 | 8개 조합 모두 정상, 값·생략 여부·수신 시각 보존 | 8건 통과 |
| Unicode·앞뒤 공백·줄바꿈 문자열 | Trim·정규화 없이 보존, ID는 Topic과 정확히 일치 | 2건 통과 |
| Int64 최소·최대·0·음수·10자리 ts | 단위·날짜 변환 없이 원래 값 유지 | 5건 통과 |
| 필수 3필드 누락·허용 5필드 중복 | MissingField/DuplicateField 및 JSON 경로 | 8건 통과 |
| 필드별 자료형·빈 값·허용값·ID 불일치·ts 표기/범위 오류 | 해당 필드 거부, 뒤의 유효 FAILURE는 정상 반환 | 43건 통과 |
| 추가·대소문자 오류·escape 중복 키 | UnexpectedField/DuplicateField, 원문 없는 오류 | 5건 통과 |
| id 생략 상태에서 Topic 오류·정상 Unicode Topic | Topic 검사는 항상 수행, 모델 Id는 생략 상태 유지 | 14건 통과 |
| JSON·Unicode escape·root 자료형·UTF-8 오류 | 공통 실패 규격으로 거부 | 17건 통과 |
| 크기/깊이 경계·256 bytes 이후 입력·분할 버퍼·연속 호출 | 전체 입력 검사와 값 보존, 최초 오류·호출 간 격리 유지 | 4건 내 경계별 assertion 통과 |
| 기존 Heartbeat·Telemetry·Alarm·필드·수명주기·공통 오류 | 기존 결과 유지, 공통 오류는 네 유형 동일 | 기존 357건 통과 |

## 실제 결과와 재현

- 빌드: 경고 0·오류 0·종료 0. [build.log](build.log), [명령·종료 코드](build-result.json).
- 자동시험: 463개 실행·통과, 실패 0·건너뜀 0·종료 0. ACK 106 + 기존 357. [test.log](test.log), [TRX](tests.trx), [명령·완료 시각](test-result.json).
- [소스·시험 수량 검증](verification.json).

저장소 루트에서 아래 명령으로 재현한다. 증거 수집 시 새 실행 ID를 사용하고 기존 로그·TRX·소스 스냅샷을 덮어쓰지 않는다.

```powershell
dotnet build AmrControlSystem.slnx --no-restore --verbosity minimal
dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-build --no-restore
```

## COM04-B 완료조건과 경계

| 완료조건 | 이번 확인 |
| --- | --- |
| 세 유형의 정상 모델·선택 필드 보존 | Telemetry·Alarm·ACK 시험 통과 |
| 필수 필드·자료형·허용값·ID·공통 JSON 오류 거부 | 유형별 시험과 네 유형 공통 회귀 통과 |
| 유효 FAILURE ACK를 정상 모델로 보존 | id/reason 조합 4개 및 오류 다음 FAILURE 등 확인 |
| 기존 Heartbeat 결과 유지 | 기존 검사 86·구독 21·공통 회귀 통과 |
| 실제 변경·확인 결과·미검증을 README·Issue·PR에 연결 | README·로컬 Issue 기록 연결. GitHub Issue 갱신·PR 생성은 미수행 |

구현·MQTT 없는 자동시험 범위는 완료했다. 커밋·푸시·GitHub Issue 근거 연결·PR·리뷰·병합은 남아 있으므로 #18 전체 절차 완료나 Issue 종료로 표시하지 않는다. 이번 검토는 Codex 자체 확인이며 사람 승인 리뷰로 집계하지 않는다.

실제 세 유형의 Broker 구독·다중 Topic 분기는 COM01-B, AMR별 큐는 COM01-C 범위다. Core 명령 업무 결과 판단·재시도·상태 변경·GUI·DB·Timeout·재접속은 이번 구현과 시험에서 제외했다. 정상 모델 반환을 실제 로봇 제어 동작으로 해석하지 않는다. 선행 실행 증거는 보존했다.
