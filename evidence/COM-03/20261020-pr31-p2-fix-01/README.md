# PR #31 P2 두 건 수정·재현·회귀 검증

2026-10-20, 같은 `feature/6-mqtt-keepalive`에서 [리뷰의 R1·R2](https://github.com/inhhlee/amr-control-system-demo/pull/31#pullrequestreview-5580054400)를 수정했다. **검증 도구 코드 4개를 고쳤고 제품 MQTT 연결·단절 감지 코드와 기존 합격 기준은 유지했다.** 새 도구 시험 30건과 실제 C1·C2·C3a·C3b 회귀가 통과했다. PR #31은 develop 대상이며 재검토·병합·Issue 종료가 남아 있다. 이번 요청에는 병합이 포함되지 않는다.

## 코드·기준·입력

- 기준 커밋 `da1e64083f73e12fc5ec992ba3dab03bddcf6679` + 이번 수정. [시험한 도구/시험 코드 6개 SHA-256](../20261020-pr31-p2-green-02/source-version.json), [기존 제품·xUnit 코드 대조](source-audit.json), 각 실제 실행의 `source-version.json`·`environment.json`을 함께 사용한다. 이 파일들과 함께 게시되는 커밋이 수정본이다.
- [사전 수정·검증 기준](../20261020-pr31-fix-plan-01/criteria.json), 실행 전 [재현 기준](../20261020-pr31-p2-green-02/criteria.json). R1 종료 관찰 상한은 원래 2초이며 완화하지 않았다. C3b의 시각 필드를 제외한 사전 기준 23개는 기존 2초 위상 실행과 동일하다.
- 기능명세 v0.7 COM-03, 요구사항정의 v0.5 R-04, 공통규칙 v0.4 C-09·C-10, 결정사항 v0.5 D-08·D-09를 유지한다. 원본은 문서 저장소에서 관리하며 새 운영 정책·SLA를 추가하지 않았다.
- 실제 연동 환경: Windows 10.0.26200, SDK 10.0.401, MQTTnet 5.2.0.1603, PowerShell 7.6.5, 기존 Mosquitto 2.1.2-alpine 이미지. 실행 중인 기존 `amr-demo-mosquitto-1`, 중지 상태 `amr-mosquitto`·`amr-seq` 3개의 상태를 보존했다. 별도 이름·루프백 임시 포트의 시험용 Broker만 만들고 제거했다.

## 수정 내용과 이유

1. [ResponseGateProxy.cs](../../../tools/COM-03/ResponseGateProxy.cs): block/resume의 gate 대기를 25ms 단위로 제한하고 실패하면 최신 제어 파일을 다시 읽는다. 읽지 않는 Client 때문에 응답 쓰기가 막혀도 후속 stop을 읽고 취소할 수 있다. block ACK와 응답 쓰기의 같은 gate 직렬화는 유지한다. stop은 취소 요청 뒤 Session/Pump의 실제 종료·소켓 반환까지 기다린다.
2. [C1](../../../tests/manual/COM03-C/Verify-KeepAlive.ps1), [C2](../../../tests/manual/COM03-C/Verify-BrokerShutdown.ps1), [C3](../../../tests/manual/COM03-C/Verify-ResponseGate.ps1): 로그 조회/저장, stop, rm을 각각 시도하고 뒤에서 상태를 확인한다. 실패는 합산해 기록하며 이미 있는 runner 오류를 덮어쓰지 않는다. 오류 보고 파일 자체를 쓸 수 없을 때도 원래 오류와 정리 시도를 보존한다. 기존 확인된 64자리 소유 컨테이너 ID 검사와 일반 stop/rm 방식을 유지한다.

제어 전용 작업을 추가하는 대안은 즉시 취소를 더 분명히 분리할 수 있지만 현재 단일 세션 도구에는 별도 작업 수명·공유 상태가 늘어난다. 25ms 재확인은 기존 폴링·버퍼 구조를 그대로 사용한다. Broker 정리를 공통 프레임워크로 묶는 대신 현재 세 스크립트의 짧은 정리 부분을 독립 try/catch로 보완했다. 성능 비교 시험을 한 것은 아니며, 다중 세션·고빈도 제어가 필요해지면 다시 설계한다.

## 새 재현 시험과 실제 회귀

| 구분 | 입력·기대 | 실제 결과·판정 | 직접 근거 |
| --- | --- | --- | --- |
| 수정 전 재현 | 응답 backpressure 후 block/resume→stop; 각 도구의 정리 오류 주입 | 30건 중 20건 실패(프록시 2·정리 18). 접속 전 stop 등 정상 대조는 통과 | [red-02 판정](../20261020-pr31-p2-red-02/verification.json), [프록시](../20261020-pr31-p2-red-02/proxy-results.json), [정리](../20261020-pr31-p2-red-02/cleanup-results.json) |
| R1 수정 후 | 읽지 않는 실제 루프백 TCP 상대, 미승인 block/resume 대기 중 stop. 시험 상대를 닫기 전에 2초 이내 성공 종료 | block 34.2057ms, resume 29.802ms, 접속 전 33.103ms. ACK·정상 Task 완료·Stopped 모두 확인. 3/3 통과 | [green-02 프록시](../20261020-pr31-p2-green-02/proxy-results.json), 각 case의 proxy.jsonl |
| R2 수정 후 | 세 도구 × 정상, logs 실패, 로그 파일 쓰기 실패, stop 실패, rm 실패, ps 실패, 기존 오류+logs 실패, 오류 보고 파일 실패, 미확인 ID | 27/27 통과. 후속 정리·상태 조회 시도, 소유 ID 제한, 최초 오류 보존, stop/rm/잔존 오류 합산 확인 | [green-02 정리](../20261020-pr31-p2-green-02/cleanup-results.json), [전체 30건](../20261020-pr31-p2-green-02/verification.json) |
| C1 실제 MQTT | 기존 KeepAlive 5초·무메시지 10초 이상·PING 2쌍·실제 Ctrl+C | 12.0615306초, PING 2쌍, 업무 PUBLISH 0, 종료 0·정리 통과 | [판정](../20261020-pr31-c1-regression-01/verification.json), [Broker 로그](../20261020-pr31-c1-regression-01/broker-final.log), [정리](../20261020-pr31-c1-regression-01/cleanup.json) |
| C2 실제 MQTT | 격리 Broker 정상 종료, 동일 Broker 재기동 후 새 콘솔 | 감지 50.0696ms·종료 관찰 75.1551ms·상실 1회·종료 1, 새 PID/Client ID·PING 2쌍·종료 0·정리 통과 | [판정](../20261020-pr31-c2-regression-01/verification.json), [재기동](../20261020-pr31-c2-regression-01/restart.json), [정리](../20261020-pr31-c2-regression-01/cleanup.json) |
| C3a 실제 MQTT | 응답 차단 ACK 후 폐기·TCP/반대 전달 유지, 재개 후 Ctrl+C | 2바이트 폐기, 차단 중 응답 전달 0, 양쪽 TCP 유지, 종료 0·정리 통과 | [판정](../20261020-pr31-c3a-regression-01/verification.json), [프록시 로그](../20261020-pr31-c3a-regression-01/proxy.jsonl), [정리](../20261020-pr31-c3a-regression-01/cleanup.json) |
| C3b 실제 MQTT | 차단 대기 2초, 기존 마지막 송신 7.4~8.75초·첫 폐기 2.4~3.5초 기준 | 차단→감지 5.3131973초, 마지막 송신→감지 7.5537873초, 폐기→감지 2.5075987초. 타임아웃·자율 종료 1, Broker 실행 유지·정리 통과 | [장애 전 기준](../20261020-pr31-c3b-regression-01/criteria.json), [측정](../20261020-pr31-c3b-regression-01/measurement.json), [판정](../20261020-pr31-c3b-regression-01/verification.json), [정리](../20261020-pr31-c3b-regression-01/cleanup.json) |

재현 명령: `pwsh -NoProfile -File tests/manual/COM03-C/Verify-ToolRecovery.ps1 -RunId <새-ID>`. [시험 코드](../../../tests/manual/COM03-C/Verify-ToolRecovery.ps1)는 원본 finally AST의 본문을 실행하면서 Docker 호출 경계만 대역으로 바꾼다. [TCP 시험 코드](../../../tests/manual/COM03-C/ToolRecoveryProbe.cs)는 실제 수정 프록시와 함께 컴파일한다. R2 오류 주입 시험에서 실제 Docker를 호출한 것은 아니다. 실제 Docker 정리는 위 네 회귀 실행에서 따로 확인했다.

실제 실행 명령은 각각 `Verify-KeepAlive.ps1`, `Verify-BrokerShutdown.ps1`, `Verify-ResponseGate.ps1`, `Verify-ResponseGate.ps1 -MeasureTimeout -BlockDelaySeconds 2`에 표의 실행 ID를 `-RunId`로 지정했다. C1·C3a·C3b의 세 빌드는 경고·오류 0이며 build.log를 보존한다. C2는 직전 빌드한 제품을 재사용했다. Docker 명령은 각 실행의 commands.jsonl에 있다.

## #6 완료조건·기존 근거 재사용

원래 8개 완료조건은 유지한다. 1·2·3·6은 이번 실제 C1/C2/C3a/C3b로 보강했고, 4·5는 변경 없는 제품/xUnit 코드에 대한 기존 C4로 유지한다. 7·8은 [소스 대조](source-audit.json), 이 수정 기록, [A~C 총괄](../20261020-ac-summary-01/README.md), PR/Issue 연결로 보강한다.

기존 C4 스냅샷 72개 중 70개가 현재와 같고 변경 2개는 프록시와 C3 수동 도구다. 제품·xUnit·솔루션/SDK 정의 60개는 모두 동일하다. C1/C2 도구는 그 스냅샷 목록 밖이므로 Git diff와 이번 별도 실제 실행으로 검증했다. 기존 제품 자동시험 **768/768은 재사용 근거이며 이번에 재실행하거나 새 30건에 합산하지 않는다.** [A~B 원본/최종 코드 유효성](../20261020-ac-summary-01/ab-code-validity.json)도 계속 적용한다.

## 실패 이력·한계·재검토 질문

- [원래 리뷰 재현](../20261020-pr31-review-01/README.md)은 당시 코드·결과를 보존한 과거 근거다. 당시 미커밋 표시는 그 시점의 상태이며 이번 수정 커밋에 함께 게시한다.
- [red-01](../20261020-pr31-p2-red-01/verification.json)은 새 시험 스크립트가 finally 본문 대신 ScriptBlock 객체를 반환한 시험 준비 오류다. 잘못된 54건/56실패 집계는 제품 판정에 사용하지 않는다. 61MB 객체 직렬화 출력은 크기·SHA-256·오류 설명으로 축약했으며 실행 상태와 프록시 결과는 보존했다. 바로잡은 red-02를 기준 실패로 사용한다.
- [green-01](../20261020-pr31-p2-green-01/verification.json)은 R1만 반영됐고 샌드박스 쓰기 거부로 R2 수정이 아직 적용되지 않은 중간 실행이다. 프록시 3건 통과·정리 18건 실패를 보존한다. 권한 승인 후 R2를 적용한 green-02가 최종본이다.
- 커밋 전 소스·문서 diff 공백 검사는 통과했다. 전체 diff 검사의 경고는 PowerShell 원본 오류 로그의 후행 공백·마지막 빈 줄에 한정되며 실행 증거를 변형하지 않고 보존했다.
- 질문: block ACK의 의미가 약해졌는가? 같은 gate를 획득한 뒤에만 ACK하므로 기존 의미를 유지한다. 미승인 명령은 최신 제어 파일의 후속 stop으로 대체될 수 있다.
- 질문: 로그 실패를 무시해서 통과하는가? 정리를 계속 시도하되 runner는 원래 오류 또는 첫 정리 오류로 실패한다. cleanup.json의 자원 상태 확인과 전체 시험 성공은 구분한다.
- 질문: 제품 시험을 왜 반복하지 않았는가? 수정은 검증 도구 4개이며 제품/xUnit 소스 60개가 동일하다. 기존 768건과 별도로 도구 오류 주입·실제 MQTT 회귀를 수행했다. 제품 변경이나 새로운 제품 결함이 발견되면 관련 제품 시험을 다시 실행한다.
- 실제 Ctrl+C/네트워크 동시 경합 전수, 실제 인증 Broker 거부, Seq 새 필드 수집, 장시간 부하·시각 변경, 모든 Docker 장애는 추가 검증하지 않았다. COM-06 자동 복구·COM-02 AMR Heartbeat와 운영 SLA는 제외 범위다. CI 통과·리뷰 승인·병합·Issue 종료로 확대하지 않는다.
