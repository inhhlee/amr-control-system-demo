# 프로젝트 전용 Docker 설정 검증 — 20261007-docker-01

- 작성자·실행자: Codex
- 관련 Issue: [#2 — 개발환경 재사용과 최소 프로젝트 구성](https://github.com/inhhlee/amr-control-system-demo/issues/2)

## 1. 목표와 적용 기준

2026-10-07 사용자는 기존 Broker의 외부 경로 대신 현재 프로젝트에 새 Docker 설정을 준비하고, 워크트리에서 작업하도록 요청했다. 이번 범위는 Compose·Mosquitto 설정 파일, 생성물 제외 규칙, 실행 안내와 설정 검사다. 기존 Issue #2의 Broker 재사용 범위에 대한 사용자 추가 요청으로 기록하며, GitHub Issue 본문은 변경하지 않았다.

Docker Desktop은 설치된 환경을 사용한다. 새 프로젝트 이름은 `amr-demo`, 이미지는 `eclipse-mosquitto:2.1.2-alpine`, 호스트 접속 주소는 `127.0.0.1:1884`다. 모든 마운트는 Compose 파일 기준 상대 경로를 사용한다. Mosquitto의 익명 접속은 호스트 루프백 포트로 공개하는 로컬 개발 구성이며 제품 인증 정책을 확정한 것이 아니다.

명세·계획의 원본 위치와 읽은 버전은 [앞선 적용 기준](../20261007-setup-02/README.md#1-목표와-적용-기준)을 따른다. 새 제품 기능 ID, Topic·QoS·Retain 정책, MQTT 기능 구현은 해당 없음이다. 이번 요청에는 실제 컨테이너 기동이나 MQTT 연결·송수신 시험을 포함하지 않는다.

## 2. 실행 환경과 변경

| 항목 | 실제 값 |
| --- | --- |
| 실행 ID·시각 | `20261007-docker-01`. 시작·종료 시각은 [시작 상태](baseline.log), [Compose 검사](compose-check.log), [파일 검사](repository-checks.log)에 기록. Asia/Seoul |
| 작업 경로 | `C:\Users\Admin\.codex\worktrees\2144\amr-control-system-demo` |
| 브랜치·코드 | `codex/2-dev-foundation`, HEAD `7f89908c4f1b4a0d73c8a9475ad84aa5825cbf84` + 기존 미커밋 변경과 이번 파일 변경 |
| 도구 | Windows 10.0.26200, PowerShell 7.6.5, Docker Compose v5.5.1 |
| 입력 식별 | [변경 전 파일 SHA-256](before.sha256.json), [이번 작성 파일 SHA-256](source-files.sha256) |
| 컨테이너 조회 | `amr-demo` 프로젝트 컨테이너 0개. 기존 `amr-mosquitto`는 running, `127.0.0.1:1883` 유지 |

기존 작업 브랜치를 현재 워크트리에서 재사용하기 위해 원래 `C:\amr-control-system-demo` 폴더는 같은 커밋에서 detached HEAD로 두고 브랜치를 현재 워크트리에 연결했다. 이동 전후 두 작업 사본 파일 68개의 SHA-256이 같음을 확인했다. 원래 폴더의 미커밋 변경은 보존했으며 새 Docker 파일은 현재 워크트리에만 작성했다. 기존 PR 목록은 비어 있었다.

| 변경 파일 | 이유 |
| --- | --- |
| [compose.yaml](../../../docker/compose.yaml) | 전용 프로젝트 이름·이미지·루프백 1884 포트·상대 마운트 구성. 설정 파일은 읽기 전용이며 파일이 없을 때 디렉터리를 대신 생성하지 않음 |
| [mosquitto.conf](../../../docker/mosquitto/config/mosquitto.conf) | 컨테이너의 1883 리스너, 로컬 개발용 익명 접속, 데이터 저장·stdout와 파일 로그 설정 |
| [.gitignore](../../../.gitignore) | `docker/mosquitto/data/`, `docker/mosquitto/log/`만 새 제외 대상으로 추가 |
| [README](../../../README.md) | 현재 작업 사본 기준 명령, 새 Broker 설정·기동·확인·중단 절차와 미검증 상태 안내 |
| [AGENTS](../../../AGENTS.md) | 워크트리의 실제 경로를 작업·검증 기준으로 사용하도록 한 항목 추가 |

## 3. 입력·예상 결과와 실제 판정

| 검사 | 명령·입력과 예상 결과 | 실제 결과·판정 | 증거 |
| --- | --- | --- | --- |
| Compose 모델 | `docker compose -f docker/compose.yaml config --quiet`, 종료 0 | 종료 0, 통과 | [검사 로그](compose-check.log) |
| 경로·공개 범위 | `config --format json`의 프로젝트·이미지·포트·세 마운트 검사. 현재 워크트리 안의 경로, 루프백 1884, 설정 읽기 전용 예상 | 조건 충족, 통과 | [정규화된 모델](compose-resolved.json), [검사 로그](compose-check.log) |
| 작업 디렉터리 차이 | 저장소 루트의 `-f docker/compose.yaml`과 `docker/` 폴더의 `-f compose.yaml` 결과가 동일해야 함 | 정규화된 모델 동일, 통과 | [검사 로그](compose-check.log) |
| 기존 환경과 충돌 | 프로젝트 라벨로 컨테이너 목록 조회, 기존 Broker 상태 및 1883·1884 리스너 조회 | 새 프로젝트 컨테이너 0개, 기존 Broker running·1883. 조회 시 1884 리스너 없음 | [검사 로그](compose-check.log), [리스너 조회](listeners.json) |
| Git·문서 | 생성 데이터·로그 경로 예시는 제외, 설정·증거는 추적 가능. 링크·공백·기존 파일 보존 확인 | 생성물 예시 2개 제외, 설정·증거 예시 6개 유지, 링크 57개 확인, 대상 외 기존 파일 31개 보존. 통과 | [파일 검사](repository-checks.log) |

Docker CLI는 사용자 폴더 실행 권한이 필요해 승인 후 검사했다. `compose config`는 컨테이너를 만들거나 기동하지 않는다. 이 검사는 YAML·경로·Compose 모델에 대한 것으로 Mosquitto가 실제 설정을 로딩했다는 근거는 아니다.

## 4. 미검증 항목과 직접 확인

- 새 Broker 기동과 실제 마운트·데이터·로그 쓰기: 미실행. [README의 Docker 절차](../../../README.md#프로젝트-전용-docker-설정)로 현재 작업 사본에서 기동한 뒤 서비스 상태와 포트·로그를 확인한다.
- Windows 호스트에서 `127.0.0.1:1884`로 MQTT 연결·발행·구독: 미실행. 실제 연결을 시험하는 단계에서 별도 실행 증거를 남긴다.
- .NET 콘솔의 OS 정책 차단: 이번 Docker 설정으로 해결하거나 재시험하지 않았다. [이전 실패 기록](../20261007-setup-02/console.log)을 보존한다.
- 커밋·푸시·PR·리뷰·병합: 사용자가 결과 확인 후 요청하는 단계다. 수행하지 않았다.

## 5. 종합 결과

프로젝트 전용 Docker 파일을 현재 워크트리에 준비했고 Compose 모델 검사에 통과했다. 실제 Broker 실행이나 제품 기능 통과로 확대하지 않으며 Issue #2 전체 완료로 표시하지 않는다. 기존 서비스·컨테이너·운영체제 정책은 변경하지 않았다.

관련 업무 요약은 `C:\process-plan\일일업무\2026-10-07.md`의 같은 날짜 기록에 추가했다. 기존 본문이 그대로 보존됐음을 확인했다.
