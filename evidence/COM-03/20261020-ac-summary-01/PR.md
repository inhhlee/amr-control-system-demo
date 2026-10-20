## 관련 Issue

- 관련 Issue: #6
- 대상 브랜치(base): develop
- 과제·기능 ID: 과제1 COM-03 / COM03-C1~C4
- 명세·계획 근거: 기능명세 v0.7 COM-03, 요구사항정의 v0.5 R-04, 공통규칙 v0.4 C-09·C-10, 결정사항 v0.5 D-08·D-09, 과제계획 v0.47 및 C 안내 v0.11. 원본 공유 URL은 없어 문서명·버전·ID로 식별한다.

## 변경 내용

접속 후 연결을 잃으면 UTC 시각·MQTT 원인 코드·안전한 예외 유형·Client ID를 기록하고 정리 후 종료 1을 반환한다. 먼저 접수한 사용자 취소는 종료 0을 유지한다. 중복 알림과 종료 후 도착한 콜백이 최초 원인이나 종료 결과를 바꾸지 않는지 검증했다.

- ConsoleRunner의 단절 진단과 종료 이유 출력, 안전 진단 시험 3건·C4 순서 제어 시험 7건을 추가한다.
- 기존 Docker·Mosquitto를 사용하는 격리 시험 도구와 최소 TCP 응답 차단 프록시를 추가한다. 정상 승인 후 Broker→Client 응답만 폐기하고 반대 방향과 TCP 연결을 유지한다.
- A~C 정상·최초 접속 실패/제한/취소·KeepAlive·연결 후 단절의 근거를 최종 코드에 연결한다. 기존 실패·중간 실행 기록도 보존한다.

## 실제 시험 결과

