# MQTT와 Seq 실행 환경

저장소 루트에서 Docker Compose로 MQTT 서버와 선택적인 Seq 로그 서버를 실행한다. Docker Desktop의 Linux 엔진과 Compose v2가 필요하다. Seq 구성은 `compose.yaml`, 포트 예시는 루트 `.env.example`에서 관리한다.

| 서비스 | 실행 방법 | 기본 주소 | 데이터 |
| --- | --- | --- | --- |
| Mosquitto | 기본 서비스 | `127.0.0.1:1884` | `docker/mosquitto/data`, `docker/mosquitto/log` |
| Seq | `logging` 프로필 또는 `seq` 직접 지정 | `http://127.0.0.1:5341` | Compose 프로젝트의 `seq-data` 볼륨 |

## 1. 환경변수 준비

PowerShell에서 저장소 루트로 이동한다. `.env`가 없을 때만 예시를 복사한다.

```powershell
Set-Location C:/amr-control-system-demo
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
docker compose --env-file .env -f docker/compose.yaml --profile logging config --quiet
```

저장소를 다른 위치에 복제했다면 그 루트에서 실행한다. `.env`의 실제 값과 API 키는 Git 추적 대상이 아니다.

| 변수 | 읽는 대상 | 의미 |
| --- | --- | --- |
| `SEQ_HTTP_PORT` | Docker Compose | 호스트의 Seq UI·API·로그 수집 포트. 기본 `5341` |
| `AMR_SEQ_URL` | 통신 콘솔 | 로그 수집 주소. Seq 포트와 일치시킴 |
| `AMR_SEQ_API_KEY` | 통신 콘솔 | 수집용 API 키. 기본 로컬 인스턴스에서는 비워 둠 |
| `AMR_LOG_RUN_ID` | 통신 콘솔 | 선택적인 실행 ID. 생략하면 프로세스마다 새 ID 생성 |

**Compose의 `--env-file`은 호스트에서 실행하는 .NET 프로그램에 환경변수를 설정하지 않는다.** 통신 콘솔은 아래 4절처럼 같은 터미널에서 값을 설정한 뒤 실행한다. 쉘에 이미 있는 `SEQ_HTTP_PORT`는 `.env`보다 우선한다.

## 2. 새 Seq 설치·실행

먼저 같은 포트를 사용하는 기존 컨테이너가 있는지 확인한다.

```powershell
docker ps -a --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
```

기존 Seq가 없으면 다음 명령으로 Seq만 생성·시작한다. 이미지를 처음 사용할 때는 레지스트리에서 다운로드한다.

```powershell
docker compose --env-file .env -f docker/compose.yaml --profile logging up -d seq
docker compose --env-file .env -f docker/compose.yaml --profile logging ps seq
docker compose --env-file .env -f docker/compose.yaml --profile logging logs --tail 30 seq
Invoke-RestMethod http://127.0.0.1:5341/api
```

