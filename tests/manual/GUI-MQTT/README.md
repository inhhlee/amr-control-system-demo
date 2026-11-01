# 최신 관제 GUI에 실제 MQTT 연결 (#58)

목적: 과제1 `--trial`과 같은 `ReconnectingCommunicationSession`을 GUI에서도 사용해 가상 AMR 위치·속도·운행 상태와 서버 단절·재접속을 확인한다. 대상 AMR 한 대를 지정한다. 기본 실행의 Mock 화면과 기존 `--grid-url`은 그대로 사용한다.

## 직접 확인 — Git Bash

저장소 루트에서 서버를 준비한다. 이미 같은 서버가 실행 중이면 재사용한다.

```bash
cd /c/amr-control-system-demo
docker compose -f docker/compose.yaml up -d mosquitto
dotnet run --project src/Amr.ControlGui --no-launch-profile -- --mqtt --host 127.0.0.1 --port 1884 --amr-id AMR_001
```

GUI 오른쪽 위 **ACTUAL · MQTT 수신**과 **서버 연결됨**을 확인한다. 위치 메시지가 오기 전에는 장비 위치를 만들지 않는다. 별도 Git Bash 창에서 실제 가상 AMR을 실행한다.

```bash
cd /c/amr-control-system-demo
dotnet run --project src/Amr.Simulator -- --config config/simulator.single.json --mqtt --host 127.0.0.1 --port 1884 --telemetry-ms 200 --heartbeat-ms 1000
```

- 지도·선택 상세·장비 목록에 실제 좌표와 BUSY(이동)가 표시되고, 최종 `(1,2)`, IDLE(대기), 속도0이 일치해야 한다.
- 이 단일 실행은 도착 후 종료된다. 이후 생존 신호가 5초 이상 오지 않으면 **AMR 응답 없음**이 표시된다. MQTT 서버 연결 상태와 구분한다.
- 이 안내의 최초 수신 연결 검증은 명령 수행·실제 알람·명령 결과·DB 이력을 제외한 당시 범위다. 현재 MOVE/STOP 수행과 최종 결과, 수신 알람·세션 이력은 [후속 GUI 제어 실행 안내](../GUI-CONTROL/README.md)를 따른다. DB 저장은 후속이다.

## 서버 단절·복구

아래 명령은 이 Compose 서버를 사용하는 다른 프로그램에도 영향을 준다. 공유 서버 중단이 곤란하면 아래 격리 자동 시험을 사용한다.

```bash
docker compose -f docker/compose.yaml stop mosquitto
```

GUI에서 **연결 끊김 · 재접속 대기**와 **마지막 수신값 유지**를 확인한다. 마지막 위치·운행 상태를 유지하며 현재 상태는 미확인으로 표시한다.

```bash
docker compose -f docker/compose.yaml start mosquitto
```

같은 GUI가 **서버 연결됨**으로 돌아오는지 확인한다. 새 위치 메시지가 오기 전까지 마지막값 표시를 유지한다. 앞의 가상 AMR 실행이 종료됐다면 동일 Simulator 명령을 다시 실행해 위치 갱신이 재개되는지 확인한다. **Simulator 자체의 자동 재접속을 추가한 것은 아니다.**

기본 재접속은 trial과 같은 **5초 간격·추가5회**다. 재시도 소진 시 복구 실패를 표시하며 서버·설정을 확인하고 GUI를 다시 실행한다. 시험에 필요한 경우 `--reconnect-delay-seconds 1 --reconnect-attempts 20`으로 바꾼다. 운영 정책 수치로 확정하지 않는다.

Visual Studio에서는 `Amr.ControlGui`의 **MQTT Live** 실행 프로필을 선택한다. 기본 **Mock**, 기존 **Grid MQTT** 프로필은 유지한다. 창을 닫으면 화면 구독·통신 세션의 종료를 기다린다.

## 격리된 실제 MQTT·GUI 자동 시험 — PowerShell

Windows·.NET SDK·Docker가 필요하다. 저장소 루트에서 실행한다.

```powershell
dotnet build AmrControlSystem.slnx
powershell -NoProfile -ExecutionPolicy Bypass -File tests/manual/GUI-MQTT/verify.ps1
```

스크립트는 **18885** 포트에 고유 이름의 임시 Mosquitto를 만들고 실제 Simulator 프로세스·Windows Forms 화면을 실행한다. 해당 임시 서버만 중단/시작하고 종료 때 제거한다. 기존 1884 서버는 건드리지 않는다. 포트가 사용 중이면 `-Port 18886` 등으로 변경한다.

결과는 실행 시각별 evidence 폴더의 `live-results.json`·대표 GUI 렌더링 PNG·MQTT 관측 로그·Simulator 실행 로그로 저장한다. 시험은 실제 GUI 창의 컨트롤과 수신값을 함께 확인한다. 초기 실패·버퍼 초과 등 자동 대역 시험과 구분한다.

최초 재현 시나리오: 정상 이동/도착 → 가상 AMR 종료 후 응답 없음 → 불량 메시지 거부 → 서버 단절 → 자동 재접속·첫 위치 수신 대기 → 가상 AMR 새 실행/재수신 → 재접속 대기 중 GUI 종료. [이번 최종 검증](../../../evidence/GUI/20261101-mqtt-live-01/README.md).
