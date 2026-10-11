# COM-03 A3 연결 유지·Ctrl+C 종료 검증

- 실행 ID: `20261011-a3-shutdown-03`, 작성·실행: Codex, 2026-10-11.
- 관련 Issue: [#8](https://github.com/inhhlee/amr-control-system-demo/issues/8), 총괄 [#4](https://github.com/inhhlee/amr-control-system-demo/issues/4).

## 1. 목표와 적용 기준

A2의 MQTT 승인 확인 뒤 연결을 유지하고, Ctrl+C 요청에 명시적 해제를 기다린 후 자원을 정리하고 종료 코드 0을 반환한다. 별도 프로세스 재실행은 새 Client ID로 정상 접속해야 한다.

적용 원본은 C:\process-plan의 기능명세 v0.7 COM-03, 요구사항정의 v0.5 R-04, 공통규칙 v0.4 C-04·C-09, 결정사항 v0.5 D-08·D-09, 과제계획 v0.18, COM03-A v0.8 및 통합 #8 원본 v0.5다. Issue가 인용한 과제계획 v0.17과 실제 v0.18의 차이는 업무 기록 연결이며 기능 범위는 통합 #8을 따른다. 이 검증 뒤 업무 기록용 버전 갱신은 시험 기준 변경이 아니다. 별도 기능 추적기의 spec_revision/verification_index는 이 실습 저장소에 없어 해당 없음.

#5의 상세 입력·접속 실패/시간 초과·취소 경쟁, #6의 비정상 단절/KeepAlive 감시, 메시지 송수신·자동 재접속·GUI·Core·DB는 이번 완료 범위가 아니다.

## 2. 실행 환경과 코드

| 항목 | 실제 값·근거 |
| --- | --- |
| 실행 시각 | 두 정상 실행 2026-10-11 07:52:19~07:52:26 UTC / 16:52:19~16:52:26 KST. 대조·접속 실패 확인 07:52:57 UTC까지 |
| OS / SDK | Windows 10.0.26200 x64 / .NET SDK 10.0.401, 런타임 10.0.12 |
| 라이브러리 | MQTTnet 5.2.0.1603 / xUnit 2.9.3, runner 3.1.4, Test SDK 17.14.1 |
| 기준 코드 | `855d76d28a8fd096f2328aefe306c3161c13eb26` + `feature/8-mqtt-connect`의 미커밋 A2/A3 변경 |
| 코드 보존 | [소스 ZIP](a3-source.zip), [파일·ZIP SHA-256](source-version.json) |
| 실제 Broker | Mosquitto 2.1.2, `amr-demo-mosquitto-1`, 컨테이너 `144fb936912c`, `127.0.0.1:1884 → 1883` |
| 컨테이너 상태 | [시험 전](../20261011-a3-shutdown-02/broker-before.json)·[시험 후](broker-after.json) 같은 ID·running. 기존 Broker를 재사용하고 시작·중단·재기동하지 않음 |
| 연결 설정 | MQTT 5, Clean Start, Session Expiry 0, KeepAlive 5초, 연결 제한 3초·해제 제한 3초, 실행별 GUID ID |
| Topic / QoS / Retain | 해당 없음 — 접속·유지·종료 시험이며 메시지는 송수신하지 않음 |
| 시험 구분 | 자동시험은 IMqttClient 시험 대역. 아래 정상·실패 실행은 실제 MQTTnet·TCP·Broker |

[A2와 파일 해시 비교](a2-comparison.json): 통신 클래스·설정·프로젝트·SDK·Docker 설정은 A2와 같고 Program.cs에 대기·취소 흐름을 추가했다. 시험 파일 2개를 새로 추가했다. A3 착수 전 A2 시험본과 작업 소스 일치를 확인했으며 [A2 증거·스냅샷](../20261011-a2-connect-02/README.md)을 보존한다.

## 3. 입력·절차와 예상 결과

| 시험 | 입력·절차 | 예상 결과 |
| --- | --- | --- |
| 복원·빌드·자동시험 | [01 복원](../20261011-a3-shutdown-01/commands.jsonl) → 시험 코드 수정 → [02 빌드·test 명령](../20261011-a3-shutdown-02/commands.jsonl) | 빌드 오류 없음, 6개 시험 통과 |
| 실제 정상 1·2 | [스크립트](verify-native-console.ps1)로 빌드한 콘솔 EXE를 별도 프로세스로 시작. host 127.0.0.1, port 1884, 연결 제한 3초. WaitingForShutdown 뒤 3초 유지. 해당 자식의 전용 콘솔에 Windows CTRL_C_EVENT 전달 | Ctrl+C 전에 살아 있음, 해제 완료 후 Dispose, 실제 Process.ExitCode 0, 두 실행의 ID 다름 |
| 실제 실패 정리 | 포트 1에 리스너 없음 확인 후 같은 EXE에 `--host 127.0.0.1 --port 1 --connect-timeout-seconds 3` 전달 | Connected 출력 없음, 오류·Disposed, 종료 1 |
| Broker·잔여 프로세스 | `docker compose -f docker/compose.yaml logs --no-color --timestamps --since 2026-10-11T07:47:38Z mosquitto`, `ps -a --format json`, 시험 자식 PID 조회 | 각 ID의 승인·정상 해제 로그, 시험 프로세스 종료, 기존 컨테이너 유지 |

재실행할 때는 스크립트를 새 실행 ID 폴더에 복사하여 기존 로그를 보존한다. 네이티브 시험 스크립트는 독립 PowerShell 프로세스에서 실행한다. 자기 자식과 도우미 외 다른 콘솔 프로세스가 있으면 Ctrl+C 이벤트를 보내지 않는다.

## 4. 실제 결과와 판정

| 시험 | 실제 결과 | 판정·증거 |
| --- | --- | --- |
| 복원·빌드 | 복원 성공, 수정 후 빌드 경고 0·오류 0 | 통과 — [02](../20261011-a3-shutdown-02/README.md) |
| 자동시험 | 승인 전 false·승인 후 true, 연결 옵션, 승인 거부, 예외 뒤 정리, 해제 완료 대기·반복 정리, 접속 전 정리·정리 후 재사용 방지: 6개 통과 | 통과 — [TRX](../20261011-a3-shutdown-02/tests.trx) |
| 정상 실행 1 | Client `amr-demo-136a74dd6587439e9da6724a9d0128c8`, PID 4144. 3초 유지 후 Ctrl+C·해제·정리·실제 종료 0 | 통과 — [결과](native-1.json), [출력](native-1.stdout.log) |
| 정상 실행 2 | Client `amr-demo-562c791e623a417099ec695a5ff5b638`, PID 17264. 새 ID, 같은 순서·실제 종료 0 | 통과 — [결과](native-2.json), [출력](native-2.stdout.log) |
| Broker 대조·잔여 확인 | 두 ID 각각 승인·disconnected 로그. 시험 자식 PID 없음, 같은 컨테이너 running | 통과 — [Broker 로그](broker.log), [검증 요약](verification.json) |
| 접속 실패 | Connected 없음, Disposed·ExitCode=1, 오류 출력·실제 종료 1 | 통과 — [stdout](failure.stdout.log), [stderr](failure.stderr.log), [요약](verification.json) |

정상 순서: Connecting → Connected → WaitingForShutdown → ShutdownRequested → Disconnecting → Disconnected → Disposed → ExitCode=0. 프로세스 재실행은 새 Client를 만드는 시험이며 자동 재접속 시험이 아니다. Session Expiry 0 적용·Broker 해제·프로세스 종료를 확인했으며 Broker 내부 세션 DB를 직접 덤프한 시험은 아니다.

## 5. 문제와 후속 조치

- [01](../20261011-a3-shutdown-01/README.md): 시험 코드의 MQTTnet 옵션 API 참조 오류. 수정 후 빌드·자동시험 통과.
- [02](../20261011-a3-shutdown-02/README.md): 시험 도우미의 Ctrl+C 무시 플래그 상속 때문에 두 번째 실행의 종료 신호가 무시됨. 실패와 강제 정리 기록을 보존했고 도우미 수정 후 03에서 두 번 통과. 제품 코드는 성공한 빌드 이후 변경하지 않음.
- [도구에서 Git Bash를 호출한 별도 시도](gitbash-terminal.json): 앱의 정상 해제·Disposed·ExitCode=0 출력은 확인했으나 도구 외부 PowerShell이 Ctrl+C로 끝나 `echo $?`까지 수집하지 못했다. 도구가 bash 셸을 직접 선택한 선행 시도는 WSL의 /bin/bash 부재로 앱 실행 전에 실패했다. 사용자 Git Bash에서의 셸 종료 코드는 이 도구 시험으로 검증했다고 주장하지 않는다. 앱의 실제 OS 종료 코드는 위 독립 자식 Process.ExitCode로 검증했다.
- 숨긴 콘솔의 리디렉션 로그에서는 한글 안내문 인코딩이 깨져 보일 수 있다. 원본 로그는 그대로 보존했으며 영문 상태·ID·UTC 시각과 네이티브 종료 코드로 판정했다. 도구 터미널의 한글 안내는 정상 출력됐다.
- `Console.CancelKeyPress` 처리에서 `Cancel=true`로 기본 종료를 막고 취소 요청을 전달한다. [공식 .NET 문서](https://learn.microsoft.com/en-us/dotnet/api/system.console.cancelkeypress?view=net-10.0). 해제에는 취소된 종료 토큰을 전달하지 않는다. 강제 종료·전원 차단 정리는 보장 범위 밖이다.

## 6. 완료조건 대응과 남은 단계

| #8 완료조건 / 총괄 #4 근거 | 확인 범위 |
| --- | --- |
| 선행 A1·Broker 기준 | A1 PR #12 develop 반영 확인. 현재 환경·설정·컨테이너 대조 |
| 인자·설정·실행별 ID | 콘솔 실제 입력·출력, 설정 옵션 자동시험 |
| MQTT 승인 후에만 접속 성공 | 승인 대기·거부 시험 및 Program.cs의 await 뒤 출력. 실제 정상/실패 출력 |
| A2 중간 결과 보존·A3 전 대조 | A2 소스 ZIP·해시·로그, 최종 비교 JSON |
| Ctrl+C 정상 종료 | 실제 전용 콘솔 신호 두 번, 해제·Dispose 순서·실제 종료 0 |
| 새 프로세스 재실행 | 두 ID 다름, 두 자식 종료, Broker 연결·해제 대응 |
| 자동시험·실제 시험 구분 | 자동 6개와 실제 MQTT 2회·실패 정리 1회 구분 |
| 복원·빌드·README·증거 | 기록·명령·소스 버전·실패 이력 보존 |
| Issue/PR 연결·총괄 최종 확인 | 로컬 결과표 작성. 커밋·푸시·PR·리뷰·병합·원격 완료 체크·Issue 종료는 미수행 |

정상 접속·유지·종료의 구현·로컬 시험은 통과했다. #8의 개발 절차 전체·총괄 #4·COM-03 전체·과제1 전체 완료로 표시하지 않는다. 다음은 사용자 코드 학습·수동 확인과, 요청된 단계에서 develop 대상 PR 검토·반영이다. 업무 요약은 C:\process-plan의 2026-10-11 일일업무에 연결한다.
