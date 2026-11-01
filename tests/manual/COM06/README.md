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

기본 콘솔에는 첫 위치 수신·운행 상태 변화·접속/단절/재접속·명령/ACK·알람/오류만 표시한다. 위 자동 경로 실행의 최종 값은 `(1,2)`, `IDLE`, 속도0이다. 반복 위치·생존 신호는 Debug 로그로 기록하며 Seq에서 조회한다. `detail on` 또는 `--console-detail`로 콘솔 상세 표시를 켤 수 있다. `--show-payload`의 제한된 정상 JSON 미리보기도 콘솔 상세 모드에서만 표시한다.

## Docker Seq에서 구조화 상세 로그 조회

Seq의 생성·포트·데이터·환경변수와 PowerShell/Git Bash 실행은 [Docker·Seq 구성 안내](../../../docker/README.md)를 따른다. 새 PC는 저장소의 `docker/compose.yaml`에서 선택 서비스 `seq`를 생성한다. 루트 `.env.example`은 포트와 통신 콘솔 환경변수의 공유 예시이며 .NET 프로그램이 `.env`를 자동으로 읽지는 않는다.

현재 PC의 기존 `amr-seq`(Seq2026.1, `127.0.0.1:5341`, 데이터 `C:\practice\docker\seq\data`)도 재사용할 수 있다. 기존 Compose 원본은 현재 경로에서 확인되지 않으며, 기존 컨테이너가 새 프로젝트에 자동 편입되는 것은 아니다. 아래는 **이미 만들어진 이 컨테이너를 재사용하는 예시**다. 중지 상태일 때만 `docker start`를 실행한다.

기존 trial을 Ctrl+C로 종료한 뒤 Git Bash에서 실행한다.

```bash
cd /c/amr-control-system-demo
# 기존 amr-seq가 중지 상태일 때만 실행
docker start amr-seq
AMR_SEQ_URL=http://127.0.0.1:5341 dotnet run --project src/Amr.Communication.Console -- --trial --host 127.0.0.1 --port 1884 --amr-id AMR_001
```

