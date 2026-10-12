# COM-01 Heartbeat 구독·수신 검증 — 20261012-heartbeat-05

## 1. 목표와 적용 기준

- 대상: [Issue #15](https://github.com/inhhlee/amr-control-system-demo/issues/15), COM01-A 한 종류 수신만 구현·시험. 실행자: Codex.
- 원본: `C:\process-plan\관제시스템 과제 진행\명세\`의 기능명세 v0.7 COM-01, 요구사항정의 v0.5 R-04, 공통규칙 v0.4, 공통데이터명세 v0.4 / COM-01-Heartbeat-v0.2, 검토 및 결정사항 v0.5 D-03·04.
- 현재 과제1 과제계획 v0.20, 전체일정 v0.14, COM01-A v0.1. 전달 초안의 과제계획 v0.14·전체일정 v0.11과 차이를 Issue에 기록했다. 현재 자료의 과거 실행 상태보다 실제 저장소·이번 실행 결과를 우선한다.
- 개발환경 원본 v0.7은 과거 `C:\practice` 설명이 있어 현재 저장소의 SDK·프로젝트·실행 위치로 대조했다. 기존 소스·통과 기록은 복사하지 않았다. 새 기능 추적기 없음, spec_revision 해당 없음.
- 사전 확인: 기존 `feature/COM-01-heartbeat-subscribe`를 재사용. 시작 작업 트리 깨끗함. HEAD `746c173d31ff9744e60a7e3be29fa253c98482c2`, 최신 origin/develop `98162c1c5c85c476260e26d96f0ad4ae98570916` 포함. 시작 HEAD의 기존 커밋은 이번 작업에서 만들지 않았다. CONTRIBUTING·Git Flow 설정을 변경하지 않았다.
- 구현 전 입력/예상: 정상 Topic `amr/AMR_001/heartbeat`·49바이트 JSON → 구독 승인 후 시각·원문 진단. 다른 Topic → 진단 없음. Ctrl+C → 구독 해제·연결 해제·종료 0, 새 실행 → 새 ID로 다시 수신.

## 2. 실행 환경과 코드

| 항목 | 실제 값·근거 |
| --- | --- |
| 코드·설정·버전 | [source-version.json](source-version.json), [environment.json](environment.json). 기준 HEAD + 미커밋 소스·시험 5개. 이 기록은 커밋 완료를 뜻하지 않음 |
| 실행 위치 | `C:\amr-control-system-demo` |
| SDK / 라이브러리 | .NET SDK 10.0.401 / MQTTnet 5.2.0.1603 / net10.0 |
| Broker | 프로젝트 전용 Mosquitto 2.1.2, Windows `127.0.0.1:1884` → 컨테이너 1883. 기존 실행 서비스를 사용, 재시작·설정 변경 없음 |
| MQTT | MQTT 5, QoS 0, 시험 발행 Retain=false, Clean Start, Session Expiry 0, 실행별 GUID Client ID |
| 구독·진단 | `amr/+/heartbeat` 하나. SUBACK GrantedQoS0 확인. 원문 기본 숨김, `--show-payload`에서 최대 256바이트. 수신 UTC와 발행 ts 구분 |
| 실행 일시 | 2026-10-12 12:36:32~12:36:40 KST, 원본 로그는 UTC/명시적 offset. [verification.json](verification.json) |
| 자동시험 | 최종 제품 소스는 04 빌드 이후 동일. [빌드](../20261012-heartbeat-04/build.log), [시험 로그](../20261012-heartbeat-04/test.log), [TRX](../20261012-heartbeat-04/tests.trx), [종료 코드](../20261012-heartbeat-04/build-test.json) |

## 3. 입력·명령·예상 결과

루트 [실행 안내](../../../README.md#heartbeat-run)를 따른다. 이번 실제 시험은 `pwsh -NoProfile -File evidence/COM-01/20261012-heartbeat-05/run-mqtt.ps1`로 실행했고 종료 코드는 0이다. 각 시험 제어기를 별도 PowerShell 프로세스로 실행하여 자신이 만든 숨김 콘솔에만 Windows CTRL_C_EVENT를 보냈다. 동일 콘솔의 프로세스 목록을 검사한 뒤 신호를 전달했다.

- [run-mqtt.ps1](run-mqtt.ps1): 두 독립 실행·결과·Broker 로그 확인.
- [verify-mqtt.ps1](verify-mqtt.ps1): 각 콘솔의 SUBACK 대기 → Mosquitto 관찰자 준비 → 발행 → 수신 확인 → Ctrl+C → 종료 코드 기록. 관찰자는 출력 버퍼링을 피하도록 Docker exec TTY를 사용한다.
- 실제 발행 명령·Client ID·시각·입력·종료 코드: [publications-1.json](publications-1.json), [publications-2.json](publications-2.json). 컨테이너 내부 `mosquitto_pub`, MQTT 5·QoS 0, `-r` 미지정.
- 첫 실행은 정상 2건 사이에 비구독 Topic 3건을 발행. 둘째 실행은 정상 1건. 고정 ts 1791195767000/001/002는 입력 식별용이며 최신 생존 판정이 아니다.

## 4. 완료조건별 실제 결과·브라우저 확인

브라우저에서 Ctrl+O → 이 폴더의 `report.html`을 선택한다. 각 번호의 표와 펼침 로그를 확인한다. 제품 GUI나 실시간 감시 페이지를 구현한 것은 아니다.

| 완료조건 | 실제 결과·판정 | 직접 근거·브라우저에서 볼 항목 |
| --- | --- | --- |
| 1. 구독 성공 뒤 시험 메시지 수신 | 통과. SUBACK 확인 후 Heartbeat Topic·UTC 수신 시각·발행 원문 일치. 검사 결과·생존 판정 출력 없음 | 보고서 1번, [첫 콘솔](console-1.stdout.log), [발행 1](publications-1.json) |
| 2. 비구독 Topic 전달 안 됨 | 통과(관찰한 입력·시간 범위). Mosquitto 관찰자는 5건을 받았고 C# 콘솔은 그중 정상 Heartbeat 2건만 받음. telemetry·추가 suffix·추가 중간 level 3건 제외, 이후 정상 수신 지속 | 보고서 2번, [관찰자](observer-1.stdout.log), [첫 결과](mqtt-result-1.json). 추가 7개 Topic 경계는 자동시험 |
| 3. Ctrl+C 정리·새 실행 수신 | 통과. 두 콘솔에서 구독 해제 응답 확인 → 연결 해제 → Dispose → 실제 Process.ExitCode 0. ID 서로 다르고 둘째 세션 정상 수신 1건. Broker 정상 해제 기록 대응 | 보고서 3번, [두 실행 결과](verification.json), [둘째 콘솔](console-2.stdout.log), [Broker 로그](broker.log). 이벤트 처리기 해제는 자동시험으로 확인 |
| 4. README·Issue·PR에 결과 연결 | 부분 완료. 루트 README↔이 결과↔Issue #15 연결 완료. 변경을 원격에 게시하고 Issue·PR에서 접근 가능한 근거로 연결하는 단계는 미수행 | 보고서 4번. 사용자 요청이 구현·검증 후 정지·미커밋이므로 커밋·푸시·PR 생성·Issue 체크/종료는 남김 |

자동시험은 기존 정상 수명주기 6개와 새 구독 경계 12개, 총 18개 통과·실패 0·건너뜀 0이다. 구독 승인 전 출력 차단, 승인 거부, Topic 경계, 구독 해제 대기·반복 정리·실패 후 이벤트 해제, 기본 원문 숨김, 10,000바이트 입력의 256바이트 진단 제한을 포함한다. Fake 시험을 실제 Broker 시험으로 집계하지 않는다.

## 5. 실패 이력·미검증·범위

- [01](../20261012-heartbeat-01/README.md): 시험 코드 API 오류로 첫 솔루션 빌드 실패. 수정 뒤 02·04 빌드/자동시험 통과.
- [02](../20261012-heartbeat-02/harness-attempt.md): 시험 제어기의 실행 중 로그 파일 공유 오류. 실제 MQTT 검증 실패 보존.
- [03](../20261012-heartbeat-03/README.md): 관찰자 출력 버퍼링으로 SUBACK 대기 실패. 발행 전 중단.
- [04](../20261012-heartbeat-04/README.md): 첫 수신·필터·Ctrl+C는 통과했으나 둘째 실행 시험 제어기 실패. 최종 두 실행 판정은 이 05 기록을 사용.
- 사람이 키보드로 직접 누른 Ctrl+C와 사용자 터미널의 `$LASTEXITCODE`는 이번 자동 실행에서 미수집. 실제 Windows CTRL_C_EVENT 및 독립 Process.ExitCode로 검증했다.
- 브라우저 자동 표시 확인은 CUA 런타임의 Windows sandbox setup refresh 오류로 두 번 실패했다. HTML 생성·링크 검사는 수행했으며 화면 배치의 실제 브라우저 확인은 미검증이다. 브라우저 Ctrl+O로 report.html을 열어 확인할 수 있다.
- Broker 내부 세션 DB 직접 검사는 미실행. 정상 해제·Session Expiry 0·이전 프로세스 종료·새 ID 수신을 이번 정상 수명주기 근거로 사용한다.
- 구독/해제 대기 3초는 무한 대기를 피하는 실습 제한이다. 비정상 단절·시간 경계·종료 경합·자동 재접속은 후속 Issue 범위다.
- 진단 옵션은 비밀정보 없는 통제된 시험 입력에만 사용한다. 비밀정보 자동 탐지·마스킹을 제공하지 않는다. 기본 원문 숨김과 길이 제한을 적용했다.
- Telemetry·Alarm·ACK 처리, JSON 검사기·정상 모델 전달, Heartbeat Timeout·생존 갱신, 자동 재접속은 미구현·이번 제외 범위다. 해당 Topic 발행은 구독 경계 시험일 뿐 해당 기능 구현이 아니다.

## 6. 종합 결과

한 종류 수신의 로컬 구현·자동시험·실제 MQTT 완료조건 1~3은 통과했다. 조건 4의 원격 게시·PR 연결은 요청된 후속 단계에 남긴다. COM-01 전체·과제1 전체 완료로 표시하지 않는다. 사용자 검토용 결과를 남기고 커밋 없이 정지한다.

[브라우저 보기본](report.html) · [같은 날짜 업무 기록](<C:/process-plan/일일업무/2026-10-12.md>)
