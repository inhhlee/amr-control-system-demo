# COM-03 A~C 최종 근거 취합과 #6 완료조건

후속 상태: [PR #31 P2 수정·재현·회귀](../20261020-pr31-p2-fix-01/README.md)에서 검증 도구 두 결함을 수정하고 새 30건·실제 C1/C2/C3a/C3b를 확인했다. 제품/xUnit 소스는 동일하며 아래 768건과 A~B 근거를 재사용한다. 원래 완료조건 8개는 유지하고 재검토·병합·종료는 대기한다. 아래 C4 취합·게시 시점의 코드 버전과 수치는 과거 이력이다.

2026-10-20 Codex 확인. **C1~C4 구현·시험과 후속 게시 요청을 마쳐 #6 원래 완료조건 8개 모두에 충족 근거를 연결했다.** 구현·시험·증거 커밋은 `bccb461554f14dd5b7456c07f4dec4d9ea9a3a99`, [PR #31](https://github.com/inhhlee/amr-control-system-demo/pull/31)은 develop 대상이다. [#6 Issue](https://github.com/inhhlee/amr-control-system-demo/issues/6)의 8개 체크와 근거·PR 연결을 갱신했다. **Issue·PR은 open, 병합·Issue 종료는 대기**다. [게시 시 코드/상태 대조](../20261020-pr-publication-01/README.md)에서 기존 검증 시점의 미커밋·7/8 상태와 구분한다.

## 1. 적용 기준·최종 코드

- [Issue #4](https://github.com/inhhlee/amr-control-system-demo/issues/4)·[#5](https://github.com/inhhlee/amr-control-system-demo/issues/5)·[#6](https://github.com/inhhlee/amr-control-system-demo/issues/6)의 원래 완료조건을 유지한다. [원격 조회 스냅샷](remote-snapshot.json)은 #4/#5 closed·completed, PR #14/#25 develop merged, #6 open·동일 브랜치 PR 검색 0건을 기록한다.
- 원본 문서는 C:\process-plan의 기능명세 v0.7 COM-03, 요구사항정의 v0.5 R-04, 공통규칙 v0.4 C-09·C-10, 결정사항 v0.5 D-08·D-09이다. Issue가 인용한 과제계획 v0.11은 최초 범위이며 착수 v0.46/C 안내 v0.10, 결과 반영 v0.47/v0.11과 구분한다. 공유 문서 URL은 확인되지 않아 이름·버전·ID로 식별한다.
- 검토 대상: `feature/6-mqtt-keepalive`, HEAD `d777ab7d8ef034d66a862343231268cd1476af24` + 미커밋 C1~C4 코드·시험·도구·문서. C4는 시험 두 파일만 변경했다. [소스 72개 SHA-256](../20261020-c4-unit-02/source-version.json)으로 시험 버전을 고정하며 README만 바뀌었다고 전체 제품 동일성을 추정하지 않았다.
- [A/B 원본·병합·현재 대조](ab-code-validity.json): A3 ZIP/12개 내부 해시 및 PR #13 병합 내용 일치. B 53개 시험 소스는 PR #25 병합 내용과 UTF-8 BOM/CRLF/LF 차이를 정규화하면 모두 대응한다. 선행 A1/A3/총괄/B 병합 커밋은 현재 HEAD의 조상이다.
- [C 단계 소스/바이너리 대조](../20261020-c4-unit-02/source-comparison.json): C2/C3b 이후 제품 소스 차이 0, 최종 C3b 및 C3a 회귀와 다른 파일은 시험 2개다. C3b 콘솔 EXE/DLL·통신 DLL·MQTTnet DLL 4개 모두 현재와 바이트 동일. C3a 과거 도구와의 차이는 C3b 확장이며 최종 도구의 회귀 결과를 사용한다.
- [최종 링크·체크 상태·소스 검사](final-audit.json)와 [원본 문서 갱신 기록](documentation-update.json)을 남겼다. 계획 보기본은 과제1 HTML만 내용이 변경됐고 다른 5개 출력은 기존 해시와 동일하다.

## 2. A~C 증거와 최종 코드 유효성

과거 실행을 이번 실행 횟수에 더하지 않는다. **이번에 실행한 제품 자동시험은 768건**이며 실제 Broker·프록시·OS Ctrl+C를 다시 실행하지 않았다. 자동시험은 제품/시험 대역, 실제 연동은 각 당시 프로세스·소켓·Broker 로그로 구분한다.

| 단계 | 유효한 실제 결과·직접 근거 | 최종 코드에 적용하는 이유·한계 |
| --- | --- | --- |
| A1 Broker 기동 | [20261008-a1-broker-01](../20261008-a1-broker-01/README.md): 전용 Broker 기동·중단·재기동 | 당시 Docker 준비 기록. 이후 구성은 A2/A3, 현재 격리 구성은 C1/C2/C3의 실행별 설정을 따른다 |
| A2 승인·명시적 해제 | [20261011-a2-connect-02](../20261011-a2-connect-02/README.md), [명령](../20261011-a2-connect-02/commands.jsonl), [두 콘솔/브로커](../20261011-a2-connect-02/broker-logs.log) | A2는 중간 콘솔. A3/후속 B/C의 최종 정상 수명주기와 연결하며 A2 Program이 현재와 같다고 주장하지 않음 |
| A3 Ctrl+C·새 프로세스 | [20261011-a3-shutdown-03](../20261011-a3-shutdown-03/README.md), [실제 종료 0 두 건](../20261011-a3-shutdown-03/verification.json), [총괄 7조건](../20261012-a-summary-01/README.md) | 당시 정상 기준. 이후 MqttConnection은 B의 제한/취소 처리가 추가됐고 콘솔도 확장됨. 현재 정상 6개 자동시험 및 C2 복구/C3a 실제 회귀로 보강 |
| B1 입력·접속 실패 | [최종 B 자동시험](../20261019-connect-failure-unit-03/README.md), [실제 TCP 거절](../20261019-connect-failure-mqtt-02/refused.stderr.log) | MqttConnection/설정/예외 내용은 현재와 동일(경로·개행 이동 구분). 초기 실패 분류·입력 회귀를 이번에 실행. MQTT 승인 거부는 대역 시험 |
| B2 대기 제한 | [실제 TCP 수락 후 승인 무응답 1초/2초](../20261019-connect-failure-mqtt-02/verification.json): 1043.4677/2040.8842ms·종료 1·EOF | 연결 구현·라이브러리 계약 유지. 현재 1초/2초 제한·늦은 완료 회귀 통과. 최초 접속 제한을 연결 후 PING 감지 제한과 혼용하지 않음 |
| B3 취소·늦은 승인·새 실행 | [실제 취소/새 정상 2세션](../20261019-connect-failure-mqtt-02/README.md), [현재 자동 TRX](../20261020-c4-unit-02/tests.trx) | 사용자/제한 후 늦은 성공·예외·승인 직전 취소·재사용 차단 회귀 통과. Program의 Ctrl+C→토큰 연결 유지 |
| C1 KeepAlive | [20261020-c1-keepalive-02](../20261020-c1-keepalive-02/README.md): K=5초, 무업무 12.0899808초, PING 2쌍, 종료 0 | C2에서 ConsoleRunner 진단만 추가. C2 복구의 12.0829546초/PING 2쌍 및 최종 C3a 회귀로 현재 콘솔 정상 동작 확인 |
| C2 Broker 종료/새 실행 | [20261020-c2-mqtt-01](../20261020-c2-mqtt-01/README.md), [시각·종료·소켓](../20261020-c2-mqtt-01/verification.json): 감지 53.4907ms, 프로세스 종료 관찰 79.4166ms·종료 1, 같은 Broker 재기동 뒤 새 Client/PID·종료 0 | 제품 소스 동일. Broker 복구 뒤 명시적 새 실행이며 자동 재접속이 아님. 그 실행의 사전 관찰 범위·동적 포트 사용 |
| C3a 전달·차단·정리 | [최종 초기 환경](../20261020-c3a-proxy-03/README.md), [접속 전 stop](../20261020-c3a-stop-01/README.md), [C3b 확장 후 회귀](../20261020-c3b-c3a-regression-01/README.md) | 양쪽 TCP 유지·응답 2바이트 폐기·역방향 전달·재개/종료 0. 최신 runner 회귀 결과 사용, 방화벽 변경 없음 |
| C3b 응답 무시 감지 | [두 세션 기준·측정·원본](../20261020-c3b-noresponse-02/README.md): 차단→감지 7.3297821/5.3980114초, 마지막 송신→감지 7.5744957/7.6373481초, 폐기→감지 2.5027692/2.5050294초·종료 1 | 사전 허용 7.4~8.75초/2.4~3.5초 및 차단 위상 반영. 동일 제품/도구/바이너리, 무응답 시 타임아웃 예외. 운영 SLA는 아님 |
| C4 순서·중복·늦은 알림 | [20261020-c4-unit-02](../20261020-c4-unit-02/README.md): 신규 7건, 직접 관련 46건 포함 전체 768건 통과 | 최초 알림을 보류한 중복 8회, 정상 해제 중 알림, Dispose 뒤 캡처 콜백 호출을 제어. 구독 전/후와 두 최초 원인 확인. 실제 OS/네트워크 동시 경합의 전수 검증은 아님 |

B 이후 변경은 [콘솔 비교](b-to-final-console.diff)에서 확인한다. Serilog 연결·명령 모드 분기·최초 종료 원인 선택·안전 단절 진단이 추가됐다. 정상 수신 모드의 입력/최초 접속 알고리즘은 유지되며 현재 B 회귀 19건과 정상 수명주기 6건, 종료 원인 21건으로 해당 변경의 영향을 확인했다. B의 53개 중 내용이 달라진 5개는 Program/ConsoleRunner/콘솔 csproj, 공통 Fake, COM01-B 수동 도구이며 마지막 항목은 B 접속 시험 도구가 아니다.

## 3. #6 원래 완료조건별 판정

| 번호 | 원래 완료조건 요지 | 충족 여부·근거 |
| --- | --- | --- |
| 1 | 무메시지·KeepAlive 2배 이상 유지·프로토콜 근거 | 충족 — C1 및 C2 복구, 12초 이상·PING 2쌍 |
| 2 | 실제 Broker 종료 후 범위 내 상실·이유·시각 | 충족 — C2 실제 종료·53.4907ms·정리·종료 1 |
| 3 | 연결 후 응답 중단의 감지 기준·오차·실제 비교 | 충족 — C3b 두 위상, 장애 전 criteria·실제 measurement·종료 1 |
| 4 | 중복 통지/명시적 종료 구분·늦은 알림 차단 | 충족 — C4 첫 콜백 게이트/해제 중 알림/종료 후 캡처 콜백 및 B3 늦은 승인 회귀 |
| 5 | Ctrl+C와 단절 경합의 정리·이유/코드 일치 | 충족 — 동일 취소 경계의 두 순서·진행 중 콜백·32회 동시 시작 회귀. UserCanceled/0, ConnectionLost/1, 정리 각 1회. 실제 Ctrl+C 전달은 기존 실제 세션 근거 |
| 6 | Broker 복구 후 새 실행 접속, 자동 재접속과 구분 | 충족 — C2 동일 Broker 재기동 후 새 PID/Client ID·PING·종료 0 |
| 7 | A~C의 유효한 증거·필요 회귀를 최종 코드에 연결 | 충족 — 본 표·A/B 병합/소스 대조·C2/C3 제품/바이너리 동일성·768건 최종 TRX |
| 8 | 자동/실제 조건·실패/한계를 표시하고 README·Issue·PR·증거 연결 | **충족** — README·본 보고서·[#6 Issue](https://github.com/inhhlee/amr-control-system-demo/issues/6)·[develop 대상 PR #31](https://github.com/inhhlee/amr-control-system-demo/pull/31)·[PR 본문](PR.md)을 실제 연결하고 8조건 갱신. [게시 확인](../20261020-pr-publication-01/publication.json) |

최초 취합 시점에는 7개 충족·게시 연결 1개 부분 충족이었다. 후속 요청으로 커밋·푸시·PR 생성과 Issue 결과 연결을 수행해 8개를 체크했다. 완료조건의 근거 확인과 develop 병합은 구분하며 Issue는 아직 닫지 않는다.

## 4. 선행 #4·#5 원래 조건의 유지

| #4 조건 | 판정·현재 연결 |
| --- | --- |
| 1 기준 브랜치·문서·경로 | 충족 — A 총괄 및 선행 병합 조상 확인 |
| 2 Broker 기동·MQTT 승인 후 Connected | 충족 — A1/A2/A3, 현재 정상 연결 자동시험 및 C2/C3 실제 승인 |
| 3 설정·고유 ID·양쪽 로그 | 충족 — A3/C2/C3 콘솔와 Broker의 같은 ID 대응 |
| 4 Ctrl+C 정리·종료 0 | 충족 — A3 실제 2회, C2 복구/C3a 회귀 |
| 5 새 ID 재실행·이전 자원 종료 | 충족 — A3/B 새 세션/C2 새 프로세스 |
| 6 정상 자동/실제 구분·복원/빌드/회귀 | 충족 — 당시 복원/실제 로그, 현재 빌드·768건 |
| 7 README·Issue·PR·증거 연결 | 충족 — PR #13/#14 develop 반영·#4 completed; 당시 기록의 open 문구는 과거 상태 |

| #5 조건 | 판정·현재 연결 |
| --- | --- |
| 1 비수신 로컬 포트 접속 실패·원인·Connected 없음 | 충족 — B 실제 거절 및 현재 콘솔 초기 실패 회귀 |
| 2 MQTT 거부/전송/제한/사용자 취소 구분 | 충족 — B 대역/실제 범위 구분, 현재 19건 관련 회귀 |
| 3 잘못된 입력의 연결 시도 전 거부 | 충족 — 현재 콘솔 8개 입력 시험에서 Client/Connect 호출 없음 |
| 4 끝나지 않는 접속의 제한·취소·대기 상한 | 충족 — B 실제 TCP 승인 대기, 현재 비협조 대역 제한/취소 회귀 |
| 5 정리·늦은 결과의 Connected 재전환 차단 | 충족 — B 실제 EOF, 현재 늦은 성공/실패·취소 시험 |
| 6 실패/취소 후 새 정상 프로세스와 회귀 | 충족 — B 실제 정상 2세션, 현재 정상/종료 관련 회귀·C2 복구 |
| 7 환경/대역/실제 구분 및 Issue·PR 연결 | 충족 — PR #25 develop 반영·#5 completed. 이번은 과거 완료조건을 재대조했으며 원격 수정 없음 |

## 5. 실패 이력·한계·남은 단계

- 이전 실패/중간 결과는 그대로 보존한다: A2/A3 초기 권한·도우미 실패, B unit-01 권한/중간 unit-02 및 mqtt-01 도구 오류, C1 첫 빈 출력 처리, C2 unit-01 API 경고, C3a proxy-01 빈 출력 오류·proxy-02 중간 통과 뒤 stop 보완, C4 unit-01 빌드 쓰기 권한 오류. 최종 실행 ID는 위 표를 따른다. 실패 로그를 통과 수에 포함하지 않는다.
- C4 전체 768건은 단위/컴포넌트 자동시험이다. 실제 MQTT/OS Ctrl+C를 새로 동시에 주입하지 않았고, 실제 인증 Broker 거부·Seq 새 진단 필드 수집·GUI·장시간 부하·벽시계 변경·모든 경합 스케줄·운영 SLA·장비 안전성은 미검증이다. 별도 COM-02 Heartbeat, COM-06 자동 재접속·재구독과 과제1 전체 완료를 뜻하지 않는다.
- 정상 종료는 실제 결과가 중요하다. MQTT 이유 코드 NormalDisconnection만으로 사용자 취소를 판정하지 않는다. C3b의 해당 이유 코드는 MqttCommunicationTimedOutException 및 최초 원인 ConnectionLost·종료 1과 함께 읽는다.
- 다음 요청 단계: PR #31 리뷰/필요 수정 → 요청된 merge commit 병합 → 8조건과 develop 반영을 확인한 뒤 #6 종료. 현재 병합은 명시적으로 보류했다. `git flow feature finish`는 사용하지 않는다.
- [PR 본문](PR.md)은 게시한 설명과 같은 범위의 기록이다. 코드 저장소만 커밋·푸시하고 C:\process-plan 문서 저장소는 별도 원본 갱신 상태로 보존했다. 이전 실행의 미커밋·PR 없음 표시는 해당 실행 시점의 기록이다.
