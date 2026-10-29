# 다른 PC에서 빌드·실행·검증

기본 구성은 `develop` 기준이며 아래 격자 지도는 Issue #46 작업 브랜치의 로컬 변경이다. 게시·병합 전에는 원격 develop을 복제하는 것만으로 격자 파일을 받을 수 없다. 저장 위치는 자유롭게 정한다. Codex, Codex 내부 Python, `C:\process-plan`, Git Flow, Seq는 아래 기본 빌드·실행·자동시험의 필수 조건이 아니다.

## 준비

- .NET SDK: 저장소 `global.json`의 **10.0.401**과 호환되는 10.0.4xx 패치 버전. 런타임만 설치하면 빌드할 수 없다. `dotnet --version`은 저장소 루트에서 확인한다.
- 첫 복원에는 NuGet 패키지 다운로드를 위한 네트워크가 필요하다.
- Git: 아래 복제 명령과 수동 시험 도구의 코드 버전 조회에 사용한다.
- Windows와 **PowerShell 7.5 이상 (`pwsh`)**: 아래 실제 프로세스·Ctrl+C·MQTT 검증 스크립트의 실행 환경이다. Windows PowerShell 5.1과 구분한다. Linux/macOS의 수동 시험은 지원 대상으로 검증하지 않았다.
- MQTT 실행·통신 시험을 할 때만 Docker Desktop의 실행 중인 Linux 컨테이너 엔진이 필요하다. `docker`를 PATH에서 찾을 수 있어야 한다.

일반 터미널에서 도구가 확인되면 된다. IDE나 Codex에서 제공하는 전용 실행 파일 경로를 설정할 필요가 없다.

## 복제·빌드·자동시험

원하는 부모 폴더에서 실행한다. 이미 복제했다면 그 저장소 루트에서 `dotnet restore`부터 시작한다.

```powershell
git clone --branch develop https://github.com/inhhlee/amr-control-system-demo.git
Set-Location -LiteralPath ./amr-control-system-demo
dotnet --version
dotnet restore AmrControlSystem.slnx
dotnet build AmrControlSystem.slnx --no-restore
dotnet test AmrControlSystem.slnx --no-build --no-restore
```

각 명령의 종료 코드가 0인지 확인하고 실패하면 다음 명령으로 넘어가지 않는다. 이후 예시는 위 기본 Debug 빌드를 사용한다. 전체 자동시험에는 Simulator와 통신 모듈 시험이 모두 포함된다.

## 격자 이동·MQTT 수신 지도 (Issue #46)

솔루션 빌드 후 기존 Broker를 사용하려면 `config/map-demo/settings.json`의 접속 정보를 확인하고 `dotnet run --no-build --project tools/Amr.Simulator.Map`을 실행한다. 기본 주소는 `http://127.0.0.1:59344`, MQTT는 `127.0.0.1:1884`다. [설정·조작 안내](tools/Amr.Simulator.Map/README.md)를 따른다.

실제 검증은 Docker의 `eclipse-mosquitto:2.1.2-alpine` 이미지를 준비한 뒤 아래처럼 독립 Broker·서버를 사용한다. 두 RunId는 매번 새 값으로 지정한다.

```powershell
pwsh -NoProfile -File tests/manual/AMR01/Start-MqttMap.ps1 -RunId grid-host-new
# 출력된 Map ready 주소를 브라우저에서 열어 출발·장애물 추가·도착을 확인
pwsh -NoProfile -File tests/manual/AMR01/Verify-MapConfiguration.ps1 -SessionRunId grid-host-new -RunId grid-settings-new
pwsh -NoProfile -File tests/manual/AMR01/Verify-GridMqttMap.ps1 -SessionRunId grid-host-new -RunId grid-mqtt-new
pwsh -NoProfile -File tests/manual/AMR01/Stop-MqttMap.ps1 -RunId grid-host-new
```

MQTT 검증 끝에는 장애 시험을 위해 해당 Broker가 중단된다. 이후 브라우저 시연은 기존 시험 호스트를 정리하고 새 RunId로 시작한다. 임의 포트 Broker만 재시작하면 포트가 바뀔 수 있으므로 이전 서버의 접속 주소를 재사용하지 않는다. 설정 검증은 6항목, MQTT 검증은 15항목이며 각각 `verification.json`에 판정을 기록한다. 기존 Windows Forms Mock은 아래 명령과 계약을 유지한다.

## Windows Forms에 격자 MQTT 상태 연결

