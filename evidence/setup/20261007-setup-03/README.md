# 개발 기반 콘솔 재시험 검증결과 — 20261007-setup-03

- 작성자·실행자: Codex
- 대상: [Issue #2 — 개발환경 재사용과 최소 프로젝트 구성](https://github.com/inhhlee/amr-control-system-demo/issues/2)
- 판정: SETUP-03 통과. 콘솔 준비 안내 출력과 정상 종료를 확인했다.

## 1. 목표와 적용 기준

- 사용자가 실행 차단을 해결했다고 보고한 뒤 기존 준비 콘솔을 같은 명령으로 재시험했다.
- Issue #2의 SETUP-03 완료조건은 준비 안내 한 줄과 종료 코드 0이다. 이번 확인에는 새 기능을 구현하지 않았다.
- 문서 위치·버전 차이와 최소 구성의 적용 기준은 [이전 검증결과](../20261007-setup-02/README.md#1-목표와-적용-기준)를 유지한다. 확인하지 못한 문서 버전을 확인한 것으로 바꾸지 않았다.
- 제품 기능 ID·spec_revision·verification_index: 해당 없음. 최소 준비 콘솔이며 MQTT·GUI·Core·DB 기능은 없다.
- Windows 보안 설정은 Codex가 변경하지 않았다. 사용자가 수행한 구체적인 해결 방법과 정책 변경 내용은 확인하지 않았다.

## 2. 실행 환경과 코드

| 항목 | 실제 값 |
| --- | --- |
| 실행 ID | `20261007-setup-03` |
| 시작 / 종료 | `2026-10-07T16:04:43.3133986+09:00` / `2026-10-07T16:04:43.8831032+09:00`, Asia/Seoul |
| 작업 위치 | `C:\amr-control-system-demo` |
| 브랜치 / 원격 | `codex/2-dev-foundation` / `https://github.com/inhhlee/amr-control-system-demo.git` |
| 기준 커밋 | `7f89908c4f1b4a0d73c8a9475ad84aa5825cbf84` + 기존 미커밋 변경 |
| OS / 셸 | `Microsoft Windows NT 10.0.26200.0` / PowerShell `7.6.5` |
| SDK / 설치 런타임 | .NET SDK `10.0.401` / Microsoft.NETCore.App 10.0.12 |
| 소스·프로젝트 | SDK 선택·솔루션·세 프로젝트·Program.cs 6개가 이전 검증본과 SHA-256 일치 |
| 실행 입력 | 기존 `bin/Debug/net10.0` 빌드 결과. 이번에는 복원·빌드를 반복하지 않음 |
| Broker·Topic·QoS·Retain | 해당 없음. 준비 콘솔은 Broker를 사용하지 않음 |

현재 환경과 실행 DLL·runtimeconfig의 SHA-256은 [환경 기록](environment.json), 이전 소스와의 대조는 [소스 비교](source-comparison.json)에 있다. 수정 후 공유 파일의 식별값은 [source-files.sha256](source-files.sha256)에 기록했다.

## 3. 입력과 절차

| 시험 ID | 선행 조건·입력 | 명령 | 예상 결과 |
| --- | --- | --- | --- |
| SETUP-03 재시험 | 로컬 저장소의 기존 준비 콘솔 빌드 결과. 사용자 보고 후 실행 | `dotnet run --project src/Amr.Communication.Console --no-build` | `AMR 관제시스템 개발 준비가 완료되었습니다.` 한 줄, 종료 코드 0 |

명령 출력과 즉시 수집한 `$LASTEXITCODE`, 시작·종료 시각은 [실행 기록](execution.json)과 [콘솔 로그](console.log)에 연결한다.

## 4. 실제 결과와 판정

| 시험 ID | 실제 결과 | 판정 | 증거 |
| --- | --- | --- | --- |
| SETUP-03 | 예상 준비 안내 한 줄 출력, 종료 코드 0. 이번 실행에서 정책 차단 오류가 발생하지 않음 | 통과 | [콘솔 로그](console.log), [실행 기록](execution.json) |
| 소스 동일성 확인 | SDK 선택·솔루션·세 프로젝트·Program.cs 6개가 이전 실행과 동일 | 통과 | [소스 비교](source-comparison.json) |

복원·빌드·xUnit 탐색은 같은 코드의 [첫 검증](../20261007-setup-01/README.md) 근거를 연결하며 이번에 반복하지 않았다. 빈 시험 프로젝트의 탐색 0건을 제품 기능 시험 통과로 표시하지 않는다.

## 5. 문제와 후속 조치

- 이전 `0x800711C7` 실패와 Code Integrity 이벤트는 [setup-01](../20261007-setup-01/README.md), [setup-02](../20261007-setup-02/README.md)에 그대로 보존한다.
- 이번 실행 통과만으로 보안 정책이 영구적으로 변경됐거나 모든 DLL의 실행이 허용된 것으로 판정하지 않는다. 해결 방법은 사용자 보고의 범위를 넘겨 추정하지 않는다.
- 새 프로젝트 전용 Broker의 실제 기동·Mosquitto 설정 로딩·데이터/로그 쓰기·MQTT 연결 및 송수신은 이번에 실행하지 않았다. [Docker 로컬 검증결과](../20261007-docker-local-01/README.md)는 파일 준비·Compose 모델 검사의 근거다.
- 커밋·푸시·PR 생성·리뷰·병합은 사용자가 별도 요청할 단계이며 수행하지 않았다.

## 6. 종합 결과

- 콘솔 준비 안내 출력·정상 종료 완료조건을 충족했다. 사용자 보고와 별도로 Codex가 현재 로컬 저장소에서 직접 실행해 확인했다.
- 소스와 Windows 보안 설정을 변경하지 않고 재시험 기록을 추가했으며 README의 현재 실행 상태와 같은 날짜 일일업무에 결과를 연결했다.
- 새 Broker 실행 검증과 후속 GitHub 절차가 남아 있으므로 Issue #2 전체 완료·`Closes #2` 적용 여부는 별도 단계에서 판단한다.
- 변경 대상 외 기존 파일·과거 증거의 보존, 문서 링크·공백 검사 결과는 [저장소 검사](repository-checks.log)에 기록한다.

## 7. Issue 제외 범위 대조와 판정 정정 — 2026-10-07

- 사용자의 지적에 따라 [Issue #2](https://github.com/inhhlee/amr-control-system-demo/issues/2)의 현재 본문·제외 범위를 다시 확인했다. Issue 댓글의 추가 지시는 없었다.
- Issue는 MQTT 연결 로직·메시지 송수신·Heartbeat·자동 재접속, GUI·Core·DB·다른 COM 기능, 기존 완성 소스·추적기·과거 통과 기록의 복사, 실제 Broker 연결 시험과 COM-03 완료 판정을 제외한다.
- 6절에서 새 Broker 실행 검증을 Issue #2 전체 완료 판단의 남은 작업처럼 기록한 부분은 정정한다. 이 미실행 항목은 제외된 후속 범위이므로 이번 준비 Issue의 미충족 완료조건으로 취급하지 않는다. 당시 판정 문장은 이력으로 보존하고 현재 판단은 이 절을 적용한다.
- 사용자가 추가 요청한 Docker 파일 준비·로컬 경로 적용·Compose 모델 검사는 완료했다. 컨테이너 기동이나 실제 MQTT 시험을 추가 요청한 것으로 확대하지 않는다.

| Issue #2 완료조건 | 현재 확인 범위 | 근거 |
| --- | --- | --- |
| 문서 위치·버전과 기존 변경 처리 범위 | 기록 완료 | [적용 기준과 시작 상태](../20261007-setup-02/README.md) |
| 도구 버전·기존 Broker 상태·관리 위치·주소 | 조회·기록 완료 | [환경 조회](../20261007-setup-02/docker-broker.log) |
| 새 솔루션 복원·빌드 | 통과 | [복원](../20261007-setup-01/restore.log), [빌드](../20261007-setup-01/build.log) |
| 준비 콘솔 안내·정상 종료 | 통과. Connected 출력 없음 | [콘솔 로그](console.log) |
| 시험 프로젝트 참조·탐색 | 빌드·탐색 확인. 시험 0건, 제품 기능 통과 아님 | [탐색](../20261007-setup-01/test-discovery.log), [진단](../20261007-setup-01/test-discovery-diagnostic.log) |
| 협업 규칙·PR 양식·ignore·추적 대상 | 검사 통과 | [저장소 검사](../20261007-setup-02/repository-checks.log) |
| README의 명령 재현·실제 결과 연결 | 복원·빌드·탐색·콘솔 실행 근거 연결 완료 | [첫 실행](../20261007-setup-01/README.md), [콘솔 재시험](console.log) |
| 실제 Issue·브랜치·PR 연결과 리뷰·반영 | Issue #2·작업 브랜치 연결. PR 생성·리뷰·반영은 아직 미진행 | 현재 `codex/2-dev-foundation`, 커밋·푸시·PR 생성은 별도 요청 단계 |

- 현재 판정: 개발 구성·로컬 검증 범위는 완료했다. Issue의 마지막 완료조건인 실제 PR 연결·리뷰·반영 절차는 남아 있어 Issue 전체 완료나 병합 완료로 표시하지 않는다.
- 이번 정정은 문서의 범위·판정만 보완한다. 실행 시점의 파일 식별값·성공/실패 로그와 기존 검증 기록은 그대로 보존한다. 제품 소스·컨테이너·보안 정책을 변경하거나 시험을 다시 실행하지 않았다.
