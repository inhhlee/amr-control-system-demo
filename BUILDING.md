# 다른 PC에서 빌드·실행·검증

현재 `develop` 기준이다. 저장 위치는 자유롭게 정한다. Codex, Codex 내부 Python, `C:\process-plan`, Git Flow, Seq는 아래 기본 빌드·실행·자동시험의 필수 조건이 아니다.

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