### 두 AMR·Core 도메인 실행

기존 프로젝트 Broker(`127.0.0.1:1884`)를 준비하고 서로 다른 터미널에서 실행한다. 단일 AMR 실행과 같은 웹 포트59344를 쓰므로 기존 지도 호스트를 정상 종료한 뒤 실행한다.

```powershell
dotnet run --project tools/Amr.Simulator.Map -- --fleet-config config/map-demo/fleet.json
dotnet run --project src/Amr.ControlGui -- --grid-url http://127.0.0.1:59344
```

Visual Studio에는 **AMR Fleet + GUI** 프로필을 추가했다. 기존 **AMR Grid + GUI**와 기본 Mock 프로필은 유지한다. 새 프로필의 Map은 `Fleet MQTT`, GUI는 기존 `Grid MQTT`를 사용한다. 이번에는 CLI·실제 MQTT/GUI와 구성 보존을 검증했으며 **새 프로필의 네이티브 F5 입력은 미실행**이다.

- 설정은 `fleet.json`의 두 임무 파일이다. 같은 지도/Broker와 서로 다른 AMR ID를 사용하며 원본 단일 설정은 바꾸지 않는다. 두 차량의 충돌·교통 제어는 구현하지 않은 독립 시연이다.
- 통신 세션이 ID별 Heartbeat를 판정하고, 연결 어댑터가 Core 입력으로 변환한다. Core의 관제 통신 상태·미션 보류·업무 경보가 GUI의 조회 원본이다. GUI에서 생존을 다시 계산하지 않는다.
- 장비 목록에서 선택한 AMR의 출발·중단·초기화와 경로를 제어한다. 무응답이면 해당 관제 미션만 **보류**, 경보가 발생한다. 통신 복구 시 경보는 해제되지만 **미션 재개**를 눌러야 보류를 해제한다. 보류 중 실제 최종 도착이 수신되면 완료 관측을 보관하고, 명시적 재개 때 관제 완료로 반영한다.
- 관제 미션 보류는 차량의 물리적 정지 명령이 아니다. 마지막 수신 Telemetry의 운행 State·위치·속도를 유지한다. Broker 단절 시 두 감시가 중지되고 미확인·관제 미션 보류·업무 경보로 표시하며 제어를 거부한다. 자동 재접속·DB 저장·실제 차량 Watchdog·교통 락 처리는 후속이다.
- [자동1141건·실제 MQTT·GUI·이전 단일 경로·실패/한계·보존 감사](evidence/COM-02/20261028-two-amr-domain-01/README.md).

### 단일 AMR 관제 통신 세션

`src/Amr.Communication/Mqtt/Sessions/ControlCommunicationSession.cs`가 수신 Client·구독·수신 큐와 Heartbeat 감시의 시작/500ms 검사/종료를 소유한다. Map의 `MqttMapReceiver`는 이 세션의 결과를 지도에 전달한다. Core는 업무 판단, GUI는 관측값 표시를 맡으며 이번 분리로 기존 정책을 추가하지 않는다.

지도 없이 같은 세션을 실행하려면 Broker를 준비하고 다음 명령을 사용한다. 별도 시뮬레이터가 **같은 Broker·AMR ID**로 발행해야 생존 확인이 시작된다. 인수 없는 기존 콘솔과 명령 발행 모드는 유지한다.

```powershell
dotnet run --project src/Amr.Communication.Console -- --monitor-amr AMR_001 --host 127.0.0.1 --port 1884
```

첫 정상 Heartbeat부터5초/500ms로 감시한다. 무응답은 `HeartbeatTimedOut`, 재수신은 `HeartbeatRecovered`다. Broker 단절은 `Failed BrokerDisconnected` 뒤 감시/수신을 정리하고 코드1로 종료한다. Ctrl+C는 정리를 기다리고 코드0으로 종료한다. 접속·해제 실패는 실패 종료이며 자동 재접속은 하지 않는다. 새 연결에는 새 세션을 만든다. [실제 MQTT·GUI/F5·자동시험과 재현](evidence/COM-02/20261028-control-session-01/README.md).

### Visual Studio에서 F5로 함께 실행

**폴더 열기와 솔루션 열기를 구분한다.** 솔루션 탐색기가 `폴더 보기`이면 `AmrControlSystem.slnx`를 두 번 클릭해 솔루션으로 연다. 현재 `.cs` 파일을 실행하는 폴더 보기에서는 라이브러리 DLL 실행 오류가 날 수 있다. 솔루션 탐색기에 솔루션/프로젝트가 로드된 뒤 아래 프로필로 F5를 누른다.

