# 개발 기반 추가 검증결과 — 20261007-setup-02

- 작성자·실행자: Codex
- 대상: [Issue #2 — 개발환경 재사용과 최소 프로젝트 구성](https://github.com/inhhlee/amr-control-system-demo/issues/2)
- 작업 상태: 진행 중. 콘솔 정상 실행과 후속 PR 절차가 남음.

## 1. 목표와 적용 기준

- 사용자가 요청한 구현·시험 단계에서 기존 `codex/2-dev-foundation` 브랜치와 미커밋 구성을 이어서 확인한다. 해당 저장소의 PR 목록 조회 결과 기존 PR은 없었다.
- Issue #2의 SDK·패키지·프로젝트 구조와 완료조건을 적용한다. 별도 제품 기능 ID는 없으며 COM-03 MQTT 연결 구현은 후속 범위다.
- 원본 문서는 현재 접근 가능한 `C:\process-plan`에서 읽었다. 문서 읽기 성공을 프로젝트 등록이나 쓰기 권한 확인으로 간주하지 않는다.
- 기존 AGENTS.md·README.md·PR 양식의 사용자 변경과 이전 검증 기록을 보존했다. 소스·설정은 요구를 이미 충족해 재작성하지 않았다. 이번 변경은 README의 현재 환경·적용 기준·직접 확인 절차와 이 실행의 증거다.

| 문서명·원본 위치 | 읽은 버전 | Issue 참조와 차이 |
| --- | --- | --- |
| 과제1 통신 서버와 클라이언트 과제계획 — `C:\process-plan\관제시스템 과제 진행\과제1\과제계획.md` | v0.9 초안 | Issue의 v0.10을 확인하지 못함 |
| Codex와 GitHub 기반 관제 프로젝트 개발 진행 절차 — `C:\process-plan\기준\개발 진행 절차.md` | v0.6 | Issue의 v0.4보다 이후 버전. 현재 문서 원본 위치와 단계별 요청 적용 |
| 관제시스템 개발환경 — `C:\process-plan\관제시스템 과제 진행\개발환경.md` | v0.7 | Issue의 v0.6보다 이후 버전. 도구 버전은 현재 PC 조회 결과 적용 |
| 가상 AMR 관제 시스템 기능명세 — `C:\process-plan\관제시스템 과제 진행\명세\기능명세.md` | v0.7 초안 | Issue 참조와 일치. COM-03 구현은 이번 범위 밖 |
| 가상 AMR 관제 시스템 검토 및 결정사항 — `C:\process-plan\관제시스템 과제 진행\명세\검토 및 결정사항.md` | v0.5 초안 | MQTT 동작 정책을 이번 준비 작업에서 확정하지 않음 |
| DEMO-SETUP | 원본 미확인 | Issue가 참조한 v0.1과 과제계획 v0.10 문자열을 현재 문서 폴더에서 찾지 못함. [검색 결과](document-search.json) |

확인하지 못한 원본은 읽은 것으로 표시하지 않는다. 이번 최소 구성에는 Issue 본문이 파일·고정 버전·예상 실행 결과를 명시하므로 이를 적용한다. 후속 기능 착수 전에는 전달 문서의 위치·버전·관련 ID를 다시 확인해야 한다. 코드 추적기의 `spec_revision`·`verification_index`는 아직 없는 최소 구성이라 해당 없음이다.

## 2. 실행 환경과 코드

| 항목 | 실제 값 |
| --- | --- |
| 실행 ID·시작 시각 | `20261007-setup-02`, 2026-10-07 14:46:54 +09:00 (Asia/Seoul). 검사 종료 시각은 [저장소 검사](repository-checks.log)에 기록 |
| 코드 위치·브랜치 | `C:\amr-control-system-demo`, `codex/2-dev-foundation` |
| Git HEAD·변경 | `7f89908c4f1b4a0d73c8a9475ad84aa5825cbf84` + 기존 미커밋 변경. [시작 상태](baseline.log) |
| 코드 식별 | [이전 실행과 SHA-256 비교](source-comparison.json), [최종 작성 파일 SHA-256](source-files.sha256) |
| OS·도구 | Windows 10.0.26200 x64, PowerShell 7.6.5, Git 2.55.0.windows.5, SDK 10.0.401 |
| 패키지 | MQTTnet 5.2.0.1603 / Microsoft.NET.Test.Sdk 17.14.1 / xunit 2.9.3 / xunit.runner.visualstudio 3.1.4 |
| Docker | Desktop 4.90.0 (238679), Client·Engine 29.7.2, context `desktop-linux` |
| Docker CLI | `C:\Users\Admin\AppData\Local\Programs\DockerDesktop\resources\bin\docker.exe`. 현재 PATH에 없음 |
| Broker | `amr-mosquitto`, `eclipse-mosquitto:2.1.2-alpine`, running. `127.0.0.1:1883` → `1883/tcp` |
| Broker 관리 위치 | Compose `C:\Monit\docker\docker-compose.yml`, 설정 `C:\Monit\docker\mosquitto\config\mosquitto.conf` |
| Topic·QoS·Retain·시험 대역 | 해당 없음. 콘솔은 준비 안내만 출력하며 MQTT 연결을 수행하지 않음 |

Docker CLI 실행은 샌드박스에서 접근 거부되어 승인 후 일반 사용자 환경에서 읽기 전용 조회했다. 컨테이너 시작·중단·재설정이나 OS 정책 변경은 수행하지 않았다. [Docker·Broker 조회](docker-broker.log)에는 현재 포트·마운트·Compose 관리 위치와 파일 존재 여부를, [설정 확인](broker-config.json)에는 인증정보가 아닌 필요한 지시문만 기록했다. 컨테이너 상태와 호스트 리스너 확인을 MQTT 프로토콜 시험 통과로 해석하지 않는다.

## 3. 입력과 절차

| 시험 ID | 입력·선행 조건·명령 | 예상 결과 |
| --- | --- | --- |
| SETUP-06 | 이전 실행의 `source-files.sha256`와 현재 파일에 `Get-FileHash -Algorithm SHA256` 적용 | 성공한 검증에 사용한 코드·설정과 현재 파일의 동일 여부 확인 |
| SETUP-07 | 발견한 CLI의 `version`, `context show`, `inspect amr-mosquitto` 중 상태·포트·마운트·Compose 필드만 조회. 호스트 TCP 리스너·설정 파일 확인 | 도구 버전과 Broker 상태·주소·관리 위치 기록 |
| SETUP-03 재시험 | 기존 빌드 결과를 입력으로 `dotnet run --project src/Amr.Communication.Console --no-build` | `AMR 관제시스템 개발 준비가 완료되었습니다.` 한 줄과 종료 코드 0 |
| SETUP-08 | 실행 시각의 Code Integrity 이벤트 3033·3077 중 이번 저장소 경로만 조회 | 콘솔 실패 원인과 관련 DLL·정책 확인 |
| SETUP-05 재확인 | `git check-ignore --no-index`로 생성물·개인 설정·소스·공유 예시·증거 확인, 변경 문서의 링크·공백 검사 | 생성물·개인 설정 제외, 소스·예시·증거 유지, 링크 연결 |

## 4. 실제 결과와 판정

| 시험 ID | 실제 결과 | 판정 | 근거 |
| --- | --- | --- | --- |
| SETUP-06 | SDK 선택 파일·솔루션·세 프로젝트·Program.cs·.gitignore가 이전 실행과 동일. AGENTS·README·PR 양식은 문서 변경 있음 | 통과 | [SHA-256 대조](source-comparison.json) |
| SETUP-01·02·04 | 동일 소스의 이전 실제 복원·빌드·시험 탐색 결과를 연결. 이번 실행에서 반복하지 않음 | 이전 통과 근거 유지. 제품 시험 통과 아님 | [복원](../20261007-setup-01/restore.log), [빌드](../20261007-setup-01/build.log), [xUnit 탐색 진단](../20261007-setup-01/test-discovery-diagnostic.log) |
| SETUP-07 | Desktop 4.90.0 / Engine 29.7.2, Mosquitto 컨테이너 running, 루프백 1883 포트와 C:\Monit\docker 관리 경로 확인 | 환경 조회 통과. 실제 MQTT 연결 미실행 | [환경 조회](docker-broker.log), [호스트 설정](broker-config.json) |
| SETUP-03 재시험 | 14:47:22 실행에서 DLL 로딩 정책 차단 `0x800711C7`, 종료 코드 -532462766. 예상 준비 안내는 출력되지 않음 | 실패 — OS 정책. 정상 출력·종료 미검증 | [콘솔 로그](console.log) |
| SETUP-08 | 같은 시각 이벤트 3033·3077, 콘솔 DLL 서명 수준·정책 위반. 정책 ID `{0283ac0f-fff1-49ae-ada1-8a933130cad6}` | 원인 확인 | [현재 실행의 이벤트](code-integrity.json) |
| SETUP-05 재확인 | 생성물 162개·개인 설정 등 예시 6개 제외, 소스·증거·공유 예시 37개 유지. 변경 문서 링크와 작성 파일 공백 검사 통과 | 통과 | [저장소 검사](repository-checks.log) |

이전 빌드는 경고 0·오류 0이었다. xUnit 어댑터 3.1.4는 시험 어셈블리를 탐색했으며 시험은 0건이다. 빈 시험 탐색을 기능 시험 통과로 표시하지 않는다.

## 5. 완료조건과 남은 작업

- [x] 실제 문서 위치·버전 차이와 기존 변경 범위를 기록했다.
- [x] SDK·Git·Docker 버전과 Broker 상태·관리 경로·접속 주소를 확인했다.
- [x] 현재와 동일한 소스의 의존성 복원·빌드 성공 근거가 있다.
- [ ] 콘솔의 준비 안내 출력·정상 종료. OS 정책 차단 때문에 미충족이다.
- [x] 시험 프로젝트 참조가 빌드되고 xUnit 탐색이 완료된 근거가 있다. 시험 0건이다.
- [x] 협업 규칙·PR 양식·제외 규칙이 현재 구성에 맞고, 생성물 제외·소스·공유 예시·증거 추적 검사를 통과했다.
- [ ] README 명령의 정상 재현. 실패 결과·재개 절차는 기록했으나 콘솔 정상 실행이 남았다.
- [ ] 커밋·푸시·PR·리뷰·병합. 사용자가 결과 확인 후 요청하기로 한 후속 단계다.

직접 재검증할 순서와 기대 결과는 [저장소 README](../../../README.md#직접-확인할-절차)에 있다. Code Integrity의 해당 이벤트를 근거로 기기 관리 담당자에게 개발 빌드 실행을 허용하는 방법을 확인한 후 동일한 콘솔 명령과 `$LASTEXITCODE`로 안내 한 줄·0을 확인한다. 새 재시험 결과는 별도 실행 ID로 보존한다.

## 6. 종합 결과

최소 개발 구성과 복원·빌드·시험 탐색 근거를 확인했고, 이번 실행에서 Broker 환경의 미확인 항목을 보완했다. 콘솔 실행 실패가 남아 Issue #2 전체 완료나 `Closes #2` 적용 상태로 판정하지 않는다. MQTT 연결·메시지 교환·GUI·Core·DB는 이번 준비 범위에 포함되지 않는다.

원본 과제계획·제품 기능 상태를 완료로 바꾸지 않았으며, 커밋·푸시·PR 생성은 수행하지 않았다. 같은 날짜의 기존 일일업무가 없어 [2026-10-07 일일업무](C:/process-plan/일일업무/2026-10-07.md)를 만들고 이 검증결과를 연결했다. 문서 저장소의 기존 사용자 변경은 보존했다.
