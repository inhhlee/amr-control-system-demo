# COM03-C4 중복 단절·늦은 알림·종료 경합 검증

2026-10-20 Codex 실행. **신규 7건 포함 전체 자동시험 768건 통과, 실패·건너뜀 0. 빌드 경고·오류 0.** C4에서 제품 코드를 바꾸지 않고 기존 종료 계약의 빈 검증 범위를 보완했다. [A~C 최종 취합·#6 완료조건](../20261020-ac-summary-01/README.md).

## 1. 목표와 적용 기준

- 관련 Issue: [#6](https://github.com/inhhlee/amr-control-system-demo/issues/6), COM03-C4. 기능명세 v0.7 COM-03, 공통규칙 v0.4 C-09·C-10, 결정사항 v0.5 D-08·D-09. 원본은 C:\process-plan. 착수 시 과제계획 v0.46·로컬 C 안내 v0.10, 결과 반영은 v0.47·v0.11이며 계약 변경은 없다.
- 먼저 기록한 [criteria.json](criteria.json): 순서 제어 게이트, 각 비동기 대기 watchdog 3초, 최초 원인/종료 코드 일치, 정리 한 번, 늦은 결과 불변. 이 watchdog은 제품의 운영 응답 시간 보장이 아니다.
- 제어 범위는 IMqttClient 알림 및 Program이 Ctrl+C에서 전달하는 동일한 사용자 취소 토큰이다. 실제 OS Ctrl+C와 Broker 단절을 동시에 발생시킨 시험은 아니다. 실제 Ctrl+C·MQTT는 C2 복구 세션과 C3a 회귀, 실제 장애는 C2·C3b 근거를 사용한다.
- 별도 spec_revision·verification_index 추적기는 없으며 실행 ID·소스 SHA-256·TRX로 식별한다.

## 2. 실행 환경·코드

| 항목 | 실제 기준 |
| --- | --- |
| 위치·브랜치 | C:\amr-control-system-demo / feature/6-mqtt-keepalive |
| 코드 | HEAD d777ab7d8ef034d66a862343231268cd1476af24 + 기존 C2 제품 변경 + 이번 시험 변경, 미커밋 |
| 일시 | 2026-10-20 10:59:09.704~10:59:17.712 KST; 원본 UTC +09:00 |
| 환경 | Windows 10.0.26200, SDK 10.0.401, PowerShell 7.6.5, MQTTnet 5.2.0.1603, xUnit 2.9.3 |
| 이번 변경 | ConsoleShutdownReasonTests.cs에 7건·수명주기/구독 정리 단언. FakeMqttClient.cs에 Disconnect 진입 게이트·콜백 캡처·구독 수 조회 |
| 실제/대역 | ConsoleRunner·MqttConnection·구독·큐·구조화 로그는 실제 코드, IMqttClient는 Fake. 외부 Broker·프록시·GUI·Seq는 사용하지 않음 |
| 설정·입력 | 기본 MQTT 5/CleanStart/SessionExpiry0/KeepAlive5초, 연결/해제3초. 업무 메시지 없음. 각 Client ID는 TRX stdout에 기록 |

[환경](environment.json) · [명령·시각·종료 코드](commands.jsonl) · [최종 소스 72개](source-version.json) · [소스/바이너리 대조](source-comparison.json).

## 3. 입력·예상·실제

| 시험 | 제어한 순서와 예상 | 실제·판정 |
| --- | --- | --- |
| 중복 단절 2건 | 구독 승인 대기/수신 대기에서 첫 ConnectionLost 기록 중 정지 → 같은 알림 8개 완료 → 사용자 취소 → 첫 콜백 재개. 첫 콜백 전에는 종료 대기 유지 | 두 경우 모두 ConnectionLost/ShutdownRequested/ExitCode 각 1회, 최초 단절·종료 1, Dispose 1회. 통과 |
| 명시적 종료 중 알림 1건 | 사용자 취소 → 정상 DisconnectAsync 반환을 게이트로 지연 → 정상 사유·비정상 사유 알림 → 해제 완료. 중간 알림은 최초 원인을 덮어쓰지 않음 | 대기 중 Dispose 0회, 완료 후 사용자 취소·종료 0, ConnectionLost 0회, Disconnect/Dispose 각 1회. 통과 |
| 종료 후 지연 콜백 4건 | 구독 전/후 × 사용자/단절 우선으로 종료 → **구독 해제 전에 캡처한 콜백**을 두 번 직접 호출 → 이벤트 발행 → 취소. 빈 이벤트 목록에만 발행하는 시험과 구분 | 출력·구조화 이벤트 배열 동일, 재접속·추가 해제 없음, 최종 코드 0/1 유지. 통과 |
| 앞선 순서·진행 중 콜백·동시 시작 | 기존 8개 순서 조합, 최초 접속 취소, 콜백 중 취소, 동시 신호 32회 반복, 안전 진단 3건 | 기존 14건 + 신규 7건 = 종료 원인 클래스 21건 통과. 32회는 시험 1건이며 전수 스케줄 증명이 아님 |
| A/B 관련 회귀 | MqttConnection 정상 수명주기, 제한·취소·늦은 접속 성공/실패, 입력/초기 실패 분류 | 정상 연결 6건·연결 실패 7건·콘솔 실패 12건 통과. 위 종료 원인 21건과 합쳐 직접 관련 46건 |
| 전체 회귀 | 공유 시험 대역 변경의 영향을 한 번 검사 | 기존 761 + 신규 7 = 768건, 실패/건너뜀 0 |

새 시험 모두 QueueStopped·SubscriptionStopped·Disposed 각 1회, Client.IsConnected=false, 메시지/단절 이벤트 구독 0, stderr 빈 값과 완료 Task를 확인한다. TaskCompletionSource/ManualResetEventSlim으로 순서를 고정하며 무작위 sleep을 사용하지 않는다. [검증 JSON](verification.json)의 7건에는 입력 순서·Client ID·원본 콘솔 출력이 있고 [TRX](tests.trx)는 전체 768건의 결과다.

```powershell
dotnet build AmrControlSystem.slnx --no-restore --verbosity minimal
dotnet test AmrControlSystem.slnx --no-build --no-restore --logger "trx;LogFileName=tests.trx" --results-directory evidence/COM-03/<새-실행ID>
```

[build.log](build.log) · [test.log](test.log). 새 실행은 기존 증거를 덮어쓰지 않는 ID를 사용한다.

## 4. 설계 판단과 검토 근거

현재 `Interlocked.CompareExchange`는 프로세스 내부에서 최초 종료 요청자 한 명을 선택한다. 승자가 이유·출력·종료 코드를 기록한 뒤 취소하고, 나중 요청은 취소를 다시 실행하지 않는다. 정리는 RunAsync의 단일 finally가 맡고 구독 해제 뒤 Dispose한다. 이를 이번 게이트·캡처 시험으로 확인했으므로 추가 제품 상태 머신을 만들 필요는 발견하지 못했다.

대안으로 lock을 통한 직렬화나 종료 이벤트 큐를 둘 수 있다. lock은 임계 구간을 읽기 쉽지만 느린 콜백과 함께 잡으면 경쟁 요청을 막고, 큐는 여러 세션의 이벤트를 다루기 쉬운 대신 버퍼·소비자 수명·종료 조정이 추가된다. 이번 방식은 세션별 원인 정수·CTS로 충분하다. 성능 비교 벤치마크는 하지 않았다. 자동 재접속·여러 세대 세션을 지원하면 세션 식별/소유권과 늦은 콜백 차단을 다시 설계해야 한다.

예상 PR 질문과 답변:

1. **구독을 해제하면 늦은 알림도 사라지는가?** 이미 캡처된 콜백은 실행될 수 있다. 종료 뒤 캡처 delegate를 직접 호출해 최초 원인 가드가 Dispose된 CTS/큐를 다시 건드리지 않는지 검증했다.
2. **취소 요청만 확인한 것인가?** 반환 Task·종료 코드와 정리 이벤트/Dispose/구독 수를 함께 단언했다. 실제 소켓 종료 근거는 별도의 C2·C3b 기록이다.
3. **실제 경합의 모든 순서를 보장하는가?** 선택한 임계 순서와 기존 32회 동시 시작을 검증했다. OS/네트워크의 모든 스케줄·부하·블로킹 출력의 무한 지연까지 증명하지 않는다.

## 5. 실패 이력·남은 범위

- [unit-01](../20261020-c4-unit-01/README.md)은 시험 출력 디렉터리의 샌드박스 쓰기 거부로 빌드 실패, 시험 미실행이다. 코드 변경 없이 승인된 환경의 unit-02에서 통과했다.
- 제품 소스는 C2·C3b와 동일하다. C3b 바이너리 4개도 동일하고 그 후 변경은 시험 두 파일이다. 실제 MQTT 재시험은 하지 않았다. C1 이후 단절 진단 추가 영향은 C2 복구의 12초/PING 및 C3a 기본 모드의 정상 종료 회귀로 연결했다.
- 대역 거부는 실제 인증 Broker 거부 시험이 아니다. 실제 Seq 새 단절 필드 수집, 장시간/부하/시각 변경, 자동 재접속·재구독, AMR Heartbeat·GUI·Core·DB는 이번 범위 밖/미검증이다.
- C4 검증은 통과했다. #6 전체는 실제 PR 게시 연결·리뷰·병합·종료가 남아 있으며 [최종 조건표](../20261020-ac-summary-01/README.md)에서 구분한다.