`AmrControlSystem.slnx`를 열고 상단 시작 프로필 **AMR Grid + GUI**를 선택한다. 이 PC에는 해당 프로필을 현재 시작 대상으로 적용했다. **F5**를 누르면 `Amr.Simulator.Map`과 `Amr.ControlGui`가 함께 시작한다. Map이 격자 Simulator·MQTT 송수신을 소유한다. **Shift+F5**로 두 디버깅 프로세스를 종료한다.

- 공유 설정은 `AmrControlSystem.slnLaunch`, 프로젝트별 설정은 각 `Properties/launchSettings.json`이다. `DebugTarget: Grid MQTT`로 두 프로필을 명시한다. 다른 PC에서도 솔루션을 연 뒤 이 공유 프로필을 선택하면 같은 구성이 적용된다.
- Docker Desktop과 기존 프로젝트 Broker(`127.0.0.1:1884`)가 준비되어 있어야 한다. Broker는 Visual Studio 디버깅 수명과 별도로 관리하며 F5/Shift+F5가 임의로 중단하지 않는다. 필요한 경우 저장소 루트에서 `docker compose -f docker/compose.yaml up -d mosquitto`로 준비한다.
- Map은 `config/map-demo/settings.json`의 포트59344를 사용하고 GUI의 Grid MQTT 프로필은 `--grid-url http://127.0.0.1:59344`로 연결한다. Map 웹 포트를 바꾸면 GUI 프로필 주소도 맞춘다. GUI 기본 CLI 실행과 `Mock` 프로필은 기존 Mock 모드를 유지한다.
- `Directory.Build.props`는 Visual Studio에서 빌드하는 Map만 `bin/VisualStudio/<Configuration>/<TargetFramework>/`에 출력한다. 기존 CLI 시연의 실행 파일 잠금을 피하기 위한 설정이며 일반 CLI의 `bin/Debug`·`bin/Release` 출력은 유지한다.
- `Amr.Simulator.Map`은 프로젝트 프로필로 실행한다. 별도 IIS Express 설정이나 새로운 NuGet 패키지는 필요하지 않다. 현재 PC에서는 기존 설치 상태로 F5 실행을 검증한다.

[실제 F5·종료·재실행·MQTT/GUI 및 보존 대조 결과](evidence/DEV/20261027-vs-f5-01/README.md).

기본 실행은 기존 Mock이다. 격자 호스트의 출력 주소를 `--grid-url`로 지정하면 같은 Windows Forms 화면이 실제 관제 MQTT 수신값을 표시한다. 주소는 `http://127.0.0.1:<port>` 형식이다. 기존 격자 호스트가 Simulator·송수신 세션을 소유하고 GUI는 HTTP로 상태를 200ms 간격에 순차 조회한다.

```powershell
dotnet build AmrControlSystem.slnx -c Release
pwsh -NoProfile -File tests/manual/AMR01/Start-MqttMap.ps1 -RunId gui-host-new -MapExe tools/Amr.Simulator.Map/bin/Release/net10.0/Amr.Simulator.Map.exe
$gridSession = Get-Content evidence/AMR/gui-host-new/session.json -Raw | ConvertFrom-Json
dotnet run -c Release --no-build --project src/Amr.ControlGui -- --grid-url $gridSession.Url
# GUI를 닫고, 자신이 만든 호스트가 더 필요 없을 때 실행
pwsh -NoProfile -File tests/manual/AMR01/Stop-MqttMap.ps1 -RunId gui-host-new
```

