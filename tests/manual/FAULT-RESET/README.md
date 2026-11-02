# 단일 AMR 장애 발생·RESET 복구 시험

현재 위치에서 장애 복구가 이루어지는지 실제 관제 GUI와 MQTT로 확인한다. `--trial`은 사용하지 않는다.
대상은 AMR 한 대의 명령 대기 모드다. RESET은 원점 복귀가 아니며, 원인이 해소된 FAULT를 해제하는 명령이다.

## 1. 준비 — Git Bash

Docker Desktop의 Linux 엔진과 .NET SDK가 필요하다. 같은 Broker에 `AMR_001` 시뮬레이터를 두 개 실행하지 않는다.
기존 시험의 GUI·AMR은 종료하고 새 세션으로 시작한다. 기존 Broker는 재사용할 수 있다.

```bash
cd /c/amr-control-system-demo
docker compose -f docker/compose.yaml up -d mosquitto
docker compose -f docker/compose.yaml ps mosquitto
docker compose -f docker/compose.yaml logs --tail 20 mosquitto
dotnet build src/Amr.ControlGui -c FaultReset --no-restore
```

- Broker: `Up`, 호스트 `127.0.0.1:1884 → 컨테이너 1883` 확인.
- 빌드: 오류 0 확인. `FaultReset`은 기존 Debug 실행 파일과 분리한 빌드 구성이다.
- 실패 시: Docker API 연결 오류, 포트 사용 충돌, 빌드 실패가 있으면 다음 단계로 진행하지 않는다.
- 설정: [simulator.fault-reset.json](../../../config/simulator.fault-reset.json). 시작 좌표 `(-1,0)`, 속도 `1m/s`, 목적지 `(100,0)`, 장애 발생 `30초`, 원인 해소 `60초`.

## 2. GUI 실행 — 터미널 A

```bash
cd /c/amr-control-system-demo
dotnet run --project src/Amr.ControlGui -c FaultReset --no-build --no-launch-profile -- \
  --mqtt --host 127.0.0.1 --port 1884 --amr-id AMR_001 \
  --simulator-config config/simulator.fault-reset.json --command-timeout-seconds 120
```

상단 `ACTUAL · MQTT 연동`, `서버 연결됨`을 확인한다. 아직 AMR이 없을 때 위치 수신 대기는 정상이다.
기본 Mock 화면이 열리거나 서버 연결이 안 되면 실행 옵션과 포트를 확인한다.

## 3. AMR 실행 — 터미널 B

```bash
cd /c/amr-control-system-demo
dotnet run --project src/Amr.Simulator -c FaultReset --no-build -- \
  --config config/simulator.fault-reset.json --command-mode --mqtt \
  --host 127.0.0.1 --port 1884 --telemetry-ms 200 --heartbeat-ms 1000
```

`CommandSessionReady` 및 `FaultScenarioScheduled ... ActivateAfterMs=30000 ResolveAfterMs=60000`을 확인한다.
시점 기준은 **명령 Topic 구독 준비 후**이며, MOVE 버튼을 누른 시각이 아니다.
GUI에서 `AMR_001`, `(-1,0)`, 대기(IDLE), 생존 확인을 확인한다. `CommandSessionFailed` 또는 연결 단절이면 시험을 중단한다.

## 4. 장애 전에 시작 클릭

GUI에서 AMR_001 선택 → **같은 앱의 별도 실행창 열기 →** → **시작**.
구독 준비 후 30초 안에 누른다. 목적지가 `(100,0)`인지 확인한다.

- 콘솔: `CommandProcessed ... Type=MOVE ... State=BUSY`.
- GUI: 이동 상태, 좌표 변화, 최종 응답 대기. Heartbeat 수신은 계속됨.
- 실패: 좌표가 변하지 않음, 이동 전에 성공 표시, 다른 장비에 명령 발행.

## 5. 30초 — 장애 발생 확인

- 콘솔: `AlarmPublished ... AlarmState=ACTIVE`, 원 MOVE의 동일 cmdId로 `Result=FAILURE Reason=FAULT_INTERRUPTED`.
- GUI: **FAULT · 장애**, 빨간 상태/알람, 메인 화면 알람 목록에 1건, 시작 비활성화.
- 메인 장비 목록에서 **현재 X/Y와 속도 0**을 기록한다. 실행창의 출발 좌표는 MOVE 출발점이므로 현재 위치와 구분한다.
- 실패: FAULT인데 좌표가 계속 이동함, MOVE가 ARRIVED로 성공함, 활성 알람이 없거나 다른 cmdId에 실패가 연결됨.
- 시작을 늦게 눌러 IDLE에서 장애가 났다면 MOVE 중단 시험은 미검증이다. GUI·AMR을 새로 실행하여 다시 시험한다.