- 검토 코드: [bccb461](https://github.com/inhhlee/amr-control-system-demo/commit/bccb461554f14dd5b7456c07f4dec4d9ea9a3a99). 최종 C4 소스 해시 72개와 게시 커밋의 내용이 대응한다.
- 실행 일시·환경: 2026-10-20 KST, Windows 10.0.26200, SDK 10.0.401, MQTTnet 5.2.0.1603, xUnit 2.9.3. 실제 연동은 기존 이미지 Mosquitto 2.1.2의 격리 Broker·루프백 임시 포트다.
- 실행 위치: amr-control-system-demo. [A~C 최종 근거·완료조건표](https://github.com/inhhlee/amr-control-system-demo/blob/feature/6-mqtt-keepalive/evidence/COM-03/20261020-ac-summary-01/README.md), [최종 시험 소스](https://github.com/inhhlee/amr-control-system-demo/blob/bccb461554f14dd5b7456c07f4dec4d9ea9a3a99/evidence/COM-03/20261020-c4-unit-02/source-version.json), [기존 시험본/바이너리 대조](https://github.com/inhhlee/amr-control-system-demo/blob/bccb461554f14dd5b7456c07f4dec4d9ea9a3a99/evidence/COM-03/20261020-c4-unit-02/source-comparison.json).
- 아래 C1~C3b는 앞서 실제 실행한 기록이며 이번 게시 단계에서 반복 실행하지 않았다. C4는 대역 알림·사용자 취소의 순서를 제어하는 자동시험이다.

| 구분 | 명령·입력·조건 | 예상 결과 | 실제 결과 | 판정 | 근거 |
| --- | --- | --- | --- | --- | --- |
| 빌드·자동시험 | dotnet build/test --no-restore, test --no-build | 경고/오류 0·회귀 통과 | 기존 761+신규 7=768건, 실패/건너뜀 0·경고/오류 0 | 통과 | [명령](https://github.com/inhhlee/amr-control-system-demo/blob/bccb461554f14dd5b7456c07f4dec4d9ea9a3a99/evidence/COM-03/20261020-c4-unit-02/commands.jsonl), [TRX](https://github.com/inhhlee/amr-control-system-demo/blob/bccb461554f14dd5b7456c07f4dec4d9ea9a3a99/evidence/COM-03/20261020-c4-unit-02/tests.trx) |
| C1 실제 유지 | KeepAlive 5초, 업무 PUBLISH 없음·2배 이상 관찰 | PING 유지·Ctrl+C 종료 0 | 12.0899808초·PING 2쌍·종료 0 | 통과 | [C1](https://github.com/inhhlee/amr-control-system-demo/blob/bccb461554f14dd5b7456c07f4dec4d9ea9a3a99/evidence/COM-03/20261020-c1-keepalive-02/README.md) |
| C2 실제 Broker 종료·새 실행 | 격리 Broker 종료, 동일 Broker 재기동 뒤 새 프로세스 | 범위 내 상실·정리·종료 1, 새 실행 정상 | 감지 53.4907ms·종료 관찰 79.4166ms, 새 PID/ID·12초/PING·종료 0 | 통과 | [C2](https://github.com/inhhlee/amr-control-system-demo/blob/bccb461554f14dd5b7456c07f4dec4d9ea9a3a99/evidence/COM-03/20261020-c2-mqtt-01/README.md) |
| C3a 전달/차단·정리 | 승인 뒤 응답 폐기·TCP 유지·재개, 접속 전 stop | 차단 조건·시험 자원 정리 | 응답 2바이트 폐기·반대 전달·연결 유지, 최신 도구 회귀 종료 0 | 통과 | [C3a](https://github.com/inhhlee/amr-control-system-demo/blob/bccb461554f14dd5b7456c07f4dec4d9ea9a3a99/evidence/COM-03/20261020-c3a-proxy-03/README.md), [회귀](https://github.com/inhhlee/amr-control-system-demo/blob/bccb461554f14dd5b7456c07f4dec4d9ea9a3a99/evidence/COM-03/20261020-c3b-c3a-regression-01/README.md) |
| C3b 실제 무응답 | 차단 대기0초/2초, 사전 기준·허용 오차 저장 | 위상 반영 범위 내 감지·종료 1 | 차단→감지7.3297821/5.3980114초, 마지막 송신→감지7.5744957/7.6373481초(허용7.4~8.75), 폐기→감지2.5027692/2.5050294초(허용2.4~3.5) | 통과 | [C3b](https://github.com/inhhlee/amr-control-system-demo/blob/bccb461554f14dd5b7456c07f4dec4d9ea9a3a99/evidence/COM-03/20261020-c3b-noresponse-02/README.md) |
| C4 순서·중복·늦은 콜백 | 첫 알림 보류 중 중복8회/취소, 정상 해제 중 알림, Dispose 뒤 캡처 콜백 | 최초 원인/코드·단일 정리 유지 | 신규7건 포함 종료 원인21건·관련46건 통과. UserCanceled/0·ConnectionLost/1, 정리 각1회·구독0 | 통과 | [C4](https://github.com/inhhlee/amr-control-system-demo/blob/bccb461554f14dd5b7456c07f4dec4d9ea9a3a99/evidence/COM-03/20261020-c4-unit-02/README.md) |

## 완료조건

- [x] 무메시지 상태에서 적용 KeepAlive 2배 이상 실제 연결 유지와 PING 또는 이에 대응하는 프로토콜 진단 근거를 확인한다.
- [x] 실제 격리 Broker 정상 종료 후 사전에 정한 관찰 범위 안에 연결 상실·이유·시각이 기록된다.
- [x] 접속 후 응답 전달이 중단된 조건에서 감지 기준·허용 오차·실제 감지 시간을 비교할 수 있다.
- [x] 같은 단절의 중복 통지와 명시적 종료를 구분하며 늦은 알림이 종료 상태를 되돌리지 않는다.
- [x] Ctrl+C와 단절 경합에서도 자원 정리·종료가 가능하고 종료 이유와 코드가 일치한다.
- [x] Broker 복구 후 새 실행의 접속을 확인하고 자동 재접속 결과로 표시하지 않는다.
- [x] A~C의 유효한 정상·실패·대기 제한·취소·유지·단절 증거와 필요한 회귀 결과를 검토할 코드 버전에 연결한다.
- [x] 자동 시험·실제 Broker·프록시 조건의 적용 범위와 미실행·실패·감지 한계를 표시하고 README·Issue·PR·증거를 연결한다.

8개 완료조건의 개별 근거와 선행 #4/#5 조건 유효성은 최종 근거표에 있다. [#6 Issue의 최종 검증·PR 게시](https://github.com/inhhlee/amr-control-system-demo/issues/6)에서 실제 PR·고정 코드 증거를 연결하고 8개 조건을 갱신했다. Issue는 open, PR은 병합 대기로 유지한다.

## 미검증 항목과 남은 작업

| 항목 | 사유·영향 | 후속 확인 |
| --- | --- | --- |
| 리뷰·develop 병합·Issue 종료 | 이번 요청은 커밋·푸시·PR·Issue 근거 갱신까지다 | 병합은 보류. 이후 요청된 단계에서 리뷰·반영·완료 확인 |
| 실제 OS Ctrl+C와 Broker 동시 경합 전수 | C4는 동일 취소 토큰·대역 콜백 순서 제어이며 실제 Ctrl+C/단절은 기존 별도 세션 | 검증 범위를 넘어 모든 스케줄 보장으로 확대하지 않음 |
| 실제 인증 Broker 거부·Seq 새 필드 수집·장시간/부하/시각 변경 | 이번 실제 MQTT 통과 범위 밖 | 필요 시 별도 계획·입력으로 검증 |
| 자동 재접속·재구독, AMR Heartbeat·GUI·Core·DB | COM-06·COM-02 등 후속 범위 | 별도 Issue·계획 |

초기 환경/도구 실패와 중간 결과는 [실패 이력](https://github.com/inhhlee/amr-control-system-demo/blob/feature/6-mqtt-keepalive/evidence/COM-03/20261020-ac-summary-01/README.md#5-실패-이력한계남은-단계)에 연결했다. MQTT 이유 코드 NormalDisconnection도 최초 원인·타임아웃 예외·실제 종료 코드와 함께 해석한다. C3b 감지 허용 범위는 시험 기준이며 운영 SLA가 아니다.