- **출발 / 이동 중단 / 지도 초기화**로 기존 격자 시연을 제어한다. 지도 통로를 클릭해 장애물을 추가·제거한다. 현재 AMR·S·G·고정 벽은 편집하지 않는다.
- 파랑 점선은 Simulator의 예정 경로, 청록 실선은 관제 수신 궤적이다. 위치 수신 전에는 Marker·장비 행을 만들지 않는다. X=열, Y=-행, 1칸=1m를 유지한다.
- GUI의 위치·속도·운행 상태·목록·상세·요약은 같은 수신 프레임에서 갱신한다. 단일 AMR의 첫 정상 Heartbeat부터 감시하며 Timeout5초·확인 주기0.5초·단조 시계 경과>=Timeout을 적용한다. 중단 구간당 Timeout1회, 정상 수신 재개 시 복구1회다. 생존 정상/응답 없음은 Broker 연결과 별도로 표시한다. Broker 단절/호스트 종료에는 감시를 중지하고 생존을 미확인으로 둔다. 업무 Offline/FAULT나 물리적 STOP을 추론하지 않는다.
- 호스트가 시작하면 출발 전부터 AMR 통신을 연결한다. Ready·경로 차단 대기·도착·이동 중단·초기화 중에도 Heartbeat/Telemetry를 유지한다. 도착은 최종 정지 좌표의 관제 수신 확인이며 세션 종료가 아니다. 초기화는 마지막 수신값과 수신 건수를 보존하고, 다음 실제 Telemetry로 시작 위치를 갱신한다.
- 서버 응답 실패/형식 오류에는 마지막 수신 위치를 유지하고 갱신 오류를 표시하며 제어 버튼을 비활성화한다. 취소된 임무의 마지막 BUSY 수신값을 물리적 정지 ACK로 바꾸지 않는다.
- 이 버튼들은 시연 HTTP 제어이며 업무 MOVE/STOP/RESET Command·ACK가 아니다. 알람·업무 명령 데이터는 미연결/미확인으로 표시한다. 하단은 해당 시연의 최근 로그이며 DB 업무 이력이 아니다.
- GUI를 닫으면 자신의 조회·요청 취소와 정리를 기다린다. 별도 호스트의 임무를 중단하려면 GUI의 중단 버튼을 먼저 사용한다. 호스트/소유 Broker 종료는 위 Stop 명령으로 관리한다.

[이번 GUI48건·실제 창 검증 근거](evidence/GUI/20261027-grid-dashboard-01/README.md). 실제 폼 검증은 관찰 시간을 확보하기 위해 복사 설정의 이동 간격을 3000ms로 사용했고 제품 기본 설정600ms는 유지했다.

[후속 Heartbeat 구현·자동시험·실제 MQTT/GUI 검증과 재현 순서](evidence/COM-02/20261027-heartbeat-one-01/README.md). 시험용 신호 차단은 `tests/manual/COM02-A/HeartbeatProbe`에서만 제공한다. 일반 GUI/호스트에 장애 주입 버튼은 추가하지 않았다.

## Windows Forms Mock 대시보드·지도

Windows에서 아래 명령으로 실행한다. 실제 AMR·Broker·DB는 필요하지 않다.

```powershell
dotnet run --project src/Amr.ControlGui/Amr.ControlGui.csproj
```

화면의 `MOCK · 시험 데이터` 표시를 확인하고 장비 목록에서 AMR_001 또는 AMR_002를 선택한다.

1. **다음 상태**: 선택 장비의 X를 1m씩 변경하고 IDLE/속도0과 BUSY/속도1을 번갈아 보여 준다. `명령 · Mock 확인` 영역의 시험 동작이며 시간 기반 이동이나 실제 Telemetry가 아니다. 실제 Broker·Heartbeat 관측은 미확인으로 유지한다.
2. 목표 X/Y를 입력해 **MOVE 요청**: Mock이 요청 ID를 생성·기록한 다음 접수를 표시한다. 접수 직후 결과는 미확인이다.
3. **성공 결과 (Mock)** 또는 **실패 결과 (Mock)**: 선택 장비에서 아직 결과를 지정하지 않은 가장 오래된 요청의 시험 결과를 지정한다. 실제 이동·ACK 수신·장비 State 변경을 만들어 내지 않는다.
4. 상태를 바꾸는 중에도 창을 닫을 수 있다. 폼은 구독·자기 작업을 취소하고 기다린 뒤 닫으며 앱 시작 지점이 공유 공급자를 정리한다.

대시보드와 지도는 기존 Mock 자료를 사용한다. 목업 상단의 네 요약 카드에 통신 관측·장비 수/생존·운행·알람/명령 현황을 표시하고, 왼쪽 Map Marker와 선택 상세·장비 목록이 같은 장비를 가리킨다. 알람/이력 전체 화면·격자/경로·STOP/RESET 업무 처리·실제 Core/MQTT/SQLite 연결은 후속이다. 알람·이력은 유효한 빈 결과이며 지원하지 않는 명령은 접수 거부 사유를 반환한다.

