# 과제1 최종 완료조건 확인

## 목적·기준·판정 범위

사용자의 과제1 최종 확인·커밋·푸시·PR·리뷰·병합 요청에 따라 기초 통신 계층과 콘솔 시험 프로그램을 마감한다. 원문은 사용자 제공 「가상 AMR_관제 시스템 단계별 개발 과제 구성_v0.2.pdf」 2~3쪽, 요구사항 R-04와 COM-01~06이다.

담당 로컬 원본은 과제1 계획 v0.53 초안 7절, 기능명세 v0.13, 공통데이터명세 v0.12, 결정사항 v0.16 및 COM05-B/TASK1-V 초안이다. 초기 #53의 계획 v0.51 기재는 실제 v0.53으로 정정한다. `feature/53-communication-trial`, 기준 develop `4e462691db9dcc312d1d6b882773c5ff382eadc6` + 이번 변경의 소스/실행 DLL 해시는 각 실행 근거로 식별한다.

**기능 판정: 원문과 계획의 과제1 기초 통신 완료조건 충족.** 게시·리뷰·develop 반영은 연결된 PR의 실제 상태로 판단한다. 실제 AMR 명령 수행·Core 업무 추적·과제5 통합을 완료했다는 뜻이 아니다.

## COM-01~06 대조

| 기능 | 확인할 결과 | 코드·시험 근거 | 판정 |
|---|---|---|---|
| COM-01 송수신 | 장비·유형별 정상 모델과 명령을 구분해 전달 | MessageSubscription·AmrReceiveQueue·CommandPublisher, [기존 두 장비/네 유형](../../COM-01/20261019-multi-mqtt-03/README.md), [최종 두 장비의 명령/ACK](../../COM-06/20261031-task1-final-acceptance-02/ack-correlation.json) | 통과 |
| COM-02 Heartbeat | 한 장비의 신호 중단/복구를 독립 감시하고 서버 단절 때 이전 감시 종료 | SingleAmrHeartbeatMonitor·ControlCommunicationSession, [기존 두 장비 실연동/격리](../../COM-02/20261028-two-amr-domain-01/README.md), 최종 통신826 자동 및 최종 MQTT20 중 생존 신호/세션 복구 | 통과 |
| COM-03 접속 관리 | 접속·정상 종료·최초 실패·연결 후 단절을 구분 | MqttConnection·기존 감시/콘솔, [이전 A~C/종료 경합](../../COM-03/20261020-ac-summary-01/README.md), [최종 소진/종료](../../COM-06/20261031-task1-final-acceptance-02/verification.json) | 통과 |
| COM-04 입력 검사 | 비정상 입력 거부 후 정상 처리 지속 | Heartbeat/Telemetry/Alarm/Ack/CommandValidator, 통신826 자동, 최종 ts 누락/ACK cmdId 누락 거부 후 정상 ACK 전달 | 통과 |
| COM-05 명령/응답 전달 | 다중 요청과 역순 ACK의 대상·ID·원본 값 보존 | [실제 명령6개와 역순 응답](../../COM-06/20261031-task1-final-acceptance-02/ack-correlation.json), SUCCESS/FAILURE·선택 id/reason·ts 보존, 다른 대상은 요청 대조 불일치, 불량 ACK 뒤 처리 지속 | 통과 |
| COM-06 자동 복구 | 같은 콘솔 프로세스의 재접속·4유형 구독·재수신 및 명령 거부/재발행 금지 | ReconnectingCommunicationSession·새 --trial, [최종 실제20항목](../../COM-06/20261031-task1-final-acceptance-02/verification.json), 복구 후 실제 Simulator 재수신과 시험 Alarm/ACK 수신 | 통과 |

COM-02의 감시 본체·큐와 COM-01/03/04의 기존 처리 경로는 유지했다. 기존 세션에 추가한 발행 수명/정리 변경은 통신826 자동과 실제 재접속/종료로 확인했다. 기존 회귀의 Code/입력/예상/실제/한계는 각 고정 실행 기록을 따른다.

## 계획 7절의 다섯 완료조건

