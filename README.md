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

현재 변경은 [Issue #17 — COM04-A Heartbeat 검사와 정상 데이터 전달](https://github.com/inhhlee/amr-control-system-demo/issues/17)이다. 검사기·수신부·콘솔을 연결했고 2026-10-19 빌드(경고·오류 0), 자동시험 113개와 실제 Broker의 오류 후 정상 전달·Ctrl+C 종료를 확인했다. [COM-04 실행 안내](#com04-run)와 [최종 검증결과](evidence/COM-04/20261019-final-06/README.md)를 따른다. 아래 COM-01·COM-03 기록은 선행 구현 당시 근거다.

[Issue #15 — COM-01 Heartbeat 한 종류 수신](https://github.com/inhhlee/amr-control-system-demo/issues/15)의 구현·검증은 아래 [Heartbeat 실행 안내](#heartbeat-run)와 [완료조건별 결과](evidence/COM-01/20261012-heartbeat-05/README.md)에서 확인한다. 사용자의 후속 요청으로 기존 `feature/COM-01-heartbeat-subscribe` 브랜치를 게시하는 단계다. 연결된 PR은 Issue #15에서 확인하며 리뷰·develop 병합은 별도 단계다.

[Issue #2](https://github.com/inhhlee/amr-control-system-demo/issues/2)에서 최소 구성을 만들었고, 현재 [Issue #8](https://github.com/inhhlee/amr-control-system-demo/issues/8)의 **A2 접속·해제와 A3 연결 유지·Ctrl+C 정상 종료**를 구현·검증했다. 아래 프로젝트는 모두 `net10.0`을 사용한다. [PR #13](https://github.com/inhhlee/amr-control-system-demo/pull/13)은 2026-10-11 develop에 병합됐다(코드 기준 `c34f380`). 총괄 [Issue #4](https://github.com/inhhlee/amr-control-system-demo/issues/4)의 원래 완료조건 7개와 기존 A1·A2·A3 시험 근거는 [통합 검증결과](evidence/COM-03/20261012-a-summary-01/README.md)에 연결한다. 이 결과는 기존 시험과 병합 코드의 대조이며 제품 재시험·COM-03 전체·과제1 전체 완료를 뜻하지 않는다.

| 파일·프로젝트 | 역할 |
| --- | --- |
| [global.json](global.json) | SDK 10.0.401, `latestPatch`, 시험판 SDK 사용 안 함 |
| [AmrControlSystem.slnx](AmrControlSystem.slnx) | Visual Studio에서 열 솔루션 |
| [Amr.Communication](src/Amr.Communication/Amr.Communication.csproj) | MQTTnet 5.2.0.1603을 사용한 접속 설정·MQTT 승인 확인·명시적 해제·자원 정리 |
| [Amr.Communication.Console](src/Amr.Communication.Console/Amr.Communication.Console.csproj) | 실제 Broker 접속 승인 후 Ctrl+C까지 유지하고 명시적 해제·정리를 기다리는 콘솔 |
| [Amr.Communication.Tests](tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj) | 수명주기 6개 + Heartbeat 구독·전달 경계 21개 + 검사기 86개, 총 113개 |
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

`dotnet test`는 작성한 xUnit 자동시험 113개를 실행한다. 검사기 직접 호출과 시험 대역(Fake)을 사용하므로 Broker 없이 실행할 수 있다. `--list-tests`를 붙이면 실행하지 않고 이름만 나열한다. `--no-build`는 빌드를 생략하므로 소스를 수정했다면 먼저 다시 빌드한다.

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

먼저 읽을 파일은 [Program.cs](src/Amr.Communication.Console/Program.cs), [MqttConnection.cs](src/Amr.Communication/Mqtt/MqttConnection.cs), [MqttConnectionSettings.cs](src/Amr.Communication/Mqtt/MqttConnectionSettings.cs)다. 콘솔이 순서를 정하고 통신 객체가 MQTT 호출을 맡는다. `await` 뒤의 문장은 해당 작업이 끝나야 실행된다.

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
