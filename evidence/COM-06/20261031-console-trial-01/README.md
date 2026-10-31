# 과제1 통합 콘솔·자동 재접속 구현과 검증 — Issue #53

## 1. 목적과 적용 기준

접속·송수신·메시지 검사·생존 신호·연결 끊김·재접속 결과를 한 콘솔에서 읽고 시험한다. 기존 수신/명령/단일 감시와 Simulator·GUI 모드는 유지한다. [Issue #53](https://github.com/inhhlee/amr-control-system-demo/issues/53), COM-01~06 / R-04. 사용자 2026-10-31 구현 요청을 따른다.

- 코드: `feature/53-communication-trial`, 기준 develop `4e462691db9dcc312d1d6b882773c5ff382eadc6` + 미커밋 변경. [최종 소스 해시](final-source.json), [최종 원본 대조](final-audit.json). 커밋·푸시·PR·병합은 수행하지 않았다.
- 읽은 로컬 원본(공유 URL 없음): 과제1 계획 v0.53, 기능명세 v0.13, 공통데이터명세 v0.12, 결정사항 v0.16의 D-08/10월22일 ACK 계약, COM06-P/A/B 초안. 초기 기록의 계획 v0.51 표기는 최종 대조에서 실제 v0.53으로 정정했다. 이번 결과는 담당 원본의 `console-trial-20261031` 후속으로 연결한다.
- 기존 Topic/JSON·QoS0·Retain=false·자동 명령 재전송 금지 계약 유지. PING/PONG·heartbeat_ack·battery 필드를 추가하지 않았다.
- 재접속 기본5초/추가5회는 이번 구현에서 선택한 **시험 기본값**이며 운영 확정 수치가 아니다. 실행 옵션으로 바꾼다. 최초 시도와 별도이며, 각 실패/정리 뒤 대기한다. 성공적인 구독 준비 뒤 다음 장애의 예산을 초기화한다.

## 2. 변경 내용과 소유 책임

| 대상 | 역할 |
|---|---|
| ReconnectingCommunicationSession / ReconnectOptions | 단일 접속 감독 루프. 실패한 세션 정리 후 새 Client/구독 생성, 간격·횟수·취소·이전 세션 알림 격리 |
| 기존 ControlCommunicationSession | 준비된 연결에서 단일 명령 발행, 세션 종료 시 진행 중 발행 대기 취소·반환 후 연결 정리 |
| TrialConsoleRunner / Options / Display | 새 `--trial` 분기, move/stop/reset/status/help/exit, 한글 상태·메시지 요약·선택적 정상 JSON 일부·색상·제어 문자 이스케이프 |
| TrialConsoleInput | 취소할 수 없는 Console.In의 별도 ReadLine 작업을 남기지 않고 Windows 콘솔 키/파일/파이프 가용량을 읽음. ASCII128자 한도 |
| tests/manual/COM06 | 별도 실제 Broker·Simulator·Git Bash·동적 콘솔 입력·단절/복구 시험. 소유 Broker만 조작 |

명령은 연결·구독 준비 전 거부하고 보관하지 않는다. 발행 중 단절은 이미 전송됐을 수 있어 전달/실행 불명을 보존하며 다시 발행하지 않는다. 새 Client는 생존 상태를 새로 감시한다. Broker 연결 복구와 AMR 생존 신호 재수신을 다른 이벤트로 표시한다.

준비/단절 알림은 하위 세션의 구독·단절 통지 경계에서 순서를 유지한다. 입력·발행·수신/감시·재시도 루프는 종료 시 취소하고 기다린다. 이전 세션 정리 실패나 수신/관찰 처리 오류는 재접속으로 숨기지 않고 종료한다.

## 3. 실제 시험 결과

| 종류 | 입력·예상 | 실제 결과·근거 |
|---|---|---|
| 솔루션 빌드 | 제품/기존 시험 빌드 | [build-final.log](build-final.log), 경고0/오류0. 그 뒤 새 감독자의 알림 순서 보완은 [통신 재빌드/시험](final-Communication-order.log)에서 확인 |
| 최종 통신 자동시험 | 기존805 + 새21. 접속/구독 실패·한도·취소·이전 콜백·발행 중 단절·재발행 없음·알림 순서·입력/출력 경계 | **826/826 통과**, [TRX](final-tests/Communication-order.trx), [로그](final-Communication-order.log) |
| 관련 기존 회귀 | Core9·GUI53·Simulator244·Map72 | **378 통과**. [프로젝트별 집계](final-audit.json), final-tests의 개별 TRX. 새 감독자 최종 알림 순서 보완 전 실행이며 해당 기존 경로 소스는 무변경. 모두를 한 번의 최종 전체 실행으로 표시하지 않음 |
| 최종 실제 MQTT/CLI | 정상 수신·검사·3명령·생존 신호·단절·자동 재접속/재구독·실제 재수신·단절 중 거부·재발행 없음·한도/종료·Git Bash | **15항목 통과**, [최종 실행/판정](../20261031-console-trial-live-03/verification.json), [콘솔 로그](../20261031-console-trial-live-03/trial.stdout.log), [실행 명령](../20261031-console-trial-live-03/commands.jsonl) |
| 실제 Simulator | 동일 콘솔 프로세스에서 서버 단절 전/후 두 번 실행 | 두 실행 모두 최종 `(1,2)`·IDLE·속도0 수신. [송신 전](../20261031-console-trial-live-03/simulator-before.stdout.log), [송신 후](../20261031-console-trial-live-03/simulator-after.stdout.log) |
| Git Bash | 프로필 없이 Bash에서 .NET 실행, 파이프의 status/exit 처리 | [실제 Bash 출력](../20261031-console-trial-live-03/git-bash.stdout.log), 정상 접속·Broker=Connected·ExitCode0 |
| 보존/정리 | 기존 파일/컨테이너 보존, 소유 시험 자원 정리 | 이전 결과 파일 해시 동일. 기존 컨테이너 ID 보존 및 start/stop/rm 미실행. 기존 amr-mosquitto의 독립적인 Restarting 변화는 전후 기록만 남기며 시험이 만든 상태 변화로 단정하지 않음 |

환경: Windows 10.0.26200, SDK10.0.401/net10.0, MQTTnet5.2.0.1603, Docker Linux 엔진24.0.6, Mosquitto2.1.2-alpine. 최종 실제 실행은 2026-10-31 23:21:50~23:22:23 KST, 별도 loopback 고정 포트63567이다. [실행 설정](../20261031-console-trial-live-03/session.json), [Docker 버전](../20261031-console-trial-live-03/docker-server.json), [제품/도구/DLL 해시](../20261031-console-trial-live-03/source-files.json). 실제 시험은 재시도1초/추가10회, 소진 시험은1초/추가2회, 취소 시험은30초 간격을 명시했다.

## 4. 실패와 보완 기록

- 첫 빌드에서 시험의 System.Buffers import와 xUnit 비동기 예외 오버로드를 수정했다. [build-01.log](build-01.log), 수정 후 빌드 로그 보존.
- 첫 전체 회귀의 새 수신 시험은 큐 접수 직후 연결을 끊고 소비 완료까지 기대해 1건 실패했다. 접수/소비를 구분하고 첫 Received 관측 후 단절하도록 동기화했다. 대기 제한을 늘리거나 제품의 취소 동작을 완화하지 않았다. [당시 전체 출력](full-tests.log), 최종826 통과와 구분한다.
- 첫 전체 TRX는 같은 파일명으로 프로젝트별 결과가 덮어써졌다. 원본 출력·마지막 Map TRX를 보존하고 이후 개별 파일로 분리했다. Map.trx는 그 마지막 원본의 복사이며 새 Map 재실행으로 합산하지 않는다.
- 초기 실제 live-01/02도 보존한다. live-03이 알림 순서 보완 후 최종 제품/도구 해시의 실제 검증이다. 이전 실행과 항목 수를 합산하지 않는다.
- 자체 검토에서 준비 표시와 단절 표시가 별도 스레드에서 역전될 수 있는 경계를 보완하고, 준비 알림을 제어한 상태에서 단절 알림이 앞지르지 않는 자동시험을 추가했다.

## 5. 선택 이유·대안·리뷰 질문

1. **왜 매번 새 Client/세션인가?** 기존 Client를 다시 연결하면 할당을 줄일 수 있지만 취소/구독/지연 콜백의 이전 수명을 관리해야 한다. 이번 시험 규모에서는 기존 소유 책임을 재사용해 정리 후 새 세션을 만드는 방식을 택했다. 메모리/성능 우열은 측정하지 않았다.
2. **연결 복구 때 왜 명령을 다시 보내지 않는가?** 단절 직전 이미 전달됐을 수 있어 중복 실행 위험이 있다. 이번 계약은 거부·전달 불명·자동 재전송 없음이다. 저장/멱등 처리/보존 정책을 별도로 정하기 전 송신 큐를 추가하지 않는다.
3. **ACK SUCCESS가 왜 자동으로 명령 완료가 아닌가?** 이 콘솔은 응답 원문 값과 대상/ID를 관찰한다. 실제 Core의 등록 요청과 대조·기한/중복/이력 판단은 구현하지 않았다. 현재 Simulator의 실제 MOVE/STOP/RESET 수행도 별도 Issue #51이다.

입력의 동기 대기를 백그라운드 Task로 감추는 대안 대신 가용량을 검사한다. 이 방식은 Windows 시험 환경과 하나의 입력 소유자를 전제로 한다. 비Windows 리다이렉트 입력·복잡한 편집/히스토리 요구가 생기면 지원 범위를 다시 설계한다. API 근거와 조건은 [실행 안내](../../../tests/manual/COM06/README.md)에 연결했다.

## 6. 결과와 남은 범위

2026-10-31 후속 마감 요청으로 [과제1 최종 완료조건](../../task1/20261031-final/README.md)을 COM-01~06에 대조했다. 누락된 두 장비/6요청의 역순 ACK 실제 MQTT 시험을 시험 대역으로 보완했고 [최종20조건](../20261031-task1-final-acceptance-02/verification.json)이 통과했다. 이 후속 판정은 아래 앞선 로컬 구현 단계의 전체 완료 보류 기록을 대체하며 과제2~5 범위는 계속 구분한다.

현재 요청한 통합 콘솔과 자동 재접속·실제 수신 복구를 로컬에서 구현·검증했다. 실행은 [Git Bash 안내](../../../tests/manual/COM06/README.md)를 따른다. 기본 명령은 `dotnet run --project src/Amr.Communication.Console -- --trial --show-payload`다.

과제1 전체·과제5 통합 완료를 뜻하지 않는다. 실제 AMR 동작/Alarm 생성·Core 요청-ACK 추적·DB·장시간/대량 운용과 전달 보장은 남아 있다. 네이티브 터미널의 색상/키보드 Ctrl+C 화면 캡처는 이번 실제 시험에 포함하지 않았다. 파이프 입력·exit·취소 토큰·한도 소진 중 종료는 구분해 확인했다. Issue는 열어 두며 커밋·푸시·PR·병합은 요청 전 수행하지 않는다.
