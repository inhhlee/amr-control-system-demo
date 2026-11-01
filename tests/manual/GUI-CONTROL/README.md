# 실제 GUI와 독립 가상 AMR 실행

관제 화면에서 보낸 이동·정지 명령이 가상 AMR에서 수행되고, 같은 요청 ID의 최종 결과가 돌아오는지 확인한다. 콘솔의 화면을 GUI 안에 넣는 방식이 아니라 `--trial`과 같은 통신 모듈을 공유한다.

## 직접 실행

Windows·.NET SDK·Docker가 준비된 상태에서 저장소 루트의 Git Bash 창을 두 개 연다. 아래 명령만 복사하고 실행 뒤 출력되는 로그는 다시 입력하지 않는다.

첫 번째 창: Broker와 관제 GUI를 실행한다.

```bash
cd /c/amr-control-system-demo
dotnet build AmrControlSystem.slnx --no-restore
docker compose -f docker/compose.yaml up -d mosquitto
dotnet run --project src/Amr.ControlGui --no-build --no-launch-profile -- --mqtt --host 127.0.0.1 --port 1884 --amr-id AMR_001 --simulator-config config/simulator.single.json
```

두 번째 창: 명령을 기다리는 실제 가상 AMR 프로세스를 실행한다.

```bash
cd /c/amr-control-system-demo
dotnet run --project src/Amr.Simulator --no-build -- --config config/simulator.single.json --command-mode --mqtt --host 127.0.0.1 --port 1884 --telemetry-ms 200 --heartbeat-ms 1000
```

GUI에서 실제 위치와 생존 신호가 수신되면 AMR을 선택하고 **같은 AMR 별도 실행창 열기**를 누른다. **시작**은 현재 위치에서 목적지로 새 MOVE를 보낸다. **Stop**은 별도의 STOP을 보내 정지를 확인한다. Stop 뒤 다시 시작하면 멈춘 좌표에서 새 요청 ID로 이동한다. 별도 창을 닫아도 이동은 계속된다.

- 초기 좌표·속도: `config/simulator.single.json`의 `initialPosition`·`speedMps`를 변경한 뒤 AMR을 다시 실행한다. GUI는 좌표를 순간 이동시키지 않는다.
- 목적지: 위 GUI 옵션은 설정 파일의 마지막 경유점을 목적지로 읽는다. 별도 창의 **목적지 설정**으로 변경할 수도 있다. 명령 모드는 현재 위치부터 목적지까지 직선으로 이동한다. 기존 자동 경유점 실행과 구분한다.
- Visual Studio의 **MQTT Live** 프로필은 파일 상대 경로에 의존하지 않고 실행한다. AMR 위치를 수신한 뒤 별도 창의 **목적지 설정**을 먼저 누른다. 설정 파일에서 목적지를 읽으려면 위 터미널 명령을 사용하거나 `--simulator-config`에 해당 PC의 절대 경로를 지정한다. 이 프로필 자체의 Visual Studio F5 조작은 이번 시험에서 별도로 실행하지 않았다.
- 도착·정지·대기에도 위치·상태·생존 신호를 계속 보낸다. **명령 발행**은 수행 완료가 아니다. `목적지 도착 · 실행 완료` 또는 `Stop · 정지 완료`와 요청 ID를 확인한다.
- 등록 수는 지정한 실제 대상 ID 기준이다. 수신 전 좌표·예시 장비·예시 알람을 추가하지 않는다. 이력은 이번 GUI 실행의 메모리 기록이며 DB 저장이 아니다.
- GUI의 기본 결과 대기 시간은 30초다. 긴 이동은 `--command-timeout-seconds 120`처럼 조정한다. 시간 초과는 수행 미확인이며, 늦게 온 최종 결과도 별도로 표시한다.

## 단절·재접속 확인

세 번째 창에서 아래 명령을 순서대로 실행하며 GUI 상태를 확인한다.

```bash
docker compose -f docker/compose.yaml stop mosquitto
docker compose -f docker/compose.yaml start mosquitto
```

GUI는 단절 때 마지막 좌표를 보존하고 현재 상태는 미확인으로 표시한다. 기본 5초 간격·추가 5회 재접속/구독을 시도하며, 새 위치 수신 전에는 과거 좌표를 최신으로 표시하지 않는다. 이미 보낸 명령을 자동으로 다시 보내지 않는다.

현재 명령 모드 AMR은 Broker 단절 시 종료한다. Broker 복구 후 **두 번째 창의 AMR 실행 명령을 다시 실행**한다. GUI의 재접속과 AMR 프로세스 재실행은 서로 다른 동작이다. 이전 MOVE의 결과가 미확인이라면 연결·새 위치·생존 신호를 확인하고 수동 Stop의 성공을 확인한 뒤 새 MOVE를 요청한다. 이전 요청의 결과는 미확인으로 보존한다. GUI 재접속 횟수가 소진되었다면 GUI도 다시 실행한다.

## 재현 시험

아래는 고유 이름의 임시 Broker(기본 `127.0.0.1:18886`)와 실제 Windows Forms·독립 AMR을 사용한다. 기존 1884 Broker는 건드리지 않는다. 시험 도중 다른 GUI 자동 시험을 동시에 실행하지 않는다.

```powershell
pwsh -NoProfile -File tests/manual/GUI-CONTROL/verify.ps1
```

결과 폴더의 `control-results.json`에서 20항목의 `passed: true`, `communication.jsonl`, Simulator 로그와 화면을 확인한다. 시험용 알람/중복·상충 ACK는 시험 도구가 실제 MQTT로 넣은 입력이며 AMR 장애 시나리오 구현과 구분한다. 도구는 자신이 만든 컨테이너와 AMR 프로세스를 종료한다. 기존 증거 폴더는 덮어쓰지 않는다.

[실제 실행 근거·코드 버전·한계](../../../evidence/GUI/20261101-figma-live-02/README.md). RESET, 장애 발생/해제 시나리오, 여러 AMR 동시 제어, SQLite 이력은 이번 범위에 포함하지 않는다.
