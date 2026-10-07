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
3. [계획·명세·업무 문서 안내](C:/process-plan/README.md)에서 필요한 담당 문서를 찾고, 문서 작업에는 [문서 저장소 지침](C:/process-plan/AGENTS.md)과 [관제 프로젝트 지침](<C:/process-plan/관제시스템 과제 진행/AGENTS.md>)을 함께 적용한다.
4. [개발 진행 절차](<C:/process-plan/기준/개발 진행 절차.md>)와 사용자가 요청한 단계에 맞춰 진행한다. 작업별 원본 스킬은 [AGENTS.md의 안내](AGENTS.md#작업별-스킬)를 따른다.

## 개발 구성

[Issue #2](https://github.com/inhhlee/amr-control-system-demo/issues/2)의 최소 구성을 이 프로젝트에 만들었다. 아래 프로젝트는 모두 `net10.0`을 사용한다.

| 파일·프로젝트 | 역할 |
| --- | --- |
| [global.json](global.json) | SDK 10.0.401, `latestPatch`, 시험판 SDK 사용 안 함 |
| [AmrControlSystem.slnx](AmrControlSystem.slnx) | Visual Studio에서 열 솔루션 |
| [Amr.Communication](src/Amr.Communication/Amr.Communication.csproj) | 통신 라이브러리와 MQTTnet 5.2.0.1603 참조. 통신 기능은 아직 없음 |
| [Amr.Communication.Console](src/Amr.Communication.Console/Amr.Communication.Console.csproj) | 통신 라이브러리를 참조하고 준비 안내 한 줄을 출력하는 콘솔 |
| [Amr.Communication.Tests](tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj) | 통신 라이브러리 참조와 xUnit 시험 탐색 구성. 기능 시험은 아직 0건 |
| [evidence/setup](evidence/setup/) | 실행별 환경·복원·빌드·실행·시험 탐색 증거 |

시험 패키지는 `Microsoft.NET.Test.Sdk 17.14.1`, `xunit 2.9.3`, `xunit.runner.visualstudio 3.1.4`다.

**이번 준비 Issue의 범위:** 도구·기존 Broker 환경 조회, 최소 프로젝트·참조 구성, 복원·빌드·준비 콘솔 실행·시험 탐색, 협업 규칙과 증거 기록이다. Issue #2의 제외 범위는 MQTT 연결 로직·메시지 송수신·Heartbeat·자동 재접속, GUI·Core·DB·다른 COM 기능, 기존 완성 소스·추적기·과거 통과 기록의 복사, 실제 Broker 연결 시험과 COM-03 완료 판정이다. 사용자가 추가 요청한 Docker 파일 준비와 Compose 모델 검사까지 수행했으며, 새 Broker 실제 기동·MQTT 연결 시험은 후속 범위다. 제외된 시험을 이번 Issue의 미충족 완료조건으로 표시하지 않는다.

최소 .NET 구성은 Issue #2에 명시된 대상 파일·버전·완료조건을 따른다. 사용자의 추가 요청으로 프로젝트 전용 Docker 설정을 준비했고, 작업·실행 기준은 `C:\amr-control-system-demo`로 확정했다. 기존 Broker 재사용을 전제로 한 Issue 본문과의 범위 차이 및 로컬 검사 결과는 [Docker 로컬 검증결과](evidence/setup/20261007-docker-local-01/README.md)에 기록한다. 현재 문서 원본은 `C:\process-plan`이며, 읽은 문서의 버전과 Issue 참조 사이의 차이는 [적용 기준](evidence/setup/20261007-setup-02/README.md#1-목표와-적용-기준)에 기록했다. Issue가 참조한 과제계획 v0.10과 DEMO-SETUP v0.1은 현재 문서 폴더에서 확인하지 못했다.

## 복원·빌드·실행

PowerShell에서 아래 명령을 순서대로 실행한다. .NET SDK 10.0.401과 NuGet 패키지 소스·캐시에 대한 접근이 필요하다.

```powershell
Set-Location C:\amr-control-system-demo
dotnet restore AmrControlSystem.slnx
dotnet build AmrControlSystem.slnx --no-restore
dotnet run --project src/Amr.Communication.Console --no-build
dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-build --list-tests
```

콘솔의 예상 출력은 `AMR 관제시스템 개발 준비가 완료되었습니다.`이고 예상 종료 코드는 0이다. Broker 접속 설정이나 실행 중인 Broker는 이 준비 콘솔에 필요하지 않다.

**콘솔 재시험 통과:** 2026-10-07 16:04에 같은 명령으로 준비 안내 한 줄과 종료 코드 `0`을 확인했다. [재시험 결과](evidence/setup/20261007-setup-03/README.md)와 [콘솔 로그](evidence/setup/20261007-setup-03/console.log)를 확인한다. 이전 `0x800711C7` 실패는 [당시 실행 로그](evidence/setup/20261007-setup-02/console.log)와 [정책 차단 이벤트](evidence/setup/20261007-setup-02/code-integrity.json)에 보존한다. 사용자가 차단을 해결했다고 보고한 뒤 재시험했으며, 구체적인 해결 방법·정책 변경 내용은 확인하지 않았다.

`--list-tests`는 시험을 실행하지 않고 탐색한다. xUnit 어댑터가 로드되어 탐색을 마쳤으며 현재 결과는 0건이다. 제품 기능 시험의 통과를 의미하지 않는다.

## 프로젝트 전용 Docker 설정

설정은 [docker/compose.yaml](docker/compose.yaml)과 [mosquitto.conf](docker/mosquitto/config/mosquitto.conf)에서 관리한다. 설치된 Docker Desktop을 사용하며 새 Broker를 위한 설정·데이터·로그 경로는 `C:\amr-control-system-demo\docker\` 안에 둔다.

| 항목 | 설정 |
| --- | --- |
| Compose 프로젝트 / 서비스 | `amr-demo` / `mosquitto` |
| 이미지 | `eclipse-mosquitto:2.1.2-alpine` |
| Windows 호스트에서 접속할 주소 | `127.0.0.1:1884` → 컨테이너 `1883/tcp` |
| 인증 범위 | 로컬 개발용 익명 접속. 호스트 공개 주소는 `127.0.0.1` |
| 설정 파일 | `docker/mosquitto/config/mosquitto.conf`, 읽기 전용 마운트·Git 추적 |
| 실행 데이터 / 로그 | `docker/mosquitto/data/`, `docker/mosquitto/log/`, 최초 기동 시 생성·Git 제외 |
| 자동 재시작 | 사용 안 함. 명시적으로 실행할 때만 기동 |

모든 마운트의 호스트 경로는 Compose 파일 기준 상대 경로다. 설정 파일이 없으면 디렉터리를 대신 만들지 않도록 구성했다. `amr-demo`라는 이름으로 기존 Compose 프로젝트와 구분하며, 기존 `amr-mosquitto`의 1883 포트와 겹치지 않는다. [Compose 경로·포트 규칙](https://docs.docker.com/reference/compose-file/services/) · [프로젝트 이름](https://docs.docker.com/compose/how-tos/project-name/)

현재 PC의 CLI는 PATH에 없어 확인한 설치 경로를 사용한다. 다른 PC에서는 실제 Docker CLI 경로를 사용하거나 PATH에 등록된 `docker`를 사용한다.

```powershell
Set-Location C:\amr-control-system-demo
$dockerCli = 'C:\Users\Admin\AppData\Local\Programs\DockerDesktop\resources\bin\docker.exe'
& $dockerCli compose -f docker/compose.yaml config --quiet
$LASTEXITCODE
```

위 설정 검사의 예상 종료 코드는 `0`이다. `compose config`는 Compose 모델을 검사하며 Broker를 시작하거나 Mosquitto 설정의 실제 로딩·MQTT 통신을 시험하지 않는다. [명령 설명](https://docs.docker.com/reference/cli/docker/compose/config/)

다음 명령은 **후속 Broker 실행·연결 작업을 위한 안내**다. Issue #2의 완료 검증에는 새 Broker 기동이나 실제 MQTT 연결 시험이 포함되지 않는다.

```powershell
& $dockerCli compose -f docker/compose.yaml up -d mosquitto
& $dockerCli compose -f docker/compose.yaml ps
& $dockerCli compose -f docker/compose.yaml logs --tail 50 mosquitto
```

후속 실행의 예상 결과는 `mosquitto` 서비스가 실행되고 `127.0.0.1:1884`로 포트가 공개되며 설정·데이터·로그 경로 오류가 없는 것이다. 실제 기동과 호스트에서의 MQTT 연결·송수신은 미검증인 후속 범위이며 Issue #2의 완료조건에 포함되지 않는다. 새 프로젝트만 중단할 때는 `& $dockerCli compose -f docker/compose.yaml stop mosquitto`를 사용한다.

이 프로젝트의 관리·실행 위치는 `C:\amr-control-system-demo`다. 설정의 상대 경로는 이 로컬 폴더의 `docker/compose.yaml`을 기준으로 해석된다. 데이터·로그도 이 폴더에 생성되며, 검토할 로그는 필요한 부분을 `evidence/`로 옮겨 보관한다.

## 확인한 환경과 결과

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

## 직접 확인할 절차

1. 위 복원·빌드 명령으로 세 프로젝트가 경고·오류 없이 빌드되는지 확인한다.
2. 다음 명령으로 준비 안내 한 줄과 종료 코드 `0`을 확인한다. 16:04 재시험에서 두 조건을 충족했다. 이후 정책 차단이 발생하면 이벤트 뷰어의 `Microsoft-Windows-CodeIntegrity/Operational`에서 해당 DLL의 이벤트 3033·3077을 확인하고 기기 관리 담당자에게 허용된 개발 실행 방법을 확인한다.

   ```powershell
   dotnet run --project src/Amr.Communication.Console --no-build
   $LASTEXITCODE
   ```

3. 위 `--list-tests` 명령이 탐색을 마치는지 확인한다. 시험 0건은 현재 빈 시험 프로젝트의 예상 결과이며 기능 시험 통과가 아니다.
4. 추가 요청한 Docker 파일은 위 [프로젝트 전용 Docker 설정](#프로젝트-전용-docker-설정)의 `compose config --quiet`로 모델을 검사한다. 실제 기동·MQTT 연결 절차는 후속 연결 작업에서 사용한다. 관리 폴더는 `C:\amr-control-system-demo`, 접속 주소는 `127.0.0.1:1884`다.

Issue #2의 개발 구성·로컬 검증 범위는 완료했다. 실제 PR 연결과 리뷰·반영 확인이 남아 있다. 새 Broker 기동·MQTT 연결은 제외된 후속 범위이며 이번 Issue의 완료를 막는 항목이 아니다. 이 준비 결과를 COM-03 또는 과제1 전체 완료로 표시하지 않는다. 커밋·푸시·PR 생성은 결과 확인 후 별도 요청 단계에서 진행한다.