기본 UI는 [http://127.0.0.1:5341](http://127.0.0.1:5341)이다. API 응답이 준비되기 전에는 잠시 기다린 뒤 다시 확인한다. MQTT도 필요하면 별도로 실행한다.

```powershell
docker compose -f docker/compose.yaml up -d mosquitto
```

Seq는 기본 `up`에 포함되지 않는 선택 서비스다. 두 서비스를 함께 시작하려면 `docker compose --env-file .env -f docker/compose.yaml --profile logging up -d`를 사용한다.

이 구성은 **개인 PC의 로컬 개발·시험용**이다. `127.0.0.1`에만 포트를 열고 새 데이터 저장소는 인증 없이 초기화한다. `ACCEPT_EULA=Y`는 Seq 이용약관 동의를 나타낸다. 인증 초기화 변수는 기존 데이터의 사용자·인증 설정을 바꾸지 않는다. 외부 공유·운영용 구성은 관리자 인증과 접근 정책을 별도로 정한다.

## 3. 현재 PC의 기존 Seq 재사용

2026-11-02 확인한 기존 서비스는 `amr-seq`, `datalust/seq:2026.1`, `127.0.0.1:5341`, 데이터 `C:/practice/docker/seq/data`다. 컨테이너의 Compose 라벨에 기록된 `C:/practice/docker/docker-compose.yml`은 확인 시 존재하지 않았다.

기존 컨테이너가 있으면 새 Seq를 같은 포트에 만들지 않고 재사용할 수 있다.

```powershell
docker inspect amr-seq --format '{{.State.Status}}'
# stopped 상태일 때만 시작
docker start amr-seq
Invoke-RestMethod http://127.0.0.1:5341/api
```

이 기존 컨테이너는 이번 저장소의 `amr-demo` Compose 프로젝트에 자동 편입되지 않는다. 기존 데이터 경로를 새 Seq에 동시에 연결하지 않는다. 이번 구성 추가는 기존 컨테이너·데이터의 이관이나 초기화를 수행하지 않는다.

기존 Seq를 유지하면서 새 구성을 시험하려면 `.env`에서 `SEQ_HTTP_PORT=5342`, `AMR_SEQ_URL=http://127.0.0.1:5342`로 바꾼 뒤 2절을 실행한다. API 주소와 아래 콘솔 환경변수·시험 도구의 `-SeqUrl`도 `5342`로 맞춘다. 두 인스턴스의 데이터는 별도다.

## 4. 통신 콘솔에서 로그 전송

PowerShell에서는 다음처럼 환경변수를 설정한다. 새 포트를 사용했다면 URL도 바꾼다. 기본 로컬 Seq에서는 API 키를 비워 두며, 인증된 기존 서버는 해당 서버의 수집용 키를 개인 환경변수로 지정한다.

```powershell
$env:AMR_SEQ_URL = 'http://127.0.0.1:5341'
Remove-Item Env:AMR_SEQ_API_KEY -ErrorAction SilentlyContinue
Remove-Item Env:AMR_LOG_RUN_ID -ErrorAction SilentlyContinue
dotnet run --project src/Amr.Communication.Console -- --trial --host 127.0.0.1 --port 1884 --amr-id AMR_001
```

위 예시는 기본 무인증 로컬 Seq용이며, 키가 필요한 서버에서는 `Remove-Item Env:AMR_SEQ_API_KEY` 대신 자신의 수집용 키를 설정한다. 다른 터미널에서 [명령 대기 AMR](../tests/manual/COM06/README.md#명령을-받아-실제-이동정지하는-가상-amr)을 실행하고 `move 10 2`, `stop`을 시험한다.

Git Bash에서 기본 로컬 Seq로 보낼 때는 다음 명령을 사용한다.

```bash
AMR_SEQ_URL=http://127.0.0.1:5341 AMR_SEQ_API_KEY= dotnet run --project src/Amr.Communication.Console -- --trial --host 127.0.0.1 --port 1884 --amr-id AMR_001
```

첫 위치·상태 변화·명령/ACK 등은 콘솔에 표시하고 반복 Telemetry·Heartbeat는 Seq의 Debug 로그에서 조회한다. 콘솔 시작 로그의 RunId를 복사해 Seq 검색창에 입력한다.

```sql
RunId = '실제_RunId' and AmrId = 'AMR_001' and EventType = 'TelemetryReceived'
```

로그 전송 주소 설정이나 API 응답만으로 수집 성공을 판정하지 않는다. 같은 RunId의 이벤트가 검색되는지 확인한다. Seq 장애 시 통신을 계속하는 기존 동작은 유지한다. 자세한 검색은 [통신·Seq 안내](../tests/manual/COM06/README.md#docker-seq에서-구조화-상세-로그-조회)를 따른다.

## 5. 종료·재실행과 데이터 보존

저장소에서 만든 Seq만 중지하거나 다시 시작한다.

```powershell
docker compose --env-file .env -f docker/compose.yaml --profile logging stop seq
docker compose --env-file .env -f docker/compose.yaml --profile logging start seq
```

로그 데이터는 기본 `amr-demo_seq-data` Docker 볼륨에 저장된다. `-p`로 프로젝트 이름을 바꾸면 볼륨 이름도 달라진다. 컨테이너 중지·재시작이나 볼륨을 남긴 컨테이너 재생성 뒤에도 데이터를 사용한다. 데이터 보존 중에는 `down -v`나 해당 볼륨 삭제를 실행하지 않는다. Mosquitto까지 중지할 수 있는 전체 `down` 대신 위의 서비스 지정 명령을 사용한다.

## 6. 실제 로그 수집 시험

솔루션 Debug 빌드, Docker Linux 엔진, 캐시된 Mosquitto 이미지와 실행 중인 무인증 로컬 Seq가 필요하다. 아래 도구는 Seq 조회 API를 키 없이 사용하므로 인증된 서버의 시험으로 확대하지 않는다.

```powershell
dotnet build AmrControlSystem.slnx --no-restore
pwsh -NoProfile -File tests/manual/LOG/Verify-TrialSeq.ps1 -RunId manual-seq-new-01 -SeqUrl http://127.0.0.1:5341
```

실행할 때마다 새 RunId를 사용한다. 결과는 `evidence/LOG/<RunId>/verification.json`과 Seq 조회 JSON·콘솔 로그에 남는다. 도구는 시험용 Broker·AMR만 만들고 정리하며 공유 Seq는 종료하지 않는다. 화면 캡처·실제 수집 시험·자동시험 결과는 각각 구분해 기록한다.

## 공식 설정 근거

- [Seq Docker 설정](https://datalust.co/docs/docker-deployment-overview): 이미지, `/data`, 초기 인증 환경변수
- [Compose 프로필](https://docs.docker.com/compose/how-tos/profiles/): 선택 서비스 실행
- [Compose 환경변수](https://docs.docker.com/compose/how-tos/environment-variables/variable-interpolation/): `--env-file`과 쉘 우선순위
