# PR #31 / 55ac854 재리뷰

[GitHub COMMENT 재리뷰](https://github.com/inhhlee/amr-control-system-demo/pull/31#pullrequestreview-5580657608)에 같은 커밋을 고정해 판정·실행 결과·근거 범위·남은 단계를 게시했다.

2026-10-20 KST. 검토 대상은 `55ac85491bed143f1eb33162fa9a601eec9d2853`, base는 develop이다. **기존 P2 R1·R2의 해결을 확인했고, 이번 수정 범위에서 추가 수정이 필요한 결함은 발견하지 않았다.** PR/Issue는 open이며 병합·Issue 종료는 수행하지 않았다. 이 폴더는 재리뷰에서 만든 로컬 미커밋 근거다.

## 판단과 직접 근거

| 지적 | 수정 확인 | 검증과 판정 |
| --- | --- | --- |
| R1: gate에서 기다리는 block이 후속 stop을 가림 | [프록시](../../../tools/COM-03/ResponseGateProxy.cs)의 25ms gate 대기 실패는 최신 명령을 다시 읽는다. stop은 gate 없이 취소한다. block/resume ACK와 응답 쓰기는 같은 gate로 직렬화한다. finally는 Session/Pump 종료를 기다린다 | 커밋의 시험을 그대로 재실행. backpressure 중 block/resume 뒤 stop, 접속 전 stop 3/3 통과. 각각 19.0545/31.2582/34.3891ms. 상대 소켓을 강제로 닫기 전 성공 Task 완료·ACK·Stopped 확인. [결과](proxy-results.json) |
| R2: 로그 수집/저장 실패가 stop/rm을 생략 | C1/C2/C3의 각 로그·stop·rm에 독립 catch가 있고 이후 상태를 확인한다. 첫 runner 오류를 유지하면서 정리 오류를 합산한다. 보고 파일 저장 실패도 기존 오류를 덮어쓰지 않는다 | 세 원본 finally를 AST로 실행하고 Docker 경계만 대역으로 바꾼 27/27 통과. 정상·logs·로그 저장·stop·rm·ps·최초 오류·오류 보고 저장·미확인 ID를 검증. [결과](cleanup-results.json), [stop/rm/잔존 오류 합산 예](Verify-ResponseGate-stop-fail/cleanup-failure.log) |

명령: `pwsh -NoProfile -File tests/manual/COM03-C/Verify-ToolRecovery.ps1 -RunId 20261020-pr31-rereview-01`. [실행 전 기준](criteria.json), [소스 6개](source-version.json), [30/30 결과](verification.json). 새 실행은 실제 루프백 TCP 3건과 Docker 경계 대역 27건이며 실제 MQTT·Docker를 다시 실행한 것이 아니다. 기존 red-02의 20실패와 green-02의 30통과는 [수정 보고서](../20261020-pr31-p2-fix-01/README.md)에서 보존한다.

## 실제 회귀·코드 대조

[감사 결과](evidence-audit.json)와 [C1/C2 원본 로그·바이너리 대조](c1-c2-binary-audit.json)를 새로 작성했다. 보고서의 통과 문구만 재인용하지 않고 다음 원본을 대조했다.

- 네 실제 실행의 소스 스냅샷(40/72/74/74개)은 현재 파일과 차이 0이다. 최종 도구 시험 소스 6개는 55ac854의 Git blob과도 일치한다. 제품/xUnit·솔루션/SDK 정의 60개는 C4 시험본과 동일하다. 55ac854에는 제품/xUnit 변경이 없다.
- C1 원본 Broker 로그: 같은 Client ID의 PINGREQ/PINGRESP 각2회, 업무 PUBLISH 0, K=5초·관찰12.0615306초·실제 Ctrl+C 종료0. C1은 별도 바이너리 스냅샷을 저장하지 않았으며 소스·성공 빌드/실행 로그가 근거다.
- C2 원본 단절 stdout 시각−stop 요청 시각=50.0696ms, ConnectionLost 1회. 재기동 뒤 새 PID/Client ID, PING 각2회·관찰12.0050119초·종료0. 자동 재접속으로 집계하지 않는다.
- C3a 차단 ACK 후 응답 폐기·반대 전달·TCP 유지와 재개/종료0을 확인했다. C3b 원본 proxy/console 시각으로 차단→감지5.3131973초, 마지막 송신→감지7.5537873초, 첫 폐기→감지2.5075987초를 재계산했다. 기준 저장은 장애보다 앞서며 기존23필드 차이0, 차단 이후 응답 전달0·resume/stop 제어0·Ctrl+C 없음·Client EOF·타임아웃/종료1·Broker 실행 유지다.
- C2/C3a/C3b 바이너리 스냅샷은 각각4개 모두 현재 빌드와 동일하다. 네 실행의 원본 Docker 명령은 확인된 소유 ID에 stop/rm 성공을 기록하며, 전후 목록은 기존3개 상태 동일·시험 컨테이너 없음이다.
- 기존 제품768건은 이전 TRX·소스 동일성 근거로 재사용하며 이번에 재실행하거나 새30건에 합산하지 않는다. 전체 제품/MQTT 재실행은 하지 않았다.

## #6 완료조건·남은 단계

원래8조건을 바꾸지 않는다. 1·2·3·6은 실제 유지/종료/무응답/새 실행 근거, 4·5는 변경 없는 제품의 C4 중복·늦은 알림·종료 원인/자원 정리 시험, 7·8은 소스/바이너리·실패 이력·README/Issue/PR 연결로 충족 근거가 유지된다. 두 P2로 남았던 수정 항목은 해소됐다.

- 남은 절차: 사용자 요청에 따른 develop 병합·반영 확인, 그 뒤 Issue 종료. PR 작성자와 같은 계정이므로 이번 GitHub 제출은 COMMENT이며 정식 APPROVE로 표시하지 않는다.
- CI checks/statuses 0개다. 로컬 근거와 구분하며 CI 통과로 표시하지 않는다.
- 기존 미검증 범위: 실제 OS Ctrl+C/네트워크의 모든 동시 경합, 실제 인증 Broker 거부, Seq 새 필드, 장시간 부하·시각 변경·모든 Docker 장애. COM-06 자동 복구·COM-02 AMR Heartbeat·GUI/Core/DB와 운영 SLA는 이번 완료 범위에 포함하지 않는다.
- 새 필수 수정 사항은 없다. 제품/도구 변경이나 새 실패가 발생하면 관련 시험을 다시 검토한다. 이번에는 제품·시험 소스 수정, 추가 커밋/푸시, Issue 체크 변경, 병합을 하지 않았다.

## 설계 판단과 재검토 질문

현재 단일 세션 도구에서는 유한 gate 대기가 기존 폴링·버퍼 구조를 유지하면서 종료 취소를 관찰하게 하므로 타당하다. 별도 제어 작업도 대안이지만 수명·공유 상태 관리가 추가된다. 각각의 짧은 cleanup 시도는 현재 규모에 맞으며 공통 정리 프레임워크가 필수인 근거는 없다. 성능 대안을 실측 비교한 것은 아니다.

1. block ACK 뒤 응답이 전달될 수 있는가? block 상태 변경/ACK와 응답 WriteAsync가 같은 gate를 사용하며, 실제 C3a/C3b에서도 차단 뒤 응답 전달0을 확인했다. ACK 전 완료된 쓰기를 소급 취소하지 않는다.
2. 정리 오류를 숨겨 통과하는가? 원래 runner 오류 또는 첫 정리 오류가 남아 전체 실행은 실패하고, 이후 정리 시도와 오류 합산만 계속한다. stop 실패 시 rm도 실패하고 컨테이너가 남을 수 있음을 오류로 기록한다.
3. 실제 Broker 시험을 대역으로 바꾸었는가? 이번 새 실행은 도구 오류 경로 확인이며 기존 실제 네 회귀의 원본/코드 대응을 별도로 감사했다. 그 결과를 새 실제 실행으로 집계하지 않는다.
