# 과제 1 통합 콘솔 시험

## 직접 실행 (Git Bash)

저장소 루트에서 MQTT 서버를 준비하고 통신 콘솔을 먼저 실행한다.

```bash
cd /c/amr-control-system-demo
docker compose -f docker/compose.yaml up -d mosquitto
dotnet run --project src/Amr.Communication.Console -- --trial --show-payload
```

기본 서버는 `127.0.0.1:1884`, 대상은 `AMR_001`이다. `[접속완료]` 뒤 별도 Git Bash 창에서 가상 AMR을 실행한다.

```bash
cd /c/amr-control-system-demo
dotnet run --project src/Amr.Simulator -- --config config/simulator.single.json --mqtt --host 127.0.0.1 --port 1884 --telemetry-ms 200 --heartbeat-ms 1000
```

통신 창에서 `[수신·검사통과]`의 위치·속도·상태·원본 시각 `ts`를 확인한다. 최종 값은 `(1,2)`, `IDLE`, 속도0이다. `--show-payload`는 정상 입력 JSON의 앞부분도 표시한다. 원문 데이터와 요약 좌표의 표시 정밀도는 다르며 검사된 모델은 변경하지 않는다.

## 콘솔 안에서 입력할 명령

| 입력 | 동작 |
|---|---|
| `move 10 20` | 대상 AMR의 MOVE 명령을 한 번 발행한다. |
| `stop` / `reset` | STOP / RESET 명령을 한 번 발행한다. |
| `status` | Broker 상태·대상·송수신 준비 여부를 출력한다. |
| `help` | 입력 형식을 표시한다. |
| `/exit` 또는 `exit` / Ctrl+C | 대기·발행·수신 작업을 취소하고 연결 정리를 기다린다. |

표의 입력은 통신 프로그램이 실행 중인 창에 입력한다. 프로그램이 출력한 로그를 Git Bash 명령으로 다시 붙여넣지 않는다. 입력은 ASCII 128자 이내, 좌표 소수점은 `.`이다. 입력 대기에는 취소할 수 없는 별도 ReadLine 작업을 남기지 않는다. Windows 콘솔은 준비된 키를 읽고, Git Bash/파일/파이프 입력은 가용 바이트만 읽는다. 새 시험 모드의 리다이렉트 입력은 Windows 지원 범위다.

현재 Simulator는 설정 경로 이동·송신까지 구현돼 있다. 콘솔의 MOVE/STOP/RESET 발행이 Simulator의 실제 명령 수행을 뜻하지 않는다. 이를 실제 Broker 수신으로 확인하거나 별도 장비를 연결한다. ACK는 수신한 결과값으로 표시하고 Core의 요청-응답 업무 추적을 대신하지 않는다.

## 연결 끊김·복구

개발용 프로젝트 Broker를 시험할 때 별도 창에서 중단 후 다시 시작한다.

```bash
docker compose -f docker/compose.yaml stop mosquitto
docker compose -f docker/compose.yaml start mosquitto
```

같은 통신 콘솔에서 `[연결끊김]` → `[재접속대기]` → `[재접속시도]` → `[재접속완료]`를 확인한다. 재구독까지 승인돼야 준비 완료로 표시한다. 새 데이터는 Simulator를 다시 실행해 수신 재개를 확인한다. **Simulator 자체는 Broker 단절 시 종료하며 자동 재실행하지 않는다.**

현재 단일 경로 Simulator는 목적지에서 종료하고 생존 신호 전송도 끝난다. 이후 `[AMR무응답]`은 서버 연결 끊김과 구분한다. 생존 신호를 다시 보내면 `[생존복구]`가 출력된다.

## 시험용 재접속 기준