- [x] 정리된 절차로 콘솔을 실행한다. [실행 안내](../../../tests/manual/COM06/README.md), 실제 Git Bash 입력·status/exit 결과 확인.
- [x] 접속·송수신·Heartbeat·재접속·명령/응답·검증을 개별 시험한다. 위 COM 대조표와 최종 실제 MQTT20항목 참조.
- [x] 메시지·감시·복구 정책과 적용 범위를 기록한다. 기존 JSON/Topic·QoS0·Retain=false 유지, 5초/추가5회는 변경 가능한 시험 기본값, 준비 전 명령 거부·대기열 없음·자동 재전송 없음.
- [x] 비정상 입력 후 처리 지속과 장애 복구의 근거를 남긴다. 같은 수신 프로세스에서 오류 후 ACK 전달 및 같은 시험 콘솔의 서버 복구 후 재수신 확인.
- [x] 실제 결과·판정·미해결 범위를 기록한다. 코드/DLL·입력·명령·소유 자원·실패·한계와 PR 검토 근거를 연결한다.

## 최종 보완 시험

Windows 10.0.26200 / .NET SDK10.0.401 / MQTTnet5.2.0.1603 / Docker Linux 엔진24.0.6 / Mosquitto2.1.2-alpine. 실행 시각·별도 고정 포트·소유 컨테이너는 [환경](../../COM-06/20261031-task1-final-acceptance-02/session.json)과 [판정](../../COM-06/20261031-task1-final-acceptance-02/verification.json)을 따른다.

입력은 AMR_001/002의 MOVE·STOP·RESET 6요청, 원래 ID를 유지한 역순 ACK(SUCCESS/FAILURE와 id/reason 생략/포함), 다른 AMR에 보낸 기존 ID, cmdId 없는 불량 ACK와 이후 정상 ACK다. 대조용 사전은 시험 도구에만 있고 제품 Core 상태·이력·기한을 만들지 않는다.

최종 실제20항목 통과. 제품 C# 코드는 이전 통신826 통과 버전과 동일하며 이번에는 누락된 실제 시험 대역/근거만 추가했다. 이전 관련 회귀378건과 신규 실제 MQTT를 합쳐 한 번의 새 자동시험으로 표시하지 않는다. [전체 구현/자동시험 근거](../../COM-06/20261031-console-trial-01/README.md).

첫 최종 시험은 PowerShell 자동 변수 Matches와 시험용 목록 이름의 충돌로 중단했다. `ackMatches`로 바꾸고 새 실행 ID로 재실행했다. [첫 실패](../../COM-06/20261031-task1-final-acceptance-01/failure.log)와 원본 출력을 보존하며 제품 결함/통과로 집계하지 않는다.

## 과제2~5 및 추가 검증으로 넘기는 범위

- 실제 Simulator의 MOVE/STOP/RESET 수행과 장애·ACK 생성은 과제2 #50/#51.
- 원래 요청과 응답의 업무 상태·기한·중복·늦은 결과/이력 판단은 과제4, 전체 GUI/DB와 종단 간 시나리오는 과제5.
- 장시간·대규모·실장비 운용/전달 보장은 원문 기초 통신 시험의 고정 수치 요구가 아니며 이번 완료 판정에 추가하지 않는다. 미검증 상태를 유지한다.
- 네이티브 터미널의 색상/키보드 화면은 미검증이며 실제 CLI 표준 입출력·Git Bash 파이프·종료 검증과 구분한다.

## 리뷰 포인트

새 Client/세션 생성은 이전 구독/콜백 수명을 격리하기 위한 선택이다. Client 재사용은 할당을 줄일 여지가 있지만 이전 수명 관리를 더 요구하며 성능 차이를 측정한 것은 아니다. 취소 중 발행은 전달 불명으로 표시하고 자동 재전송하지 않는다. 준비 알림/단절 순서·입력 대기 중 종료·발행 작업/세션 정리·기존 경로 보존을 중점 검토한다. 자세한 대안·예상 리뷰 질문은 구현 근거 5절을 따른다.