## 6. 30~60초 — 미해소 RESET

메인 GUI의 **RESET · 선택 장비 장애 복구** 또는 별도 창의 **RESET · 장애 복구**를 한 번 누른다.

- 콘솔: RESET의 새 cmdId, `Result=FAILURE Reason=FAULT_CAUSE_ACTIVE`.
- GUI: `요청 실패 / 장애 원인이 아직 해소되지 않았습니다.`, FAULT·알람·정지 좌표 유지.
- 실패: IDLE로 전환됨, 알람이 사라짐, 좌표가 원점이나 출발점으로 바뀜.

## 7. 60초 — 원인 해소만 확인

`FaultCauseResolved ... AllCausesResolved=True State=FAULT Alarm=LatchedUntilReset` 로그를 기다린다.
아직 RESET을 누르지 않은 상태에서 **FAULT와 알람이 그대로 남는 것**이 통과 조건이다.
원인 해소는 이 시험에서 설정 시간이 지난 것으로 모사하며 실제 장비 센서를 검사하지 않는다.

## 8. 새 RESET 클릭 — 정상 복구 확인

같은 RESET 버튼을 다시 한 번 누른다. GUI는 **새 요청 ID**로 발행한다.

- 콘솔: `AlarmState=CLEARED`, `Result=SUCCESS Reason=RESET_COMPLETED`.
- GUI: 대기(IDLE), 활성 알람 0건, `RESET · 복구 응답 수신`, 시작 버튼 활성화.
- 장비 목록: 5단계에서 기록한 **현재 좌표 그대로**, 속도 0. 중단된 MOVE는 FAILURE로 유지됨.
- 실패: 원점 복귀, 자동 이동 재개, 원 MOVE의 실패가 성공으로 덮임, 최종 ACK 없이 복구 성공으로 처리됨.
- 같은 실패 cmdId를 MQTT로 재사용하면 캐시된 실패 ACK가 다시 온다. 원인 해소 후 재시험은 반드시 새 RESET 요청이다.

## 9. 복구 후 새 이동 및 종료

목적지 설정에서 현재 위치와 가까운 좌표를 정하고 **시작**을 누른다.
새 MOVE가 도착했을 때만 같은 cmdId의 `SUCCESS / ARRIVED`가 나와야 한다.
시험 종료 시 AMR 터미널 `Ctrl+C`, GUI 닫기. `CommandSessionEnded ExitCode=0`을 확인한다.

관찰이 필요하면 별도 Git Bash에서 다음을 실행한다. 모든 Topic은 QoS0·Retain=false이며,
Alarm·Telemetry·ACK는 개별 메시지다. 최종 복구 판정은 수신된 CLEARED·IDLE·RESET SUCCESS를 함께 대조한다.

```bash
docker compose -f docker/compose.yaml exec -T mosquitto \
  mosquitto_sub -h 127.0.0.1 -p 1883 -V mqttv5 -v -t 'amr/AMR_001/#'
```

## 자동 E2E 재현

```powershell
./tests/manual/FAULT-RESET/verify.ps1
# docker가 PATH에 없으면 -Docker '<docker.exe 절대 경로>' 지정
```

스크립트는 전용 Mosquitto(기본 18887)를 만들고 종료 시 자신이 만든 컨테이너만 정리한다.
생성물이 새 증거 폴더에 기록되며 기존 폴더 덮어쓰기를 거부한다.
실제 WinForms MainForm/SimulationForm을 STA에서 실행하고 버튼을 조작한다.
사용자 입력과 섞이지 않게 시험 창을 화면 밖에 두고 원본 폼 렌더링을 PNG로 기록한다.
시뮬레이터는 같은 시험 프로세스의 별도 Task에서 실제 MQTT 세션과 실행 루프를 사용한다.
프로세스 분리/CLI 수동 조작 시험과 구분하며, 위 수동 절차는 독립 GUI·AMR 프로세스용이다.

`results.json`의 FR-01~10, `mqtt.jsonl`, `simulator.log`, 화면 PNG를 확인한다.
4초 발생/8초 원인 해소의 축약 입력을 사용하며, 미해소 RESET·BUSY RESET·중복 요청·복구 후 새 이동·정상 세션 종료를 포함한다.
[실행 결과와 한계](../../../evidence/AMR/20261102-fault-reset-01/README.md)