가상 AMR·GUI는 같은 Broker1884를 계속 사용한다. Seq는 [http://127.0.0.1:5341](http://127.0.0.1:5341)에서 연다. 콘솔의 `[로그설정]`/`status`에 출력된 RunId로 이번 실행만 선택할 수 있다. 필요하면 실행 환경의 `AMR_LOG_RUN_ID`로 실행 ID를 지정한다. Seq 미설정 상태에서는 상세 로그를 저장하지 않는다고 시작 시 표시한다.

Seq 검색칸에 아래 조건을 넣고 Go를 누른다. `여기에_콘솔의_RunId`를 실제 값으로 바꾼다.

```sql
RunId = '여기에_콘솔의_RunId' and AmrId = 'AMR_001' and EventType = 'TelemetryReceived'
```

이동 중 데이터만 찾으려면 `and State = 'BUSY'`, 특정 요청을 찾으려면 `RunId = '실행ID' and CommandId = '요청ID'`를 사용한다. 구조화 속성은 RunId·SessionId(확인 가능한 경우)·AmrId·Topic·X/Y·Speed·State·DeviceTimestamp·ReceivedAtUtc, 명령에는 CommandId/CommandType/Result/Reason 등이다. Topic의 장비 ID와 ACK 안의 생략 가능한 장비 ID는 구분한다.

전체 최소 Debug → 입력줄 보호 콘솔 Sink는 기본 Information, 기존 Seq Sink는 Debug로 연결한다. Seq에는 허용한 진단 속성만 보내며 원본 Payload/모델/예외 원문·콘솔용 JSON 미리보기는 보내지 않는다. 전송 큐/종료 제한·실패/유실 집계를 유지한다. Seq에 문제가 있어도 통신을 계속하며, 실패한 로그를 자동 재전송하지 않는다. 정상 수집 완료를 로그 설정 완료와 혼동하지 않는다.

[이번 실제 Docker Seq 검증·화면](../../../evidence/LOG/20261101-trial-seq-01/README.md).

## 로그가 나오는 동안 같은 창에서 명령 입력

Windows 대화형 터미널에서는 마지막 `trial>` 줄에 명령을 입력한다. 새 로그가 오면 그 위에 출력하고 작성 중인 문장·커서를 다시 표시한다. 긴 입력은 화면 폭에 맞는 부분만 보여주며 실제 입력을 잘라 보내지 않는다.

- `Enter`: 현재 명령 실행. `←/→`, `Home/End`, `Backspace/Delete`: 커서 이동·수정. `Esc`: 현재 입력 비우기.
- 로그와 키 입력은 같은 출력 잠금을 사용한다. 터미널 모드·고정 화면 영역이나 새 패키지는 추가하지 않았다.
- 파일·파이프로 입력/출력을 연결한 기존 시험은 일반 텍스트 로그를 유지한다. 화면용 프롬프트·커서 문자를 파일에 추가하지 않는다. `--no-color`는 기존처럼 로그 색상을 끈다.
- 기존 trial을 실행 중이었다면 그 창에서 Ctrl+C로 종료한 뒤 위 실행 명령을 다시 실행한다. `--no-build`를 사용하지 않아 수정본을 빌드·반영한다. 가상 AMR·GUI·Broker를 재시작할 필요는 없다.

[입력줄 보호·실제 터미널/MQTT 검증](../../../evidence/COM-06/20261101-trial-input-01/README.md).

## 명령을 받아 실제 이동·정지하는 가상 AMR

두 번째 창에서 위 자동 경로 실행 대신 아래 명령을 사용한다. 같은 AMR ID의 기존 실행은 먼저 종료한다.

```bash
dotnet run --project src/Amr.Simulator -- --config config/simulator.single.json --command-mode --mqtt --host 127.0.0.1 --port 1884 --telemetry-ms 200 --heartbeat-ms 1000
```

처음에는 `(-1,0)`·IDLE로 명령을 기다린다. trial의 `move 10 2`로 이동하고 이동 중 `stop`으로 정지한다. 다시 `move 10 2`를 입력하면 멈춘 위치에서 새 요청 ID로 이동한다. 도착은 `(10,2)`·IDLE·속도0과 같은 요청 ID의 최종 SUCCESS/ARRIVED를 함께 확인한다. `--show-payload`의 ACK JSON에서 사유를 볼 수 있다.

GUI도 같은 Broker/AMR ID에 연결하면 위치·속도·운행을 함께 관찰한다. trial에서 보낸 요청은 현재 GUI의 명령 추적에 등록되지 않으므로 최종 ACK는 trial에서 확인한다. [GUI와 독립 AMR 실행·범위](../GUI-CONTROL/README.md).

## 콘솔 안에서 입력할 명령

| 입력 | 동작 |
|---|---|
| `move 10 20` | 대상 AMR의 MOVE 명령을 한 번 발행한다. |
| `stop` / `reset` | STOP / RESET 명령을 한 번 발행한다. |
| `status` | Broker 상태·대상·송수신 준비 여부를 출력한다. |
| `detail on` / `detail off` | 콘솔 상세 로그를 켜거나 끈다. Seq의 Debug 수집은 계속된다. |
| `help` | 입력 형식을 표시한다. |
| `/exit` 또는 `exit` / Ctrl+C | 대기·발행·수신 작업을 취소하고 연결 정리를 기다린다. |

표의 입력은 통신 프로그램이 실행 중인 창에 입력한다. 프로그램이 출력한 로그를 Git Bash 명령으로 다시 붙여넣지 않는다. 입력은 ASCII 128자 이내, 좌표 소수점은 `.`이다. 입력 대기에는 취소할 수 없는 별도 ReadLine 작업을 남기지 않는다. Windows 콘솔은 준비된 키를 읽고, Git Bash/파일/파이프 입력은 가용 바이트만 읽는다. 새 시험 모드의 리다이렉트 입력은 Windows 지원 범위다.

`--command-mode` Simulator는 MOVE/STOP을 실제 수행하고 최종 ACK를 보낸다. RESET은 미지원 FAILURE다. 콘솔의 발행 반환만으로 수행 완료를 판단하지 않는다. trial은 ACK 수신값을 보여주며 Core의 요청-응답 업무 추적을 대신하지 않는다.

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

Docker Seq의 실제 수집/속성 검색·핵심 콘솔·상세 전환·Seq 불가 시 통신 유지는 실행 중인 Seq와 새 실행 ID로 확인한다. 이 도구는 고유 이름의 시험 Broker/AMR만 정리하고 공유 Seq/1884 Broker를 중단하지 않는다.

```powershell
pwsh -NoProfile -File tests/manual/LOG/Verify-TrialSeq.ps1 -RunId <새ID>
```

입력 설계 근거: [Microsoft의 Console.In ReadLineAsync 동기 실행 설명](https://learn.microsoft.com/en-us/dotnet/api/system.io.textreader.readlineasync?view=net-10.0), [Windows 파이프의 데이터 가용량 조회](https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-peeknamedpipe). 입력 핸들은 하나의 소유자만 읽으며 조회와 실제 읽기 사이에 다른 독자가 데이터를 소비하지 않도록 한다.
