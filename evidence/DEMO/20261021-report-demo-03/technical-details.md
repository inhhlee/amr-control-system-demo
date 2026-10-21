# 책임님 보고용 AMR 시연 — 실제 검증결과

- 실행: 2026-10-21 19:58:27 ~ 19:59:47 KST / 실행 ID: 20261021-report-demo-03
- 기준 코드: 71160f412624f829f8b5673c409da5a4accf202c, 브랜치: feature/33-simulator-movement. 제품 수정 없음; 시험 도구와 실행 증거만 추가·보완.
- 환경: Microsoft Windows 10.0.26200, .NET SDK 10.0.401, PowerShell 7.6.5, Docker 29.7.2 / 29.7.2, Mosquitto 2.1.2, MQTTnet 5.2.0.1603.
- 적용 기준: 기능명세 v0.9 COM-01·03·04·05, 요구사항정의 v0.5 R-04, 공통규칙 v0.6 C-09·10, 결정사항 v0.9 D-08·09·12 및 현재 코드·기존 시험 입력. 기존 COM03/COM05 실행 근거의 당시 계약·시험 기준을 유지하며 새 운영 정책은 추가하지 않았다.
- 관련 문서 원본: C:\process-plan. 공유 URL이 없으므로 문서명·버전·항목으로 식별한다. spec_revision/verification_index 별도 파일은 없으며 Git 커밋·소스 해시·실행 ID로 추적한다.

## 실제 실행 순서와 판정

| 순서 | 입력·예상 | 실제 결과 | 판정·원본 근거 |
| --- | --- | --- | --- |
| 01 빌드·자동시험 | 전용 두 프로젝트 빌드 및 전체 자동시험 통과 | Communication 768건, Simulator 129건. 빌드 경고·오류 0 | [통과](../20261021-report-demo-01/automatic/Communication/Communication.trx) |
| 02 메시지 수신·검사 | 두 AMR·네 유형 구분, 불량 입력 거부 후 정상 수신·정상 종료 | 두 세션·총 28입력: 수신 20, 거부 4, 구독 외 4. 두 종료 코드 0 | [통과](../../COM-01/20261021-report-demo-03-msg/verification.json) |
| 03 명령 발행 | MOVE·STOP·RESET 수신, 잘못된 입력 거부, 잔존 명령 없음 | 3명령의 ID·Topic·좌표·QoS 0·Retain=false 확인. NaN 종료 1·추가 발행 0·잔존 0 | [통과](../../COM-05/20261021-report-demo-03-cmd/verification.json) |
| 04 연결 유지 | K=5초의 두 배 이상 무메시지 유지·PING·Ctrl+C 종료 0 | 12.101초, PING 2쌍, 업무 PUBLISH 0·종료 0 | [통과](../../COM-03/20261021-report-demo-03-c1/verification.json) |
| 05 Broker 종료·새 접속 | 단절 1회·정리·종료 1, 재기동 뒤 새 PID/Client ID로 접속 | 종료 요청→감지 58.70ms, 종료 관찰 85.55ms. 새 세션 PING 2쌍·종료 0 | [통과](../../COM-03/20261021-report-demo-03-c2/shutdown.json) |
| 06 응답 차단 | TCP를 유지한 채 응답 폐기, 사전 시간 범위 내 감지·자율 종료 1 | 마지막 송신→감지 7.626초(기준 7.4~8.75), 첫 응답 폐기→감지 2.507초(기준 2.4~3.5). 종료 1 | [통과](../../COM-03/20261021-report-demo-03-c3/measurement.json) |
| 07 Simulator 실제 콘솔 | 정상·변경 설정 초기화, 잘못된 설정은 생성 전 거부 | 15개 별도 프로세스 통과. 정상 IDLE·속도 0·종료 0, 오류 종료 1 | [통과](../../AMR/20261021-report-demo-03-sim/verification.json) |

자동시험은 20261021-report-demo-01 실행에서 직접 수행했다. 최종 실행에서는 제품·자동시험·설정·프로젝트 파일 해시 일치와 전건 통과 TRX를 확인해 재사용했다. 재실행하거나 다른 종류 시험과 합산한 수치가 아니다. Simulator 129건은 기존 88건과 이동 계산 41건이며, 실제 시간에 따른 이동 시연을 대신하지 않는다.

## 책임님께 설명할 내용

통신부의 메시지 수신·검사, 명령 발행, 연결 유지·단절·무응답 감지를 실제 Broker에서 확인했습니다. Broker 종료 시 원인·시각을 기록하고 정리한 뒤 종료하며, 재기동 후 새 실행의 접속도 확인했습니다. 가상 AMR은 설정·초기화와 이동 계산까지 검증했고 실제 실행 루프와 MQTT 송신 연결은 다음 범위입니다.

## 로그를 보여주는 위치