프로젝트는 `Amr.Core`·`Amr.ControlGui`·`Amr.ControlGui.Tests`를 추가하고 파일은 각 루트에 둔다. 기존 통신·Simulator 구조는 유지한다. Core에는 GUI·MQTT·SQL 참조가 없고 GUI는 Core 계약을 참조한다. MainForm은 인터페이스로 조회·명령을 사용하며 Mock 시나리오 버튼의 동작은 시작 지점에서 별도로 전달한다.

**대시보드·지도 확인**

- 상단 요약은 실제 공급자 값에서 계산한다. 초기 Mock은 2대·대기2·알람0·명령0이며 Broker/생존은 미확인이다. Figma의 MQTT 정상·5대·경로/장애물은 예시이므로 생성하지 않는다.
- Map Marker 클릭 또는 장비 목록 선택으로 같은 장비의 상세를 확인한다. Marker가 겹치면 반복 클릭으로 선택을 순환한다. 지도에 포커스한 뒤 좌우 방향키·Home/End로 선택할 수도 있다.
- `다음 상태`를 누르면 해당 장비의 X·속도·운행 상태가 목록·상세·지도·요약에 함께 반영된다. 선택 장비는 유지된다.
- 좌표는 m 단위, X는 오른쪽·Y는 위쪽이며 같은 배율을 사용한다. 원점을 포함하고 전체 장비 좌표에 여백을 두어 자동 맞춤한다. 음수도 표시한다. 운영 지도 범위나 경로 계산 규칙을 정한 것이 아니다.
- 비유한 좌표는 잘못된 좌표 건수로, 표시 불가 범위는 안내 문구로 드러낸다. 장비가 없으면 빈 화면을 표시한다. 경로·장애물·목적지 자료는 임의로 만들지 않는다.
- 현재 장비 목록과 기존 명령 확인 동작을 유지했다. 이력/알람의 전체 조회·선택 시나리오는 후속이며 Mock의 빈 결과를 실제 연결 완료로 해석하지 않는다.
- 창을 최소 크기(1000×720)와 넓은 크기에서 확인한다. 최소 크기에서도 명령 결과 한 행이 온전히 보이며, 다른 요청과 요청 ID·설명은 표의 세로·가로 스크롤로 확인한다. 화면은 사용 가능한 작업 영역 안에서 시작한다.
- 코드 파일은 GUI 루트의 `MainForm.cs`·`.Designer.cs`·`MapCanvas.cs`이며 새 폴더·프로젝트는 추가하지 않았다. Map의 축 변환과 hit test는 MapCanvas 파일 안에 둔다.

**첫 Mock의 개발 설정과 계약**

- 구독별 버퍼 64항목, 앱 실행당 요청 보관 128건을 `Program.cs`에 명시한다. 운영 기본값이 아니다. 보관 한도에 도달하면 새 요청을 거부하고 기존 결과는 유지한다. 앱을 다시 실행하면 시험 상태가 초기화된다.
- 초기 스냅샷과 구독 등록을 같은 잠금에서 처리한다. 변경마다 공급자 내 Revision이 증가하며 읽기 전용 값 사본을 전달한다. 첫 실행본은 작은 전체 스냅샷을 변경 항목에도 포함한다.
- 버퍼 초과는 `AmrResyncRequiredException`으로 알리고 해당 구독을 종료한다. 화면은 새 구독의 최신 초기 스냅샷으로 동기화한다. 통지 스트림은 업무 이력의 무손실 보관소가 아니다.
- 입력 형식 오류는 ArgumentException, 구독/호출 취소는 OperationCanceledException, 종료 후 사용은 ObjectDisposedException으로 구분한다. 업무 거부는 CommandSubmission의 사유다.
- Mock 접수 잠금 안의 취소 확인을 경계로 한다. 그 전에 취소되면 기록하지 않고, 확인 뒤 접수되면 ID를 반환한다. 이후 토큰 취소로 접수 사실이나 결과를 지우지 않는다.
- Mock은 주기·발행 작업을 만들지 않는다. 공급자 종료는 버퍼 정리와 대기 독자 해제로 끝난다. 실제 전송·운영 종료 Timeout은 아직 정하지 않았다. 자동 시험별 대기 제한은 시험 정지 방지용이다.
- GUI의 비동기 변경은 UI 스레드에서 적용한다. [Microsoft의 InvokeAsync 안내](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-make-thread-safe-calls)를 따른다.

자동 시험은 위 솔루션 명령에 포함되며 개별 실행은 아래와 같다. 실제 화면 조작·닫기 확인은 자동 시험과 별도로 기록한다.

```powershell
dotnet test tests/Amr.ControlGui.Tests/Amr.ControlGui.Tests.csproj
```

