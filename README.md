# amr-control-system-demo
가상 AMR 기반 관제시스템 구현 및 Issue·PR 개발 절차 실습

## 작업 위치

| 구분 | 위치 |
| --- | --- |
| 구현·빌드·시험·실행 증거 | `C:\amr-control-system-demo` |
| 계획·명세·결정사항·업무 기록 | `C:\process-plan` |

개발 구성과 소스는 이 저장소에서 관리한다. 구현·Issue·PR 대상은 [amr-control-system-demo](https://github.com/inhhlee/amr-control-system-demo)이며, 계획·명세 원본은 `C:\process-plan`에서 관리한다.

## 작업을 시작할 때

1. 이 README에서 프로젝트 목적과 작업 위치를 확인한다.
2. [AGENTS.md](AGENTS.md)의 에이전트 작업 규칙을 읽고 따른다.
   팀원의 담당·인계, Issue 분할, 브랜치·시험·PR 진행은 [팀 협업 규칙](CONTRIBUTING.md)을 함께 확인한다.
3. [계획·명세·업무 문서 안내](C:/process-plan/README.md)에서 필요한 담당 문서를 찾고, 문서 작업에는 [문서 저장소 지침](C:/process-plan/AGENTS.md)과 [관제 프로젝트 지침](<C:/process-plan/관제시스템 과제 진행/AGENTS.md>)을 함께 적용한다.
4. [개발 진행 절차](<C:/process-plan/기준/개발 진행 절차.md>)와 사용자가 요청한 단계에 맞춰 진행한다. 작업별 원본 스킬은 [AGENTS.md의 안내](AGENTS.md#작업별-스킬)를 따른다.

## 개발 구성

현재 작업은 [Issue #23 — COM01-C AMR별 수신 큐](https://github.com/inhhlee/amr-control-system-demo/issues/23)의 종료 경합 보완과 PR 검토다. 기존 큐 구현은 develop의 `1060c64`에 이미 포함됐고, `feature/23-amr-receive-queue`에서 호출 진입·취소 동기화와 추가 시험을 준비했다. 빌드 경고·오류 0, 전체 자동시험 547건, 실제 MQTT 정상 세 세션 및 별도 Broker 중단 시험을 통과했다. [최신 검증결과](evidence/COM-01/20261019-queue-pr-unit-02/README.md)와 [게시 전 PR 본문 초안](evidence/COM-01/20261019-queue-pr-unit-02/PR.md)을 연결하며, 게시 PR은 Issue #23에서 참조한다. 병합·Issue 종료는 별도 단계다. 아래 Issue별 이전 수치와 미커밋 표시는 당시 실행 기록이다.

선행 [#21](https://github.com/inhhlee/amr-control-system-demo/issues/21)은 [PR #22](https://github.com/inhhlee/amr-control-system-demo/pull/22)로 develop에 반영됐다. 당시 실제 MQTT 두 세션에서 정상 20건 전달·불량 4건 거부·구독 밖 4건 미수신과 정상 종료를 확인했다. [당시 실행 안내](#com01b-run)와 [브라우저 보고서](evidence/COM-01/20261019-multi-mqtt-03/report.html)를 보존한다. 현재 콘솔은 그 수신부의 짧은 콜백에서 큐에 접수한 뒤 AMR별 비동기 처리자를 호출한다.

선행 [#18](https://github.com/inhhlee/amr-control-system-demo/issues/18)은 PR #20으로 develop에 병합됐다. [Telemetry](#telemetry-validation), [Alarm](#alarm-validation), [ACK](#ack-validation) 검사 API와 [당시 463건 검증결과](evidence/COM-04/20261019-ack-01/README.md)는 유지한다.

선행 [Issue #17 — COM04-A Heartbeat 검사와 정상 데이터 전달](https://github.com/inhhlee/amr-control-system-demo/issues/17)은 PR #19로 develop에 반영됐다. 당시 자동시험 113개와 실제 Broker 근거는 [COM-04 실행 안내](#com04-run)와 [최종 검증결과](evidence/COM-04/20261019-final-06/README.md)에 보존한다. 아래 #15·#17·#18·COM-03 절의 결과와 출력 예는 해당 단계 당시 기록이며, 현재 콘솔 형식은 COM01-B 안내를 따른다.

[Issue #15 — COM-01 Heartbeat 한 종류 수신](https://github.com/inhhlee/amr-control-system-demo/issues/15)의 구현·검증은 아래 [Heartbeat 실행 안내](#heartbeat-run)와 [완료조건별 결과](evidence/COM-01/20261012-heartbeat-05/README.md)에서 확인한다. 사용자의 후속 요청으로 기존 `feature/COM-01-heartbeat-subscribe` 브랜치를 게시하는 단계다. 연결된 PR은 Issue #15에서 확인하며 리뷰·develop 병합은 별도 단계다.

[Issue #2](https://github.com/inhhlee/amr-control-system-demo/issues/2)에서 최소 구성을 만들었고, 현재 [Issue #8](https://github.com/inhhlee/amr-control-system-demo/issues/8)의 **A2 접속·해제와 A3 연결 유지·Ctrl+C 정상 종료**를 구현·검증했다. 아래 프로젝트는 모두 `net10.0`을 사용한다. [PR #13](https://github.com/inhhlee/amr-control-system-demo/pull/13)은 2026-10-11 develop에 병합됐다(코드 기준 `c34f380`). 총괄 [Issue #4](https://github.com/inhhlee/amr-control-system-demo/issues/4)의 원래 완료조건 7개와 기존 A1·A2·A3 시험 근거는 [통합 검증결과](evidence/COM-03/20261012-a-summary-01/README.md)에 연결한다. 이 결과는 기존 시험과 병합 코드의 대조이며 제품 재시험·COM-03 전체·과제1 전체 완료를 뜻하지 않는다.

| 파일·프로젝트 | 역할 |
| --- | --- |
| [global.json](global.json) | SDK 10.0.401, `latestPatch`, 시험판 SDK 사용 안 함 |
| [AmrControlSystem.slnx](AmrControlSystem.slnx) | Visual Studio에서 열 솔루션 |
| [Amr.Communication](src/Amr.Communication/Amr.Communication.csproj) | MQTTnet 5.2.0.1603을 사용한 접속 설정·MQTT 승인 확인·명시적 해제·자원 정리 |
| [Amr.Communication.Console](src/Amr.Communication.Console/Amr.Communication.Console.csproj) | 실제 Broker 접속 승인 후 Ctrl+C까지 유지하고 명시적 해제·정리를 기다리는 콘솔 |
| [Amr.Communication.Tests](tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj) | 기존 543개 + 큐 경계 시험 4개, 총 547개 |
| [evidence/setup](evidence/setup/) | 실행별 환경·복원·빌드·실행·시험 탐색 증거 |

시험 패키지는 `Microsoft.NET.Test.Sdk 17.14.1`, `xunit 2.9.3`, `xunit.runner.visualstudio 3.1.4`다.

**준비 Issue #2 당시 범위:** 도구·기존 Broker 환경 조회, 최소 프로젝트·참조 구성, 복원·빌드·준비 콘솔 실행·시험 탐색, 협업 규칙과 증거 기록이다. Issue #2의 제외 범위는 MQTT 연결 로직·메시지 송수신·Heartbeat·자동 재접속, GUI·Core·DB·다른 COM 기능, 기존 완성 소스·추적기·과거 통과 기록의 복사, 실제 Broker 연결 시험과 COM-03 완료 판정이다. 사용자가 추가 요청한 Docker 파일 준비와 Compose 모델 검사까지 수행했으며, 새 Broker 실제 기동·MQTT 연결 시험은 후속 범위다. 제외된 시험을 이번 Issue의 미충족 완료조건으로 표시하지 않는다.

최소 .NET 구성은 Issue #2에 명시된 대상 파일·버전·완료조건을 따른다. 사용자의 추가 요청으로 프로젝트 전용 Docker 설정을 준비했고, 작업·실행 기준은 `C:\amr-control-system-demo`로 확정했다. 기존 Broker 재사용을 전제로 한 Issue 본문과의 범위 차이 및 로컬 검사 결과는 [Docker 로컬 검증결과](evidence/setup/20261007-docker-local-01/README.md)에 기록한다. 현재 문서 원본은 `C:\process-plan`이며, 읽은 문서의 버전과 Issue 참조 사이의 차이는 [적용 기준](evidence/setup/20261007-setup-02/README.md#1-목표와-적용-기준)에 기록했다. Issue가 참조한 과제계획 v0.10과 DEMO-SETUP v0.1은 현재 문서 폴더에서 확인하지 못했다.

## 복원·빌드·실행

**Git Bash는 명령을 입력하는 터미널이다.** `git`은 버전 관리, `dotnet`은 C# 빌드·실행·자동시험, `docker`는 Broker 컨테이너 관리에 사용한다. 명령 앞에 `git`을 붙이지 않는다.

Git Bash에서 저장소로 이동한 뒤 다음 명령을 순서대로 실행한다. SDK 10.0.401과 NuGet 접근이 필요하다.

```bash
cd /c/amr-control-system-demo
dotnet restore AmrControlSystem.slnx
dotnet build AmrControlSystem.slnx --no-restore
dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-build
```

`dotnet test`는 작성한 xUnit 자동시험 547개를 실행한다. 검사기 직접 호출과 시험 대역(Fake)을 사용하므로 Broker 없이 실행할 수 있다. `--list-tests`를 붙이면 실행하지 않고 이름만 나열한다. `--no-build`는 빌드를 생략하므로 소스를 수정했다면 먼저 다시 빌드한다.

<a id="com01b-run"></a>

## COM01-B 여러 AMR의 네 유형 구독·전달 (Issue #21)

MQTT 파일은 `Mqtt/Connection`(접속·설정)과 `Mqtt/Subscriptions`(구독·진단)으로 나눈다. namespace는 `Amr.Communication.Mqtt`를 유지한다. [폴더 이동 검증결과](evidence/COM-01/20261019-mqtt-layout-02/README.md)에 내용 동일성과 이동 후 빌드·529건 회귀를 기록했다. 이전 실행 증거의 경로는 시험 당시 기록으로 보존한다.

[MessageSubscription](src/Amr.Communication/Mqtt/Subscriptions/MessageSubscription.cs)은 한 MQTT Client에 한 번 연결한다. `amr/+/heartbeat`, `amr/+/telemetry`, `amr/+/alarm`, `amr/+/ack` 네 필터의 QoS 0 승인을 모두 확인한 뒤 전달을 시작한다. 기존 HeartbeatSubscription API는 유지하지만 현재 콘솔에서는 함께 연결하지 않는다.

전체 원본 바이트를 기존 Validator에 전달한다. 정상 결과는 [ReceivedMessage](src/Amr.Communication/Models/ReceivedMessage.cs)의 `AmrId`(Topic 대상), `Kind`, `Model`(유형별 기존 모델), `ReceivedAtUtc`로 전달한다. id 없는 ACK는 대상 AmrId만 Topic에서 얻고 `AckMessage.Id == null`과 FAILURE·선택 reason을 그대로 보존한다.

호출 순서는 **접속 → SubscribeAsync → 시험 발행 → StopAsync → 연결 해제·Client Dispose**다. 구독 실패·취소 때도 StopAsync로 시도한 네 필터와 이벤트를 정리한다. 수명주기를 동시에 호출하거나 객체를 재사용하지 않는다. 콜백은 짧은 동기 함수로 작성하고 async void·수명주기 재호출을 피한다. 느린 콜백의 다른 AMR 영향은 이번 범위에서 격리하지 않는다.

검사 실패는 `Rejected Code=... Field=... Reason=...`, 수신·거부·진단 콜백 예외는 `CallbackError Stage=... ExceptionType=...`로 구분한다. 실패 Payload·예외 메시지는 이 오류 기록에 포함하지 않고 재전달하지 않는다. 오류 보고 콜백 자체의 예외는 밖으로 전파되므로 콜백에서 처리해야 한다. `MessageDiagnostic`는 기본 원문을 숨기며 `--show-payload`에서도 정상 시험 입력만 앞 256바이트를 보여 준다. `Received`에는 정상 모델 필드가 기록되므로 비밀정보 없는 시험 데이터를 사용한다.

기존 개발 Broker가 실행 중인 상태에서 PowerShell로 수신 콘솔을 연다.

```powershell
dotnet run --project src/Amr.Communication.Console --no-build -- --host 127.0.0.1 --port 1884 --show-payload
```

`Subscribed TopicFilters=... QoS=0`을 확인한 뒤 다른 터미널에서 발행한다. 컨테이너 CLI는 1883, 호스트 콘솔은 1884다.

```powershell
docker compose -f docker/compose.yaml exec -T mosquitto mosquitto_pub -h 127.0.0.1 -p 1883 -V mqttv5 -q 0 -t amr/AMR_001/ack -m '{"cmdId":"CMD_1","result":"FAILURE","ts":123}'
```

콘솔 `Received` JSON에서 AmrId=AMR_001, Kind=ack, Model.Id=null, Result=FAILURE, Reason=null, Timestamp=123을 확인한다. JSON 안의 두 ReceivedAtUtc는 동일하고 앞선 MessageDiagnostic 시각과 일치한다. 줄 맨 앞은 별도 로그 작성 시각이다. Ctrl+C 후 SubscriptionStopped → Disconnected → Disposed → ExitCode=0을 확인한다.

두 AMR·불량 입력·새 세션까지 자동 재현하려면 위 수신 콘솔을 종료하고 다음을 실행한다. PowerShell 7·Windows·Docker와 빌드된 Debug 실행 파일이 필요하며 도구는 Broker를 시작하거나 재시작하지 않는다.

```powershell
pwsh -NoProfile -File tests/manual/COM01-B/Verify-Mqtt.ps1 -RunId <새-실행ID>
```

[시험 입력](tests/manual/COM01-B/inputs.json)과 [검증 도구](tests/manual/COM01-B/Verify-Mqtt.ps1)를 사용한다. 기존 실행 ID를 거부하며 새 evidence/COM-01/실행ID에 기록한다. [완료조건별 상세 결과](evidence/COM-01/20261019-multi-mqtt-03/README.md)와 [브라우저 보고서](evidence/COM-01/20261019-multi-mqtt-03/report.html)를 확인한다. 브라우저 Ctrl+O로 report.html을 열면 ① 유형별 전달 ② ACK ③ 오류 경계 ④ 정리 ⑤ 회귀 ⑥ 남은 게시 절차를 각각 확인할 수 있다. 이 HTML은 이번 실행의 고정 보고서이며 실시간 GUI가 아니다.

개발 시험 설정은 MQTT 5·QoS 0·Retain=false·구독 시 보관 메시지 재전송 안 함이다. 로컬 이벤트당 한 번 호출을 네트워크 무손실·exactly-once로 해석하지 않는다. AMR별 큐(COM01-C), Core 업무, Timeout·재접속·운영 QoS 정책은 이번에 추가하지 않았다.

<a id="telemetry-validation"></a>

## COM04-B Telemetry 검사 (Issue #18, 첫 단계)

[TelemetryValidator](src/Amr.Communication/Validation/Telemetry/TelemetryValidator.cs)는 `amr/{amrId}/telemetry`와 전체 Payload, 수신 UTC 시각을 입력받는다. [공통 검사](src/Amr.Communication/Validation/Common/MessageValidation.cs)는 Heartbeat와 같은 크기 → Topic → strict UTF-8 → JSON object → 필드 경계를 적용한다. 제한은 65,536 bytes·깊이 16이며 첫 실패 한 건을 Code·Field·원문 없는 Reason으로 반환한다. 기존 Heartbeat 공개 API와 오류 순서·메시지는 유지했다.

검사 코드는 `Validation/Common`(공통 검사·오류), `Validation/Heartbeat`, `Validation/Telemetry`, `Validation/Alarm`, `Validation/Ack`(유형별 검사기·결과)으로 묶는다. namespace는 모두 `Amr.Communication.Validation`을 유지하고 수신 모델은 `Models`에 둔다. [폴더 정리 검증결과](evidence/COM-04/20261019-validation-layout-01/README.md)에서 ACK 추가 전 이동 당시 내용 동일성과 빌드·회귀 시험을 확인한다.

필수 필드는 `id, ts, pos{x,y}, sp, state`다. `pos`는 x/y만 허용하고 두 값은 유한 double이며 음수 좌표도 허용한다. 속도 sp는 유한 double·0 이상, state는 `IDLE/BUSY/STOPPED/FAULT`다. ts는 signed Int64 정수 표기만 허용하고 원래 값을 보존한다. 개발 단위는 위치 m·속도 m/s다. 정상 [TelemetryMessage](src/Amr.Communication/Models/TelemetryMessage.cs)는 검사한 필드와 전달받은 수신 시각을 보존한다.

라이브러리를 참조한 C# 코드에서 MQTT 없이 직접 호출하는 예다.

```csharp
using System.Buffers;
using System.Text;
using Amr.Communication.Validation;

var validator = new TelemetryValidator();
const string json = """{"id":"AMR_001","ts":1791195767000,"pos":{"x":1.2,"y":-3.4},"sp":0.5,"state":"IDLE"}""";
var result = validator.Validate("amr/AMR_001/telemetry",
    new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(json)), DateTimeOffset.UtcNow);
// result.IsValid == true, result.Message에 정상 모델, result.Error == null.
```

위 예에서 sp만 `-0.5`로 바꾸면 `InvalidValue / $.sp`, 정상 모델 없음이 예상 결과다. 비유한 숫자 범위, 필수 필드·자료형·허용값·ID·추가/중복 키 오류와 오류 후 정상 처리의 정확한 입력·예상값은 [Telemetry 시험](tests/Amr.Communication.Tests/TelemetryValidatorTests.cs), 기존 오류 이유·우선순위 대조는 [공통 회귀 시험](tests/Amr.Communication.Tests/MessageValidationRegressionTests.cs)을 따른다. 재현은 위 빌드·전체 자동시험 명령을 사용하며 TRX를 수집할 때는 기존 실행 ID를 덮어쓰지 않는다.

이번 결과는 검사기 직접 호출과 기존 Fake 회귀다. 콘솔 구독은 Heartbeat 한 종류로 유지되며 실제 Telemetry·Alarm·ACK 수신·다중 Topic 분기는 COM01-B에서 연결한다. 코드 구현·자동시험 완료를 실제 MQTT 연동·#18 전체 절차·COM-04 전체·과제1 완료로 확대하지 않는다. 커밋·푸시·PR은 아직 수행하지 않았다.

<a id="alarm-validation"></a>

## COM04-B Alarm 검사 (Issue #18, 두 번째 단계)

[AlarmValidator](src/Amr.Communication/Validation/Alarm/AlarmValidator.cs)는 `amr/{amrId}/alarm`과 전체 Payload, 수신 UTC 시각을 받는다. 공통 입력 경계 뒤에 필드 구성 → id → ts → code → level → alarmState → message 순서로 검사하며, 첫 실패 한 건을 원문 없는 Code·Field·Reason으로 반환한다.

필수 필드는 `id, ts, code, level, alarmState`, 선택 필드는 `message`다. `level`은 `INFO/WARN/ERR/FATAL`, `alarmState`는 `ACTIVE/CLEARED`만 허용한다. `code`와 존재하는 `message`는 문자열이어야 하고 null·빈 문자열·공백뿐인 문자열을 거부한다. 유효한 문자열은 Trim이나 Unicode 정규화 없이 보존한다. `message` 생략만 모델의 null로 표현한다. `ts`는 Int64 정수 표기를 검사하고 자동 단위 변환 없이 보존한다.

```csharp
using System.Buffers;
using System.Text;
using Amr.Communication.Validation;

var validator = new AlarmValidator();
const string json = """{"id":"AMR_001","ts":1791195767000,"code":"A01","level":"WARN","alarmState":"ACTIVE","message":" 진동 경고 "}""";
var result = validator.Validate("amr/AMR_001/alarm",
    new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(json)), DateTimeOffset.UtcNow);
// result.IsValid == true, result.Message.Message == " 진동 경고 ".
```

`message` 키를 생략하면 정상 모델의 Message는 null이다. 키가 있는 상태에서 null을 넣으면 `InvalidType / $.message`, 공백 문자열이면 `InvalidValue / $.message`로 거부한다. [Alarm 시험](tests/Amr.Communication.Tests/AlarmValidatorTests.cs) 117건과 Alarm까지 확장한 [공통 회귀 시험](tests/Amr.Communication.Tests/MessageValidationRegressionTests.cs)을 포함해 전체 357건을 통과했다. [실행 근거](evidence/COM-04/20261019-alarm-01/README.md)에 환경·소스 해시·입력/예상·실제 결과를 연결했다.

정상 [AlarmMessage](src/Amr.Communication/Models/AlarmMessage.cs)는 알람 정보를 보존한다. 실제 AMR 주행 상태 변경·알람 업무 처리·큐 적재·MQTT 구독은 수행하지 않는다. 후속 ACK 결과는 아래 안내를 따른다.

<a id="ack-validation"></a>

## COM04-B ACK 검사 (Issue #18, 세 번째 단계)

[AckValidator](src/Amr.Communication/Validation/Ack/AckValidator.cs)는 `amr/{amrId}/ack`와 전체 Payload, 수신 UTC 시각을 받는다. 공통 입력 경계 뒤에 필드 구성 → 존재하는 id → ts → cmdId → result → 존재하는 reason 순서로 검사한다. 필수 필드는 `cmdId, result, ts`, 선택 필드는 `id, reason`이다.

Topic ID는 항상 검사하고 Payload id는 존재할 때만 Topic ID와 정확히 대조한다. `cmdId`와 존재하는 `reason`은 문자열이어야 하며 null·빈 문자열·공백뿐인 문자열을 거부한다. 선택 필드는 생략 시 모델에서 null로 표현하고, 명시적인 JSON null은 거부한다. `result`는 `SUCCESS/FAILURE`만 허용하고 `ts`는 Int64 정수 표기를 검사해 원래 값을 보존한다.

```csharp
using System.Buffers;
using System.Text;
using Amr.Communication.Validation;

var validator = new AckValidator();
const string json = """{"cmdId":"CMD_001","result":"FAILURE","ts":1791195767000,"reason":" 이동 불가 "}""";
var result = validator.Validate("amr/AMR_001/ack",
    new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(json)), DateTimeOffset.UtcNow);
// result.IsValid == true, result.Error == null.
// result.Message.Result == "FAILURE", Id == null, Reason == " 이동 불가 ".
```

`IsValid`는 수신 계약에 맞는다는 뜻이다. 유효한 FAILURE도 정상 [AckMessage](src/Amr.Communication/Models/AckMessage.cs)로 반환하며 SUCCESS로 바꾸거나 폐기하지 않는다. `cmdId`는 모델의 `CommandId`로, 선택 필드의 생략과 유효한 문자열의 공백·Unicode는 그대로 보존한다. 생략한 id를 Topic ID로 채우지 않는다. 실제 명령 대조·업무 결과 판단·재시도는 Core 및 후속 과제의 책임이다.

[ACK 시험](tests/Amr.Communication.Tests/AckValidatorTests.cs) 106건과 네 유형의 [공통 오류 회귀](tests/Amr.Communication.Tests/MessageValidationRegressionTests.cs)를 포함한 전체 463건을 통과했다. [실행 근거](evidence/COM-04/20261019-ack-01/README.md)에 입력·예상·실제·환경·코드 해시를 연결했다. 다음 단계는 이번 변경 리뷰와 요청된 커밋·PR 절차다. 실제 MQTT·큐·Core·GUI 시험은 이번 결과에 포함하지 않는다.

<a id="com04-run"></a>

## COM-04 Heartbeat 검사·정상 모델 전달 (Issue #17)

현재 콘솔은 `amr/+/heartbeat`의 구독 승인 후 전체 Payload를 `HeartbeatValidator`에 넘긴다. 크기(최대 65,536 bytes) → Topic → strict UTF-8 → JSON(object, 깊이 최대 16) → 필드 계약 순으로 검사하며, 처음 발견한 실패 한 건을 반환한다. `id`, `ts`, `status`만 허용하고 누락·추가·중복·잘못된 자료형을 거부한다. `ts`는 signed Int64 정수 표기만 허용하며 단위 추측·날짜 변환 없이 보존한다. `status`는 `OK`만 허용하며 Online/Offline 판정은 하지 않는다.

성공 시 [HeartbeatMessage](src/Amr.Communication/Models/HeartbeatMessage.cs)에 ID·ts·status·MQTT 콜백의 UTC 수신 시각을 담아 시험용 수신자에 한 번 전달한다. 이 수신자는 콘솔 출력으로 실제 Core를 대신한다. 실패 시 `Rejected Code=... Field=... Reason=...`만 기록하고 정상 수신자는 호출하지 않는다. 수신자 자체의 예외는 별도 `HeartbeatReceiverError`로 기록하며 재전달하지 않고 다음 입력을 처리한다. 로그 콜백 자체의 장애 복구는 이번 검증 범위가 아니다.

`HeartbeatDiagnostic`는 진단용 정보다. 기본 원문은 숨기며 `--show-payload`를 켰을 때도 **정상 시험 입력만** 앞 256바이트를 보여 준다. 거부 입력은 항상 원문을 숨긴다. 검사기는 이 미리보기를 사용하지 않는다. 미리보기 끝의 UTF-8 문자가 잘려 대체문자로 보이더라도 전체 원본 바이트로 내린 검사 결과와는 별개다.

실제 Broker가 실행 중인 PowerShell에서 다음을 수행한다. 현재 확인한 Docker CLI는 사용자 설치 경로이며, 다른 PC에서는 `Get-Command docker`로 위치를 확인한다.

```powershell
Set-Location C:\amr-control-system-demo
$dockerCli = 'C:\Users\Admin\AppData\Local\Programs\DockerDesktop\resources\bin\docker.exe'
& $dockerCli compose -f docker/compose.yaml ps -a
dotnet run --project src/Amr.Communication.Console --no-build -- --host 127.0.0.1 --port 1884 --show-payload
```

`Subscribed` 이후 다른 PowerShell에서 오류 → 정상 순서로 발행한다. 호스트 수신 포트는 1884, 컨테이너 내부 발행 포트는 1883이다. QoS 0, `-r` 생략으로 Retain=false, 실행별 Client ID를 사용한다.

```powershell
Set-Location C:\amr-control-system-demo
$dockerCli = 'C:\Users\Admin\AppData\Local\Programs\DockerDesktop\resources\bin\docker.exe'
$bad = '{"id":"AMR_001","status":"OK"}'
$good = '{"id":"AMR_001","ts":1791195767000,"status":"OK"}'
foreach ($payload in @($bad, $good)) {
    & $dockerCli compose -f docker/compose.yaml exec -T mosquitto mosquitto_pub -h 127.0.0.1 -p 1883 -V mqttv5 -i "com04-pub-$([Guid]::NewGuid().ToString('N'))" -q 0 -t amr/AMR_001/heartbeat -m $payload
    if ($LASTEXITCODE -ne 0) { throw '발행 실패' }
}
```

예상 출력은 `Rejected Code=MissingField Field=$.ts` 1건과 `Received Heartbeat Id="AMR_001" ts=1791195767000 status="OK" ReceivedAtUtc=...` 1건이다. 두 입력 모두 JSON 문법은 유효하지만 첫 입력은 필수 필드 계약을 위반한다. 고정 ts는 전달 확인용이며 현재 생존 증거가 아니다. QoS 0 발행 종료 0만으로 성공을 판정하지 않고 실제 수신 출력을 확인한다. 수신 콘솔에서 Ctrl+C 후 `SubscriptionStopped → Disconnected → Disposed → ExitCode=0` 및 `$LASTEXITCODE`를 확인한다.

**2026-10-19 실제 결과:** 자동시험 113개 통과(실패·건너뜀 0). 실제 Broker에 ts 누락 → 정상 → ID 불일치 → 349바이트 정상 입력을 발행해 거부 2건·정상 모델 2건, 원본 필드·수신 시각 보존, 거부 원문 숨김, Windows CTRL_C_EVENT 후 실제 프로세스 종료 0과 Broker 해제 로그를 확인했다. [입력·수신 로그](evidence/COM-04/20261019-mqtt-05/), [자동시험](evidence/COM-04/20261019-integration-03/tests.trx), [최종 판정·실패 이력·미검증 범위](evidence/COM-04/20261019-final-06/README.md).

학습 답변은 시험 실적에 포함하지 않는다. 사용자 키보드 직접 종료·실제 Core 업무·GUI·Telemetry/Alarm/ACK·다중 Topic·큐·Timeout·자동 재접속은 검증하지 않았다. 다른 유형 검사는 별도 #18, 다중 Topic 연결은 COM01-B 범위다. COM01-A의 후속 시험 도구 보완은 수행하지 않았다.

<a id="heartbeat-run"></a>

## COM-01 Heartbeat 구독·수신 (Issue #15, 선행 이력)

아래는 2026-10-12 당시의 검사 전 진단 구현·시험 기록이다. 현재 검사·모델 전달과 실행 방법은 위 [COM-04 안내](#com04-run)를 따른다.

현재 콘솔은 접속 후 `amr/+/heartbeat` 하나를 QoS 0으로 구독한다. Broker의 SUBACK에서 QoS 0 승인을 확인한 뒤 `Subscribed`를 표시한다. 아래는 PowerShell에서 프로젝트 전용 Broker가 실행 중일 때 수행하는 순서다.

```powershell
Set-Location C:\amr-control-system-demo
docker compose -f docker/compose.yaml ps -a
dotnet run --project src/Amr.Communication.Console --no-build -- --host 127.0.0.1 --port 1884 --show-payload
```

`Subscribed TopicFilter=amr/+/heartbeat QoS=0`가 나온 뒤 다른 PowerShell에서 아래 명령을 실행한다. `--show-payload`는 **비밀정보 없는 시험 입력 전용**이다. 기본값은 원문 숨김이며, 옵션을 켜도 최대 256바이트만 UTF-8 진단 미리보기로 표시한다. 초과 시 `PayloadTruncated=True`, 제어문자·개행은 이스케이프한다. 잘린 UTF-8 문자는 대체문자로 보일 수 있다. 이것은 메시지 검사기가 아니다.

```powershell
Set-Location C:\amr-control-system-demo
$sample = '{"id":"AMR_001","ts":1791195767000,"status":"OK"}'
docker compose -f docker/compose.yaml exec -T mosquitto mosquitto_pub -h 127.0.0.1 -p 1883 -V mqttv5 -i "hb-pub-$([Guid]::NewGuid().ToString('N'))" -q 0 -t amr/AMR_001/heartbeat -m $sample
$LASTEXITCODE
```

호스트 콘솔은 **1884**, 컨테이너 내부 Mosquitto 명령은 **1883**을 사용한다. 발행에 `-r`을 넣지 않아 Retain=false다. 구독 시 저장된 retained 메시지의 재전송도 요청하지 않는다. 정상 입력은 공통데이터명세 v0.4의 `COM-01-Heartbeat-v0.2` 예제다. 고정 `ts`를 현재 생존 증거로 해석하지 않는다.

수신 콘솔의 `HeartbeatDiagnostic`에서 Topic, UTC 수신 시각, `PayloadBytes=49`, 원문 미리보기, `Validation=NotPerformed`를 확인한다. `status=OK`는 이 단계에서 생존 판정으로 사용하지 않는다.

비구독 Topic을 확인하려면 위 발행 명령의 Topic만 `amr/AMR_001/telemetry`, `amr/AMR_001/heartbeat/extra`, `amr/area/AMR_001/heartbeat`로 각각 바꾼다. 해당 Topic의 진단은 나오지 않아야 한다. 이후 정상 Topic을 다시 발행하면 수신이 계속 보여야 한다. QoS 0이므로 발행 종료 0만으로 수신 성공을 판정하지 않고 수신 로그를 함께 확인한다.

수신 콘솔에서 **Ctrl+C**를 누르면 `ShutdownRequested → Unsubscribing → SubscriptionStopped → Disconnecting → Disconnected → Disposed → ExitCode=0` 순서로 끝난다. 직후 `$LASTEXITCODE`를 확인한다. 같은 실행 명령을 다시 시작하면 새 Client ID로 구독하고 메시지를 다시 받을 수 있다. 옵션 없이 실행하면 수신 정보는 표시하고 `Payload=<hidden>`으로 원문을 숨긴다.

**2026-10-12 직접 확인:** 빌드 경고/오류 0, 자동시험 18개 통과. 실제 Broker에 6건 발행·독립 관찰, 두 콘솔 실행에서 Heartbeat 총 3건 수신·비구독 Topic 3건 제외. 두 실행 모두 Windows Ctrl+C 이벤트 후 실제 프로세스 종료 0·서로 다른 Client ID·Broker 해제 로그를 확인했다. [최종 검증결과](evidence/COM-01/20261012-heartbeat-05/README.md), [브라우저 보고서](evidence/COM-01/20261012-heartbeat-05/report.html).

브라우저에서 **Ctrl+O**로 `C:\amr-control-system-demo\evidence\COM-01\20261012-heartbeat-05\report.html`을 연다. 보고서는 저장된 결과이며 MQTT 포트 `127.0.0.1:1884`는 웹페이지 주소가 아니다. 아래의 COM-03 실행 기록은 선행 작업 당시 결과로 보존한다.

Telemetry·Alarm·ACK 처리, JSON 검사·모델 전달, AMR 생존 갱신·Timeout, 자동 재접속은 이번 구현에 포함하지 않았다. 기존 실행 기록의 미커밋·PR 미생성 표시는 시험 당시 상태이며, 후속 게시 상태와 검증 한계는 연결된 PR을 따른다.

PR 준비 시 시험 당시 이후 `HeartbeatSubscription.cs`에 추가된 학습용 주석을 보존했고 실행 로직 변경은 없음을 본문 대조로 확인했다. 기존 소스·설정 15개 중 다른 14개의 SHA-256은 시험 기록과 일치한다. 현재 소스로 빌드·자동시험을 다시 실행하려던 권한 요청은 승인되지 않아 재시험은 미실행이다. 위 18개 통과·실제 MQTT 결과는 기존 실행 기록이며 사용자 직접 실행 확인으로 표시하지 않는다.

실제 MQTT·Ctrl+C 수동 시험은 프로젝트 Broker가 실행 중일 때 다음과 같이 한다.

```bash
docker compose -f docker/compose.yaml ps -a
dotnet run --project src/Amr.Communication.Console --no-build -- --host 127.0.0.1 --port 1884 --connect-timeout-seconds 3
```

`Connected → WaitingForShutdown`을 확인하고 잠시 기다린다. 이때 콘솔은 종료되지 않아야 한다. **키보드에서 Ctrl+C**를 누르면 `ShutdownRequested → Disconnecting → Disconnected → Disposed → ExitCode=0` 순서로 마친다. 프롬프트로 돌아오면 다른 명령보다 먼저 다음을 입력해 직전 명령의 종료 코드를 확인한다.

```bash
echo $?
docker compose -f docker/compose.yaml logs --no-color --timestamps --tail 50 mosquitto
```

Broker 로그의 Client ID와 이번 콘솔의 ID를 대조한다. 다시 실행하면 다른 ID로 연결되어야 한다. Git Bash의 `$?`는 직전 명령의 결과이므로 Docker 조회 후 확인하면 콘솔 결과가 아니다. PowerShell에서는 이동에 `Set-Location C:\amr-control-system-demo`, 종료 코드 확인에 `$LASTEXITCODE`를 사용한다.

인자는 생략 가능하며 기본값은 `--host 127.0.0.1 --port 1884 --connect-timeout-seconds 3`이다. 가운데 `--` 앞은 `dotnet run`의 옵션이고 뒤는 우리가 만든 프로그램에 전달할 인자다. MQTT 5, Clean Start, Session Expiry 0, KeepAlive 5초, 해제 제한 3초를 적용한다. Client ID는 실행마다 `amr-demo-<GUID>`로 생성하고 설정·UTC 시각을 출력한다. 최초 접속 제한과 KeepAlive는 별개다. `--help`로 사용법을 확인한다.

**2026-10-11 실제 결과:** 복원·빌드 성공(경고 0·오류 0), 자동시험 6개 통과. 실제 Broker에 별도 프로세스 두 번을 연결해 Ctrl+C 이벤트 전 유지·명시적 해제·정리·네이티브 프로세스 종료 코드 0, 서로 다른 ID와 Broker 로그를 확인했다. 포트 1의 접속 실패는 `Connected` 없이 정리·종료 1이었다. [A3 최종 검증결과](evidence/COM-03/20261011-a3-shutdown-03/README.md)에 코드 버전과 증거를 연결한다. 도구의 외부 셸 종료 코드와 앱 종료 코드를 구분한 시험 한계도 기록했다.

[A2 중간 검증결과](evidence/COM-03/20261011-a2-connect-02/README.md)에는 접속 직후 해제했던 당시 소스·해시·실제 실행을 보존한다. 현재 코드는 A3이므로 A2 동작을 재현하려면 그 스냅샷을 사용한다.

먼저 읽을 파일은 [Program.cs](src/Amr.Communication.Console/Program.cs), [MqttConnection.cs](src/Amr.Communication/Mqtt/Connection/MqttConnection.cs), [MqttConnectionSettings.cs](src/Amr.Communication/Mqtt/Connection/MqttConnectionSettings.cs)다. 콘솔이 순서를 정하고 통신 객체가 MQTT 호출을 맡는다. `await` 뒤의 문장은 해당 작업이 끝나야 실행된다.

A3에서는 `Console.CancelKeyPress` 처리기가 기본 즉시 종료를 막고 `shutdown.Cancel()`로 취소를 요청한다. `Task.Delay(Timeout.Infinite, shutdown.Token)` 대기가 취소되면 `catch`를 거쳐 `finally`에서 해제·정리를 수행한다. 이미 취소한 토큰을 `DisconnectAsync`에 넘기지 않아 해제 기회를 보장한다. `finally`는 이런 제어 흐름에서 정리를 수행하지만 강제 프로세스 종료·전원 차단까지 보장하지 않는다.


현재 PC의 Docker CLI는 `C:\Program Files\Docker\Docker\resources\bin\docker.exe`이며 `docker`가 PATH에 등록돼 있다. `docker compose -f docker/compose.yaml ps -a`와 `docker compose -f docker/compose.yaml logs --tail 30 mosquitto`로 현재 상태를 조회한다. 아래 2026-10-07~08의 준비·A1 기록은 당시 코드·환경에 대한 이력이다.

**준비 콘솔 재시험 이력:** 2026-10-07 16:04에 당시 준비 콘솔 명령으로 안내 한 줄과 종료 코드 `0`을 확인했다. [재시험 결과](evidence/setup/20261007-setup-03/README.md)와 [콘솔 로그](evidence/setup/20261007-setup-03/console.log)를 확인한다. 이전 `0x800711C7` 실패는 [당시 실행 로그](evidence/setup/20261007-setup-02/console.log)와 [정책 차단 이벤트](evidence/setup/20261007-setup-02/code-integrity.json)에 보존한다. 사용자가 차단을 해결했다고 보고한 뒤 재시험했으며, 구체적인 해결 방법·정책 변경 내용은 확인하지 않았다.

준비 단계 당시 `--list-tests`의 탐색 결과는 0건이었다. 현재 A3의 자동시험 6개와 구분한다.

## 프로젝트 전용 Docker 설정

설정은 [docker/compose.yaml](docker/compose.yaml)과 [mosquitto.conf](docker/mosquitto/config/mosquitto.conf)에서 관리한다. 설치된 Docker Desktop을 사용하며 새 Broker를 위한 설정·데이터·로그 경로는 `C:\amr-control-system-demo\docker\` 안에 둔다.

| 항목 | A1 시험 당시 설정 |
| --- | --- |
| Compose 프로젝트 / 서비스 | `amr-demo` / `mosquitto` |
| 이미지 | `eclipse-mosquitto:2.1.2-alpine` |
| Windows 호스트에서 접속할 주소 | `127.0.0.1:1884` → 컨테이너 `1883/tcp` |
| 인증 범위 | 로컬 개발용 익명 접속. 호스트 공개 주소는 `127.0.0.1` |
| 설정 파일 | `docker/mosquitto/config/mosquitto.conf`, 읽기 전용 마운트·Git 추적 |
| 실행 데이터 / 로그 | `docker/mosquitto/data/`, `docker/mosquitto/log/`, 최초 기동 시 생성·Git 제외 |
| 자동 재시작 | 사용 안 함. 명시적으로 실행할 때만 기동 |

모든 마운트의 호스트 경로는 Compose 파일 기준 상대 경로다. 설정 파일이 없으면 디렉터리를 대신 만들지 않도록 구성했다. `amr-demo`라는 이름으로 기존 Compose 프로젝트와 구분하며, 기존 `amr-mosquitto`의 1883 포트와 겹치지 않는다. [Compose 경로·포트 규칙](https://docs.docker.com/reference/compose-file/services/) · [프로젝트 이름](https://docs.docker.com/compose/how-tos/project-name/)

2026-10-08 현재 PC에서는 `docker`가 PATH에서 확인된다. 아래 명령은 확인한 절대 경로를 사용한다. 다른 PC에서는 실제 Docker CLI 경로나 PATH에 등록된 `docker`를 사용한다.

```powershell
Set-Location C:\amr-control-system-demo
$dockerCli = 'C:\Program Files\Docker\Docker\resources\bin\docker.exe'
& $dockerCli version
& $dockerCli context show
& $dockerCli compose version
& $dockerCli compose -f docker/compose.yaml config --quiet
$LASTEXITCODE
```

위 설정 검사의 예상 종료 코드는 `0`이다. `compose config`는 Compose 모델을 검사하며 Broker를 시작하거나 Mosquitto 설정의 실제 로딩·MQTT 통신을 시험하지 않는다. [명령 설명](https://docs.docker.com/reference/cli/docker/compose/config/)

Docker 엔진에 연결되지 않으면 설치된 Docker Desktop을 시작하고 `version`에 Server가 표시되는지 확인한다. 이번 기록 시작 전에는 엔진 파이프 연결 오류가 있었으나, 실행 증거 수집 시에는 이미 응답했다. `& $dockerCli desktop start --timeout 45`는 `Docker Desktop is already running`과 종료 0을 반환했다. 새 도구·이미지를 설치하거나 내려받지 않았다.

다음은 [Issue #7 · A1](https://github.com/inhhlee/amr-control-system-demo/issues/7)에서 실제 수행한 기동·상태·로그·중단·재기동 순서다. 각 명령의 종료 코드는 바로 뒤에 `$LASTEXITCODE`로 확인한다. 명령이 실패하면 원인을 확인하기 전 다음 단계로 진행하지 않는다. `ps -a`는 중단된 컨테이너도 표시한다.

```powershell
& $dockerCli compose -f docker/compose.yaml up -d mosquitto
& $dockerCli compose -f docker/compose.yaml ps -a
& $dockerCli compose -f docker/compose.yaml logs --no-color --timestamps --tail 100 mosquitto

# 이 프로젝트의 Broker만 중단한다.
& $dockerCli compose -f docker/compose.yaml stop mosquitto
& $dockerCli compose -f docker/compose.yaml ps -a
& $dockerCli compose -f docker/compose.yaml logs --no-color --timestamps --tail 100 mosquitto

# 같은 컨테이너를 다시 기동한다.
& $dockerCli compose -f docker/compose.yaml up -d mosquitto
& $dockerCli compose -f docker/compose.yaml ps -a
& $dockerCli compose -f docker/compose.yaml logs --no-color --timestamps --tail 100 mosquitto
```

예상 결과는 기동 시 `running`과 `127.0.0.1:1884->1883/tcp`, 로그의 `Config loaded from /mosquitto/config/mosquitto.conf.` 및 `mosquitto version 2.1.2 running`이다. 중단 시 `exited (0)`·`terminating`·데이터 저장 로그, 재기동 시 설정 재로딩·실행·포트 공개를 확인한다. 기존 사용자 Broker와 다른 서비스는 이 명령의 대상이 아니다.

**2026-10-08 실제 결과:** 시험 당시 설정에서 Compose 검사 종료 0, 기동 → 중단(exit 0) → 같은 컨테이너 재기동을 확인했다. 설정·마운트·포트 오류가 없었고 이번 A1 작업에서 Docker 설정을 수정하지 않았다. 다른 컨테이너 4개의 상태·이미지·마운트도 보존됐다. 16:13 KST 조회 시 프로젝트 Broker는 실행 중이었다.

실행 ID는 **`20261008-a1-broker-01`**, 기준 코드는 `ebaf54641040b97f81cdda0142ab5c7e498fa454` (`feature/7-broker-check`)다. 시험 당시 소스·Docker 설정은 기준 커밋 그대로였으며 README·증거는 이번 미커밋 변경이다. [검증결과와 완료조건](evidence/COM-03/20261008-a1-broker-01/README.md), [명령·시각·종료 코드](evidence/COM-03/20261008-a1-broker-01/commands.jsonl), [재기동 로그](evidence/COM-03/20261008-a1-broker-01/24-logs-restarted.log)를 연결한다. 커밋·푸시·PR 생성과 Issue 종료는 수행하지 않았다.

**시험 후 별도 변경:** 기록 정리 중 `mosquitto.conf`에 다른 작업의 변경이 추가됐다. 사용자 요청으로 그대로 보존했으며 내용 검사·재기동·재시험 대상에 포함하지 않았다. 위 표와 통과 기록은 저장된 SHA-256의 시험본에 대한 결과다. 현재 설정 전체의 통과로 확대하지 않으며, 설정 담당 작업이 끝난 뒤 현재 설정과 적용 상태를 확인해야 한다. 후속 A2 착수 시에도 `ps -a`·`logs`로 현재 상태를 재확인한다.

브라우저 확인은 로컬 [검증 보고서 HTML](evidence/COM-03/20261008-a1-broker-01/report.html)을 열어 완료조건 표와 펼칠 수 있는 원본 증거를 확인한다. GitHub에서 HTML을 클릭하면 소스가 표시되므로, 게시 후에는 위 Markdown 검증결과를 사용한다. `127.0.0.1:1884`는 MQTT 포트이며 HTTP 웹페이지 주소가 아니다. 이 보고서는 저장된 실행 기록으로, 실시간 상태 화면이 아니다.

**범위 구분:** A1의 Broker 실행 준비 검증이며 MQTT 접속·메시지 송수신·C# 연결·Ctrl+C 종료는 미실행이다. A2(#8)·A3(#9)의 기능을 미리 구현하지 않았으며 총괄 #4나 COM-03 전체 완료로 표시하지 않는다.

이 프로젝트의 관리·실행 위치는 `C:\amr-control-system-demo`다. 설정의 상대 경로는 이 로컬 폴더의 `docker/compose.yaml`을 기준으로 해석된다. 데이터·로그도 이 폴더에 생성되며, 검토할 로그는 필요한 부분을 `evidence/`로 옮겨 보관한다.

## 준비 단계에서 확인한 환경과 결과 (2026-10-07)

2026-10-07, Windows 10.0.26200 / x64, PowerShell 7.6.5에서 확인했다.

| 항목 | 실제 확인 결과 |
| --- | --- |
| SDK / 런타임 | .NET SDK 10.0.401 / .NET 10.0.12 |
| Git | 2.55.0.windows.5 |
| 의존성 복원 | 세 프로젝트 성공 |
| 솔루션 빌드 | 성공, 경고 0개·오류 0개 |
| 콘솔 실행 | 16:04 재시험 통과. 준비 안내 한 줄 출력, 종료 코드 0 |
| xUnit 탐색 | 어댑터 3.1.4 로드·탐색 완료, 시험 0건 |
| Docker | 사용자 폴더에서 CLI 확인. Desktop 4.90.0 (238679), Client·Engine 29.7.2, context `desktop-linux` |
| 기존 Broker 조회 기록 | `amr-mosquitto`, 호스트 `127.0.0.1:1883`. 당시 상태는 [이전 환경 조회](evidence/setup/20261007-setup-02/docker-broker.log)에 보존 |
| 새 Broker 설정 | `C:\amr-control-system-demo\docker\compose.yaml`, 접속 주소 `127.0.0.1:1884`. 파일 준비·Compose 모델 검사 통과. 실제 기동·연결은 후속 범위 |

이전 검증은 기존 Broker의 환경 조회 결과다. 현재 프로젝트의 설정·관리 경로는 위 로컬 폴더이며, 새 Broker 실행과 MQTT 통신은 이번 준비 Issue에서 제외된 후속 시험이다. 실제 로컬 경로 해석과 검사 근거는 [Docker 로컬 검증결과](evidence/setup/20261007-docker-local-01/README.md)에 기록한다.

복원·빌드·탐색은 [첫 검증 기록](evidence/setup/20261007-setup-01/README.md)의 실제 결과다. 이번 확인에서 SDK·패키지·솔루션·프로젝트·소스의 SHA-256이 같음을 대조했으므로 성공한 검증을 반복하지 않았다. 당시 환경·정책 차단과 완료조건별 상태는 [추가 검증결과](evidence/setup/20261007-setup-02/README.md)에 보존하고, 최신 콘솔 정상 실행은 [콘솔 재시험 결과](evidence/setup/20261007-setup-03/README.md)에 기록했다.

<a id="com01c-mqtt"></a>

## COM01-C 실제 MQTT 큐 시험

현재 흐름은 `MQTT → MessageSubscription → Validator → TryEnqueue → AMR별 ConsumeAsync → 콘솔 Received`다. 불량 입력은 기존 Rejected로 기록하고, 큐 접수는 QueueAdmission, 처리 실패·취소·폐기는 QueueEvent로 구분한다. 원본 모델과 최초 수신 UTC는 큐에서 다시 만들지 않는다.

```powershell
dotnet build AmrControlSystem.slnx --no-restore
dotnet run --project src/Amr.Communication.Console --no-build -- --host 127.0.0.1 --port 1884 --test-delay-amr AMR_001 --test-delay-ms 5000
```

시험 지연 옵션은 AMR_001의 처리를 건당 5초 기다리게 한다. 그동안 AMR_002도 처리되는지 확인한다. 평소 실행에서는 두 지연 옵션을 생략한다. 처리자는 CancellationToken을 받아 Ctrl+C 때 대기에서 빠져나온다. 이 콘솔은 Core 대역이며 실제 로봇 상태를 바꾸지 않는다.

기존 프로젝트 Broker가 실행 중인 상태에서 다음 재현 도구를 사용한다. 새 실행 ID를 지정해야 하며 기존 증거가 있으면 중단한다. 도구가 시작한 숨겨진 콘솔에만 Windows CTRL_C_EVENT를 보내므로 사용자 터미널은 종료하지 않는다.

```powershell
pwsh -NoProfile -File tests/manual/COM01-C/Verify-Mqtt.ps1 -RunId <새-실행ID>
```

| 실제 시나리오 | 확인 결과 |
| --- | --- |
| 001의 네 유형을 5초씩 지연, 002의 네 유형도 발행, 불량 JSON 1건 | 002가 먼저 처리됨. 각 AMR의 heartbeat → telemetry → alarm → ack 순서 유지. 정상 8건·거부 1건 |
| 001 처리 중 Ctrl+C | 002 정상 1건 전달 후 001 실행 중 1건 취소·대기 3건 폐기. 종료 코드 0 |
| 새 Client ID의 다음 세션 | id 없는 FAILURE ACK 1건 그대로 전달. 이전 큐 메시지 재전달 없음, 종료 코드 0 |

종료는 `RequestStop`으로 접수·다음 처리 시작을 차단하고 잔여를 폐기한 뒤 `StopAsync`로 취소·정리를 기다린다. 큐 대기는 ConnectTimeout(기본 3초), 구독 해제와 연결 해제는 각각 기존 3초 제한이다. 이는 종료 전체가 합계 3초라는 의미가 아니다. 이미 시작한 처리자는 취소에 협조해야 하며, 취소를 무시하면 StopTimeout을 보고하고 반환한 정리 Task에서 늦은 완료·실패를 관찰한다. 모든 소비자와 취소 콜백이 끝난 뒤 취소 자원을 폐기한다.

최신 검증에서는 정상 연결·Ctrl+C·새 세션을 재시험했고, 별도 시험에서 실제 Broker 중단 후 처리 중 1건 취소·대기 3건 폐기·구독/Client 정리·예상 종료 코드 1을 확인했다. [정상 MQTT 결과](evidence/COM-01/20261019-queue-pr-mqtt-01/README.md), [끊김 결과](evidence/COM-01/20261019-queue-pr-disconnect-03/README.md), [547건 자동시험과 완료조건 대조](evidence/COM-01/20261019-queue-pr-unit-02/README.md)를 참조한다. 실제 MQTT 과부하·장시간 부하·네트워크 무응답 감지는 미실행이다. 작은 큐/AMR 제한은 실제 MessageSubscription·Validator와 Fake MQTT를 연결해 확인했으며 실제 Broker 시험과 구분한다.

호출 진입용 읽기/쓰기 잠금은 큐 접수 잠금과 분리했다. 이미 꺼낸 메시지도 처리자 호출 전에 종료 상태를 확인하면 폐기하고 수량을 기록한다. 호출자가 반환한 Task를 잠금 밖에서 기다리므로 다른 AMR의 비동기 처리는 계속된다. 취소는 진행 중인 처리자의 동기 구간이 Task를 반환한 뒤 전달된다. 처리자는 동기 구간에서 토큰 취소를 기다리는 `.Wait()`·`.Result` 등으로 멈추지 말고 비동기 Task를 빨리 반환해야 한다. 동기 구간이 반환하지 않아도 StopAsync는 제한 시간에 오류를 반환하고 늦은 반환 후 정리를 이어가며, 이 경계도 자동시험으로 확인했다.

Broker 중단 시험은 다른 사용자가 쓰지 않는 프로젝트 전용 시험 Broker에서만 실행한다. 도구는 `amr-demo/mosquitto`를 중단한 뒤 finally에서 다시 시작하며 다른 Compose 프로젝트는 변경하지 않는다.

```powershell
pwsh -NoProfile -File tests/manual/COM01-C/Verify-Disconnect.ps1 -RunId <새-실행ID> -AllowBrokerStop
```

<a id="com01c-order"></a>

## COM01-C 첫 단계: AMR별 순서와 독립 처리

택배를 **AMR마다 다른 줄에 세우는 것**과 같다. 1호기 줄의 앞 물건을 처리하는 동안 그 뒤 물건은 기다린다. 2호기는 자기 줄에서 처리한다. 같은 AMR의 Heartbeat·Telemetry·Alarm·ACK는 줄을 나누지 않는다.

```text
MessageSubscription → 기존 Validator → 정상 ReceivedMessage → TryEnqueue
                                                        ├─ AMR_001: HB → Telemetry → Alarm → ACK
                                                        └─ AMR_002: ACK → HB
```

이 절은 첫 단계의 이해를 위한 설명이다. 처음에는 FakeMqttClient로 확인했고, 이후 위 실제 MQTT 단계에서 콘솔에도 연결했다.

| 먼저 읽을 코드 | 역할 |
| --- | --- |
| [AmrReceiveQueue.TryEnqueue](src/Amr.Communication/Mqtt/Queues/AmrReceiveQueue.cs) | 동기 작업. AMR ID로 줄을 찾고 메시지를 넣은 뒤 바로 반환한다. 짧은 lock으로 줄 생성·접수 순서를 보호한다. |
| 같은 파일의 ConsumeAsync | 비동기 작업. 한 AMR은 `await _handler(message)`가 끝나야 다음 건을 꺼낸다. 다른 AMR의 소비자는 별도 Task로 진행한다. |
| 같은 파일의 CompleteAsync | 시험 정리용. 접수를 닫고 이미 넣은 메시지를 모두 처리할 때까지 기다린다. 취소·폐기하는 연결 종료 기능은 아직 아니다. |
| [QueueAdmission](src/Amr.Communication/Mqtt/Queues/QueueAdmission.cs) | Accepted·QueueFull·AmrLimitReached·NotAccepting으로 접수 여부를 알려준다. JSON 검사 실패와 업무 처리 성공은 별개다. |
| [AmrReceiveQueueTests](tests/Amr.Communication.Tests/AmrReceiveQueueTests.cs) | 첫 시험에서 001을 대기시킨 채 002 완료를 확인하고, 001을 풀어 네 유형 순서를 대조한다. |

`Task.Run`은 각 소비자를 공유 스레드 풀에서 시작하게 한다. AMR마다 전용 스레드를 만드는 것은 아니다. 처리자의 비동기 대기 중에는 스레드를 붙잡지 않는다. 처리자는 서로 다른 AMR에서 동시에 호출될 수 있으므로 함께 쓰는 값도 안전하게 다뤄야 한다.

기본 대기 256개/AMR·세션 64 AMR 제한과 처리자 예외 뒤 다음 입력 처리를 포함한다. 첫 단계에서 internal이던 큐는 콘솔 연결 시 public으로 바꾸고 취소 토큰을 받는 처리자와 StopAsync를 추가했다. MQTT 수신 콜백에 async 람다를 직접 넣지 않는다.

```powershell
dotnet build AmrControlSystem.slnx --no-restore
dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~AmrReceiveQueueTests
```

첫 단계 당시 9건, 기존 회귀 포함 총 538건이 통과했다. [당시 검증결과](evidence/COM-01/20261019-queue-order-02/README.md)를 보존하며 후속 543건·실제 MQTT 결과는 위 최신 절을 따른다. #23 전체 완료나 실제 로봇 제어 구현으로 확대하지 않는다.

## 준비 단계 당시 확인 절차 (현재 실행은 위 A3 안내 참조)

1. 위 복원·빌드 명령으로 세 프로젝트가 경고·오류 없이 빌드되는지 확인한다.
2. 다음 명령으로 준비 안내 한 줄과 종료 코드 `0`을 확인한다. 16:04 재시험에서 두 조건을 충족했다. 이후 정책 차단이 발생하면 이벤트 뷰어의 `Microsoft-Windows-CodeIntegrity/Operational`에서 해당 DLL의 이벤트 3033·3077을 확인하고 기기 관리 담당자에게 허용된 개발 실행 방법을 확인한다.

   ```powershell
   dotnet run --project src/Amr.Communication.Console --no-build
   $LASTEXITCODE
   ```

3. 위 `--list-tests` 명령이 탐색을 마치는지 확인한다. 시험 0건은 현재 빈 시험 프로젝트의 예상 결과이며 기능 시험 통과가 아니다.
4. Docker 파일은 위 [프로젝트 전용 Docker 설정](#프로젝트-전용-docker-설정)의 명령으로 모델 검사·기동·상태·로그·중단·재기동을 확인한다. A1 실제 결과는 [2026-10-08 검증결과](evidence/COM-03/20261008-a1-broker-01/README.md)와 대조한다. 관리 폴더는 `C:\amr-control-system-demo`, 후속 MQTT 접속 주소는 `127.0.0.1:1884`다. MQTT 접속 시험은 별도 A2 범위다.

Issue #2는 [PR #3](https://github.com/inhhlee/amr-control-system-demo/pull/3)의 main 병합(`963a1fa8d48f432aaa87f07f15a61db57e22db88`)으로 반영됐다. 이번 A1은 시험 당시 설정의 Broker 기동·관리를 검증했으며 이후 별도 설정 변경의 검증과 커밋·푸시·PR 연결은 남아 있다. A1 PR의 develop 반영·근거 확인 후 A2를 진행한다. COM-03 또는 과제1 전체 완료를 뜻하지 않는다.
