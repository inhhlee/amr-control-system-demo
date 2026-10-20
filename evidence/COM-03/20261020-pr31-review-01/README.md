# PR #31 / #6 검토 결과

2026-10-20 Codex 검토. 대상은 `da1e64083f73e12fc5ec992ba3dab03bddcf6679`, base `d777ab7d8ef034d66a862343231268cd1476af24`, [PR #31](https://github.com/inhhlee/amr-control-system-demo/pull/31) → develop이다. **수정 필요 P2 2건을 시험 도구에서 재현했다. 병합을 보류한다.** 제품 단절 진단 변경에서 별도의 확인된 결함은 발견하지 못했다.

[GitHub COMMENT 리뷰](https://github.com/inhhlee/amr-control-system-demo/pull/31#pullrequestreview-5580054400)에 두 인라인 지적과 조건 대조 결과를 게시했다. [리뷰 상태](review-result.json). 아래 재현 파일은 로컬 미커밋 근거이며 PR head·제품 소스·Issue 체크는 변경하지 않았다.

## 1. 기준·검토 범위

- [#6 Issue](https://github.com/inhhlee/amr-control-system-demo/issues/6)의 원래 8조건, 기능명세 v0.7 COM-03, 공통규칙 v0.4 C-09·C-10, 결정사항 v0.5 D-08·D-09, 과제계획 v0.48/C 안내 v0.12를 대조했다. 원본 위치는 C:\process-plan이며 원문 요구·개발 계약·미정 자동 복구/AMR 상태 정책을 구분했다.
- 코드/시험 변경 3개, 수동 실행 도구 3개, 최소 프록시/시작 도구 및 README·A~C 원본 증거를 검토했다. PR 파일 목록·해당 파일 diff와 로컬 head를 확인했다. C4의 최초 원인 선택·캡처한 늦은 콜백·정리 수명과 도구의 소켓/프로세스/컨테이너 수명을 함께 확인했다.
- 기존 제품 시험은 다시 실행하지 않았다. 이번 실행은 아래 두 결함 재현이며, 별도 임시 루프백 소켓과 Docker 대역만 사용했다. 기존 Broker/컨테이너·방화벽을 변경하지 않았다.
- CI 조회: 해당 head의 checks/statuses 0개. CI 통과로 표시하지 않는다.

## 2. 확인된 결함과 수정 방향

### R1 [P2] block이 응답 쓰기를 기다리면 후속 stop을 읽지 못함

- 위치: [ResponseGateProxy.cs 114행](../../../tools/COM-03/ResponseGateProxy.cs#L114), `Commands()`의 `await responseGate.WaitAsync(stop.Token)`. 응답 쓰기는 46~56행에서 같은 gate를 보유한다.
- 조건: Client가 응답을 읽지 않아 프록시의 `destination.WriteAsync`가 대기 → `1 block`이 gate 획득을 기다림 → 제어 파일을 `2 stop`으로 교체.
- 실제: [재현 JSON](proxy-stop-reproduction.json)에서 backpressure=true, fake Broker 송신393,216바이트, block ACK 없음, stop ACK/종료가 2초 동안 모두 없음. [실제 프록시 로그](proxy.jsonl)는 peer 강제 종료 뒤에야 sequence1 block을 기록했다. 정상 종료 조건이 생기기 전에는 같은 Commands 루프가 새 명령을 읽을 수 없다.
- 영향: 108행의 stop gate 우회만으로 해결되지 않는다. 이미 이전 block/resume 대기 안에 갇힌 경우 그 분기에 도달하지 못한다. 도구 README의 "쓰기 대기와 무관한 stop·실제 종료" 보장과 다르고 사용자가 peer를 닫거나 프로세스를 강제 종료해야 한다.
- 수정: stop 폴링/취소를 gate 대기와 독립시키거나, gate를 유한 시간 기다린 뒤 최신 제어 파일을 재확인한다. 응답 write와 block ACK의 직렬화는 유지해야 한다.
- 검증: [Reproduce-ProxyStop.ps1](Reproduce-ProxyStop.ps1)은 PR 프록시 소스를 수정 없이 컴파일하고 읽지 않는 Client·가짜 TCP Broker를 연결한다. 수정 후 같은 조건에서 stop ACK·Task 종료·소켓 반환을 확인하고 block 이후 응답 전달 0건도 회귀한다. 이 재현은 MQTT 제품의 무응답 감지 시험이 아니다.

### R2 [P2] 진단 로그 실패가 컨테이너 정리 전체를 건너뛰게 함

- 대표 위치: [Verify-ResponseGate.ps1 256~258행](../../../tests/manual/COM03-C/Verify-ResponseGate.ps1#L256). C1 `Verify-KeepAlive.ps1` 170~173행, C2 `Verify-BrokerShutdown.ps1` 181~183행도 같은 구조다.
- 조건: finally의 첫 Docker logs 호출 또는 그 출력 파일 저장이 실패. 모두 `$ErrorActionPreference='Stop'`이고 로그·stop·rm이 같은 try에 있다.
- 실제: [재현 JSON](cleanup-reproduction.json). PR의 cleanup try/catch를 AST로 추출해 로그 조회에 IOException을 주입했을 때 세 도구 모두 호출 목록에 logs만 있고 stopAttempted=false·removeAttempted=false다. cleanup-failure.log는 남기지만 정리를 시도하지 않는다.
- 영향: 진단 단계의 실패 때문에 이번 시험에서 만든 Broker가 남을 수 있다. C2는 복구 뒤 실행 중인 Broker를 그대로 남길 수도 있다. 로그 실패를 제품 시험 실패로 표시하는 것과 별개로 생성 자원을 정리해야 한다.
- 수정: 로그 수집·stop·rm·상태 확인을 각각 독립된 best-effort 단계/중첩 finally로 실행하고 실패를 합산한다. 로그 오류로 다음 정리 단계를 생략하거나 성공으로 표시하지 않는다. 정리 대상은 확인한 실행별 컨테이너 ID로 유지한다.
- 검증: [Reproduce-Cleanup.ps1](Reproduce-Cleanup.ps1)은 원본 cleanup 블록을 그대로 실행하되 Docker 경계만 대역으로 교체한다. 실제 Docker 호출은 0회다. 수정 후 logs/출력 실패 각각에서도 stop/rm 시도 및 오류 판정이 모두 남는지 확인한다.

## 3. #6 완료조건과 증거 대조

| 조건 | 확인한 근거·판정 |
| --- | --- |
| 1 KeepAlive 유지 | C1 원본 Broker 로그의 같은 ID PINGREQ/PINGRESP 각2, K5초·12.0899808초·종료0 일치. C2 복구·C3a 회귀로 현재 콘솔 보강. 기존 정상 결과 유효 |
| 2 실제 Broker 종료 | C2 lost 원본에 ConnectionLost1건·이유/UTC/ID·종료1. 감지53.4907ms와 정상 정리 기록 대응. 기존 결과 유효 |
| 3 응답 중단 시간 | 두 세션의 사전 기준→승인→block→폐기→감지 순서, 차단→감지7.3297821/5.3980114초 재계산 일치. 폐기 뒤 응답 전달0·타임아웃 예외·종료1. 기존 측정 유효. 도구의 종료 실패 경로는 R1/R2 보완 필요 |
| 4 중복/늦은 알림 | C4 콜백 게이트·중복8회·Dispose 뒤 캡처 delegate 호출을 확인. 단일 원인·정리 및 B3 늦은 승인 회귀 근거 있음. 전수 스케줄 증명으로 확대하지 않음 |
| 5 취소/단절 경합 | 동일 취소 토큰의 두 최초 순서·진행 중 콜백·32회 동시 시작 결과가 원인/코드와 대응. 실제 OS Ctrl+C·실제 Broker 단절은 각각의 기존 세션이며 동시 전수 실험은 아님 |
| 6 복구 뒤 새 실행 | C2 원본의 새 PID/Client ID·UserCanceled/종료0을 확인. 자동 재접속 아님 |
| 7 A~C 최종 코드 | C4 소스72개 해시 동일, A3 ZIP12개/B53개 원본·병합/현재 대조 일치. TRX768/768·실패/미실행0. 검증 이력과 이번 재현을 구분 |
| 8 범위/실패/한계·게시 연결 | README·Issue·PR·원본 근거 연결과 한계 표시 존재. Issue/PR의 8개 체크는 정상 경로 근거에 대응하지만, 이번에 발견한 R1/R2의 실패 경로 수정·검증 전에는 최종 승인/병합을 권고하지 않음 |

[C1/C2 원본 대조](c1-c2-evidence-audit.json) · [C3b 재계산/소스/TRX 대조](evidence-audit.json). 기존 실패/중간 실행을 통과로 바꾸거나 복사하지 않았다. 이번 재현 도우미의 첫 경로 지정 오류는 C:\tools를 참조해 제품 시험 전에 실패했으며, 저장소 기준 상대 경로를 바로잡은 뒤 위 재현을 실행했다. 제품 결함 재현은 수정한 도우미로 실행한 결과다.

## 4. 설계 판단·대안·PR 질문

제품의 `Interlocked.CompareExchange`로 최초 종료 요청자를 고르고 승자가 결과를 기록한 뒤 취소하는 방식은 이번 단일 세션 계약에 맞는다. 늦은 요청이 먼저 대기를 풀지 않도록 하는 조건을 유지해야 한다. 로그에는 자유 형식 Broker 이유/예외 메시지를 넣지 않아 C-10의 안전한 장애 진단 범위와 맞는다.

R1은 유한 gate 대기 후 재확인이 작은 변경이고, 별도 stop 감시 작업은 취소 반응을 분리하기 쉽지만 추가 작업의 시작·취소·종료 소유권이 필요하다. 둘 다 응답 버퍼나 범용 네트워크 프레임워크를 추가할 필요는 없다. R2는 독립 try/finally로 충분하며 범용 자원 관리 추상화의 필요는 확인하지 못했다. 성능·메모리 벤치마크는 수행하지 않았다.

예상 PR 질문:

1. **기존768건·실제 MQTT 통과가 무효인가?** 아니다. 원본 수치/코드 대응은 확인했다. 두 신규 도구 실패 경로를 그 성공 결과가 다루지 않았다는 결함이다.
2. **stop이 gate를 우회하는데 왜 멈추는가?** stop 분기 전, 같은 루프가 이전 block의 gate await에서 멈춰 새 파일을 읽지 못한다. Command dispatch 자체의 취소 반응을 보완해야 한다.
3. **리뷰에서 제품을 수정하거나 Issue 체크를 바꿨는가?** 수정/커밋/푸시/병합·Issue 상태 변경은 하지 않았다. 리뷰 권고와 실제 게시 상태를 구분한다. 수정 요청 후 해당 실패 재현과 변경 영향이 있는 실제 C3a/C3b 도구 회귀를 확인한다.

운영 SLA, 장시간/부하·벽시계 변경·실제 인증 Broker·Seq 새 필드·GUI·AMR 상태·자동 재접속은 기존 미검증/후속 범위다. 리뷰 재현에는 실제 MQTT·GUI 시험을 새로 포함하지 않았다.