## Broker 없이 이동 실행·실제 프로세스 검증

```powershell
dotnet run --no-build --project src/Amr.Simulator -- --config config/simulator.single.json
pwsh -NoProfile -File tests/manual/AMR01/Verify-Run.ps1 -RunId ("runner-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
```

기본 설정은 (-1,0) → (1,0) → (1,2), 속도 1m/s다. 약 4초 후 마지막 `SimulationCompleted`에서 (1,2)·IDLE·속도0, 종료 코드0을 확인한다. 설정 경로는 실행한 디렉터리 기준이다.

두 번째 명령은 도착·설정 변경·잘못된 설정 거부·이동 없는 경로·이동 중 취소·긴 갱신 간격의 취소 6개를 검증한다. 마지막 PASS와 `evidence/AMR/<RunId>/verification.json`을 확인한다. 매번 새 RunId를 사용한다.

## 실제 MQTT 검증

Docker 엔진을 준비한 뒤 이미지를 한 번 내려받는다. 도구는 임의 loopback 포트로 자신이 소유한 Broker를 만들고 종료 시 정리한다. 프로젝트의 고정 포트 Broker나 Seq를 미리 실행할 필요가 없다.

```powershell
docker version
docker pull eclipse-mosquitto:2.1.2-alpine
pwsh -NoProfile -File tests/manual/AMR01/Verify-MqttTelemetry.ps1 -RunId ("mqtt-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
```

정상·변경·이미 도착한 경로, Retain 없음, 긴 갱신 대기 중 독립 송신과 취소, Broker 단절, 최초 접속 실패의 7개를 확인한다. 마지막 PASS와 `evidence/AMR/<RunId>/verification.json`을 확인한다. Docker Desktop에서 복제 폴더의 bind mount 접근이 허용되어야 한다. 시험 중 다른 작업으로 컨테이너 상태를 바꾸지 않는다.

직접 두 콘솔을 연결하려면 저장소 루트에서 `docker compose -f docker/compose.yaml up -d mosquitto`로 개발용 Broker를 준비한 뒤 [MQTT 시연 안내](tests/manual/AMR01/MQTT-DEMO.md)를 따른다. 기본 포트는 `127.0.0.1:1884`다. Compose의 설정·데이터·로그 경로는 `docker/compose.yaml` 기준 상대 경로다. 사용이 끝나면 `docker compose -f docker/compose.yaml stop mosquitto`로 중단한다.

## 선택 도구와 과거 자료

- Seq는 선택 사항이다. 사용할 때만 `AMR_SEQ_URL`과 필요한 경우 `AMR_SEQ_API_KEY`를 실제 서버에 맞춘다. 미설정 상태로 콘솔·MQTT 검증이 가능하다. README의 기존 PC Seq 경로는 재사용 당시 위치다.
- `tests/manual/COM*`와 `LOG`는 각 Issue의 장애·통신·로그 시험 도구다. 일부는 실행 중인 Compose Broker, Seq 또는 장애 주입에 대한 별도 준비를 요구한다. 기본 자동시험과 위 격리 MQTT 검증의 선행 단계가 아니다. 소스 목록 수집에 별도 `rg` 설치는 필요하지 않다.
- `Verify-Settings.ps1` 및 README의 #32 초기화 전용 설명은 당시 버전의 시험이다. 현재 실행 루프의 검증은 위 `Verify-Run.ps1`을 사용한다.
- 보존 자료의 `Verify.py`는 과거 파일 무결성 확인용이며 제품 빌드·실행에 사용하지 않는다. 실행하려면 일반 Python 3와 해당 과거 커밋을 포함한 Git 이력이 필요하다. 제품 변경0도 검사하므로 보존 자료가 반영된 `4bd0be3`의 깨끗한 복사본에서 실행한다. 이후 제품·시험 변경이 있는 작업 폴더에서는 거부될 수 있다. shallow clone이나 소스 ZIP은 이력 확인을 충족하지 못할 수 있다. 필요 시 `git fetch origin`으로 보존 브랜치 이력을 가져오고 해당 자료의 README를 따른다.
- `C:\process-plan`은 작성자의 계획·명세 원본 관리 위치다. 이 저장소의 개발 구성·소스·설정·수동 시험 도구는 여기서 독립적으로 사용할 수 있다. 과거 `evidence/` 로그의 절대 경로는 당시 실행 위치이며 새 PC의 설치 경로가 아니다.