- 새 `--trial` 모드에만 적용한다. 기존 인수 없는 수신/`--monitor-amr`/`--command` 및 GUI 실행은 기존 동작을 유지한다.
- 최초 접속 실패·구독 실패·연결 후 Broker 단절에 적용한다. 기본 재시도 간격5초/최대5회이며 최초 시도와 별도이므로 계속 실패하면 접속 시도는 총6회다. 정상 구독 준비 완료 후 다음 장애의 횟수는 초기화한다.
- `--reconnect-delay-seconds 1 --reconnect-attempts 10`처럼 바꿀 수 있다. 간격1~3600초, 추가 시도0~100회. 이 값은 이번 구현에서 선택한 시험 기본값이며 운영 정책 확정값이 아니다.
- 이전 세션의 작업/구독/Client 정리를 기다린 뒤 새 세션을 만든다. 이전 연결의 생존 판정을 새 연결로 복사하지 않는다.
- 준비 전 새 명령은 거부한다. 송신 대기열이 없고, 이미 시도한 명령은 연결 복구 후에도 자동 재발행하지 않는다. 발행 중 단절은 전달·실행 여부가 불명일 수 있다.
- 명령 발행 반환/ACK 수신/실제 동작 완료를 구분한다. QoS0·Retain=false의 기존 계약을 유지하며 유실·중복 없는 전달을 보장하지 않는다.
- 구독/송수신 준비 전에는 `CONNECTED`로 표시하지 않는다. 수신 처리/기록 오류·정리 실패는 무한 재접속으로 숨기지 않고 종료한다.
- 색상은 출력이 콘솔일 때 사용한다. `--no-color`와 로그 리다이렉트에서는 색상 제어 문자를 넣지 않는다. 로그의 제어 문자는 이스케이프하고 긴 내용은 일부만 표시한다.

## 실제 MQTT 자동 검증

Windows, PowerShell7.5+, .NET10 SDK, Docker Linux 엔진과 캐시된 `eclipse-mosquitto:2.1.2-alpine`이 필요하다. 솔루션을 빌드한 뒤 새 실행 ID로 수행한다.

```powershell
dotnet build AmrControlSystem.slnx --no-restore
pwsh -NoProfile -File tests/manual/COM06/Verify-TrialConsole.ps1 -RunId my-trial-new
```

과제1 최종 완료조건의 두 장비·역순 ACK 보존까지 확인하려면 새 실행 ID와 `-FinalAcceptance`를 지정한다. 시험용 AMR 응답과 요청 대조용 사전만 추가하며 실제 Simulator 명령 수행이나 Core 업무 추적을 구현하는 옵션은 아니다.

```powershell
pwsh -NoProfile -File tests/manual/COM06/Verify-TrialConsole.ps1 -RunId my-final-new -FinalAcceptance
```

별도 loopback 고정 포트의 소유 Broker만 중단/시작/제거한다. 기존 서버는 조작하지 않는다. 실제 Simulator 도착 수신, 잘못된 입력 거부, 시험 Alarm/ACK 수신, 콘솔의 세 명령 발행, Heartbeat 중단·복구, 서버 중단·자동 재접속·재구독·수신 재개, 재발행 없음의 관측 구간, 복구 후 새 명령, 재시도 소진/대기 중 종료를 검증한다. Alarm/ACK 입력은 통신 시험 데이터이며 AMR 실제 동작 결과가 아니다.

`evidence/COM-06/<RunId>/verification.json`과 실행 로그를 확인한다. 실제 stdout/입력·Docker 명령·환경·실패를 실행 ID별로 보존한다. 기존 자동시험·실제 Broker 시험·네이티브 터미널 화면은 서로 다른 검증이다.

입력 설계 근거: [Microsoft의 Console.In ReadLineAsync 동기 실행 설명](https://learn.microsoft.com/en-us/dotnet/api/system.io.textreader.readlineasync?view=net-10.0), [Windows 파이프의 데이터 가용량 조회](https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-peeknamedpipe). 입력 핸들은 하나의 소유자만 읽으며 조회와 실제 읽기 사이에 다른 독자가 데이터를 소비하지 않도록 한다.