- [수신·거부 콘솔](../../COM-01/20261021-report-demo-03-msg/console-0.stdout.log), [재수신 세션](../../COM-01/20261021-report-demo-03-msg/console-1.stdout.log), [원본 입력](../../COM-01/20261021-report-demo-03-msg/inputs.json)
- [명령의 실제 구독 결과](../../COM-05/20261021-report-demo-03-cmd/subscriber.stdout.log), [MOVE 발행 출력](../../COM-05/20261021-report-demo-03-cmd/MOVE.stdout.log)
- [KeepAlive PING](../../COM-03/20261021-report-demo-03-c1/ping.log), [정상 종료](../../COM-03/20261021-report-demo-03-c1/console.stdout.log)
- [단절·정리 콘솔](../../COM-03/20261021-report-demo-03-c2/lost.stdout.log), [재기동·새 접속](../../COM-03/20261021-report-demo-03-c2/restart.json)
- [무응답 콘솔](../../COM-03/20261021-report-demo-03-c3/console.stdout.log), [사전 기준](../../COM-03/20261021-report-demo-03-c3/criteria.json), [프록시 원본](../../COM-03/20261021-report-demo-03-c3/proxy.jsonl)
- [상위 명령·시각·종료 코드](commands.jsonl), [실행 소스 해시](source-version.json), [전체 판정](verification.json), [저장된 로그 보기](report.html)

## 자원·기존 변경 보존과 실패 이력

- [최종 정리](cleanup.json): 기존 컨테이너 3개와 볼륨 목록을 보존했다. 최종 시연 Broker 4개·그 익명 볼륨을 제거했고 볼륨 수는 88→88로 동일하다. 프록시와 시험 콘솔은 각 실행 기록에서 종료를 확인했다.
- [기존 변경 대조](preservation.json): 기존 변경 파일 26개 원시 바이트, Git 인덱스·HEAD·브랜치를 보존했다. 커밋·푸시·브랜치 전환은 수행하지 않았다.
- 1차 실행: 자동시험·수신·명령 및 KeepAlive 동작 확인 후 Docker의 잘린 목록을 JSON으로 읽는 도구 오류로 전체 실패. --no-trunc로 수정했다. 2차 실행은 7단계 기능 통과 후 익명 볼륨 정리를 보완했다. 두 기록을 삭제하지 않았다.
- [앞선 시도 볼륨 정리](../20261021-report-demo-01/earlier-volume-cleanup/README.md): 소유권이 확인된 12개만 별도 정리했다. 이전 작업에서 존재하던 다른 볼륨은 변경하지 않았다. 사전 점검 중단과 보완 기록도 보존했다.

## 남은 기능과 이번 검증의 한계

- COM-02: 개별 AMR Heartbeat 중단·복구 감시.
- COM05-B: 송신한 요청과 수신 ACK의 ID·대상 대조 및 후속 처리.
- COM-06: 자동 재접속·재구독. 이번 복구는 새 콘솔의 명시적 접속이다.
- #34: Simulator의 실제 시간에 따른 이동 루프·이동 로그·이동 중 정상 종료.
- Simulator의 Heartbeat·Telemetry MQTT 송신과 기존 통신부의 실제 연결.
- 후속 GUI·Core·DB 및 전체 통합 검증. 이번 결과로 과제1·2 전체를 완료 처리하지 않는다.
- 실제 Seq 수집·MQTT Explorer GUI·물리 AMR·운영 부하·모든 동시 경합은 이번에 시험하지 않았다. 기존 Seq를 시작하지 않았으며 로그는 stdout/stderr와 Broker 원본으로 확인했다.
- ACK 수신 검사는 요청 대조와 다르다. 명령의 별도 구독자 수신은 실제 AMR 동작 완료를 보장하지 않는다. MQTTnet 사유 NormalDisconnection도 발생 조건·타임아웃 예외·종료 코드와 함께 읽는다. 로컬 감지 시간은 운영 SLA가 아니다.

## 실행 방법·도구 선택·예상 질문

[재실행 순서](../../../tests/manual/DEMO/README.md)를 따른다. 시연은 기존 Controller와 입력을 재사용하고 Broker 주소만 매개변수로 받는다. 컨트롤러를 복사하는 대안보다 검증 로직 중복이 적다. 별도 Compose 프로젝트를 고정하는 대안도 가능하지만 현재 도구는 루프백 동적 포트와 소유 ID로 자원을 구분한다. 성능 비교 시험은 하지 않았다.

1. 자동시험만 통과한 것인가? — MQTT 수신·명령·PING·실제 Broker 종료·프록시 응답 차단과 Simulator 별도 프로세스를 실행했다. 자동시험 TRX는 별도 근거다.
2. 재기동하면 자동 복구되는가? — 이번에는 새 PID·Client ID로 명시적으로 접속했다. 기존 실행의 자동 재접속·재구독은 COM-06 후속이다.
3. 화면에서 AMR이 움직이는가? — 현재 콘솔은 초기화 후 종료한다. 이동 계산 41건은 시간 값을 직접 주입한 자동시험이며 실시간 실행 루프·GUI는 후속이다.
