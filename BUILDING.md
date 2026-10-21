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
