# Docker 로컬 적용 검증 — 20261007-docker-local-01

## 1. 목표와 적용 기준

- 관련 Issue: [#2 — 개발환경 재사용과 최소 프로젝트 구성](https://github.com/inhhlee/amr-control-system-demo/issues/2).
- 사용자의 2026-10-07 정정 요청에 따라 작업·실행 위치를 `C:\amr-control-system-demo`로 맞췄다. 현재 관리 대상은 이 로컬 폴더다.
- 기존 Issue의 Broker 재사용 범위에 사용자가 추가 요청한 프로젝트 전용 Docker 파일 준비를 반영한다. 컨테이너 기동·실제 MQTT 연결 시험은 이번 범위에 포함하지 않는다.
- 기존 계획·명세 원본과 버전 차이는 [앞선 적용 기준](../20261007-setup-02/README.md#1-목표와-적용-기준)을 유지한다. 제품 기능 ID·Topic·QoS·Retain 정책은 이번 설정 이동에 해당 없음이다.

## 2. 실행 환경과 변경

| 항목 | 실제 값 |
| --- | --- |
| 실행 ID·시각 | `20261007-docker-local-01`, Asia/Seoul. [시작 상태](baseline.log), [Compose 검사](compose-check.log), [최종 검사](repository-checks.log)에 실제 시각 기록 |
| 작업 위치 | `C:\amr-control-system-demo` |
| 브랜치·HEAD | `codex/2-dev-foundation`, `7f89908c4f1b4a0d73c8a9475ad84aa5825cbf84` + 기존 변경과 이번 미커밋 설정 |
| 도구 | Docker Compose v5.5.1, 현재 PC에 설치된 Docker CLI 사용 |
| 파일 식별 | [적용 전 SHA-256](before.sha256.json), [최종 작성 파일 SHA-256](source-files.sha256) |
| 새 Broker 구성 | `amr-demo` / `eclipse-mosquitto:2.1.2-alpine` / `127.0.0.1:1884` → `1883/tcp` |

- [Compose](../../../docker/compose.yaml)와 [Mosquitto 설정](../../../docker/mosquitto/config/mosquitto.conf)을 로컬에 추가했다. 상대 마운트는 이 로컬 Compose 파일 위치를 기준으로 해석된다.
- [README](../../../README.md)의 관리·실행 명령을 로컬 경로로 작성했다. [AGENTS](../../../AGENTS.md)에 별도 요청 없이 워크트리로 옮기지 않는 기본 작업 기준을 추가했다.
- [.gitignore](../../../.gitignore)에 Docker 실행 데이터·로그 폴더만 추가했다. 설정 파일과 검토할 증거는 추적한다.
- 작업 브랜치를 워크트리에서 로컬 폴더로 다시 연결했다. 워크트리는 같은 커밋의 detached HEAD이며 기존 파일은 삭제하거나 덮어쓰지 않았다.
- 의도한 로컬 문서·제외 규칙 외 기존 파일 75개의 SHA-256 보존을 확인했다. [이동 검사](transfer.log)
- [이전 워크트리 검증](../20261007-docker-01/README.md)의 증거 파일 8개를 내용 변경 없이 로컬에 보관했다. 당시 기록의 워크트리 경로는 과거 검증 위치이며 현재 관리 위치로 사용하지 않는다.

## 3. 입력·예상·실제 결과

| 검사 | 입력·예상 결과 | 실제 결과 | 판정·증거 |
| --- | --- | --- | --- |
| 로컬 Compose 모델 | `C:\amr-control-system-demo`에서 `docker compose -f docker/compose.yaml config --quiet`, 종료 0 | 종료 0 | 통과 — [검사 로그](compose-check.log) |
| 경로·포트 | `config --format json`: 세 마운트가 로컬 프로젝트 아래로 해석되고 루프백 1884, 설정 읽기 전용 | 조건 충족 | 통과 — [정규화된 모델](compose-resolved.json) |
| 파일 보존 | 기존 변경 보존, 이전 증거를 변경 없이 보관 | 대상 외 75개 파일 보존, 이전 증거 8개 보관 | 통과 — [이동 검사](transfer.log) |
| Git·문서 | 데이터·로그 예시는 제외, 설정·증거는 추적 가능, 링크·공백·브랜치 확인 | 최종 검사 로그에서 확인 | [최종 검사](repository-checks.log) |

## 4. 미검증 항목과 직접 확인

- 새 Broker 기동·설정 로딩·데이터 및 로그 쓰기·호스트에서 MQTT 연결/송수신은 미실행이다. [로컬 README 절차](../../../README.md#프로젝트-전용-docker-설정)에 따라 실행하는 단계에서 확인한다.
- .NET 소스는 변경하지 않았으며 이전 콘솔 OS 정책 차단을 이번 Docker 적용으로 해결하거나 재시험하지 않았다.
- 서비스·컨테이너를 시작하거나 중단하지 않았다. 커밋·푸시·PR 생성도 수행하지 않았다.

## 5. 종합 결과

Docker 파일과 실행 안내를 사용자가 지정한 로컬 폴더에 적용하고 실제 로컬 경로로 Compose 모델 검사를 통과했다. 실제 Broker 동작이나 Issue #2 전체 완료로 확대하지 않는다. 업무 기록은 `C:\process-plan\일일업무\2026-10-07.md`에 이번 위치 정정과 근거를 추가한다.
