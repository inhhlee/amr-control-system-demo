# PR #27 P2 — 종료 원인 분리 검증

- 기준: Issue #26, PR #27의 ConsoleRunner.cs 종료 원인 리뷰, 사용자 후속 수정 요청. 과제계획 v0.36의 LOG 범위와 COM-03 기존 출력·종료 계약을 유지한다.
- 시험 코드: 기준 eb593e41bc4bcb161e8b62da2d161227f299a9a9 + 이번 수정. 커밋 전 소스는 [source-version.json](source-version.json)으로 식별하며 게시 커밋은 PR에 연결한다.
- 환경: 2026-10-20 KST, Windows, .NET SDK 10.0.401/net10.0. 기존 패키지 유지. FakeMqttClient와 Serilog Capture sink를 사용한 자동시험이다.
- 명령: `dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-restore --logger 'trx;LogFileName=tests.trx' --results-directory evidence/LOG/20261020-p2-green-01`

## 입력·예상·실제

| 입력 | 예상 | 실제·판정 |
| --- | --- | --- |
| 최초 연결 대기 중 사용자 취소 | ConnectCanceled / UserCanceled, 종료0 | 통과 |
| 구독 대기·구독 완료 이후 사용자 취소 또는 연결 단절 | 각각 ShutdownRequested / UserCanceled·0 또는 ConnectionLost·1 | 4건 통과 |
| 각 단계에서 사용자→단절, 단절→사용자 | 먼저 접수한 원인·종료 코드 유지 | 4건 통과 |
| 단절 콜백이 ConnectionLost 기록 중 대기할 때 사용자 취소 | 후행 취소가 실행을 조기 종료하지 않음. 단절 원인과 종료1 유지 | 통과 |
| 동시 취소·단절, 시작 gate를 공유하는 32회 실행 | 승자의 원인과 ExitCode, ConnectionLost 이벤트 수가 일치 | 통과. 모든 가능한 스케줄을 증명하는 시험은 아님 |
| 전체 자동시험 | 기존580 + 신규11, stdout/stderr·자원 정리 유지 | 591 통과, 실패/건너뜀0, 컴파일 경고·오류0 |

[실행 로그](test.log) · [TRX](tests.trx). 신규 시험은 원인·종료 코드·단절 이벤트 수·콘솔 출력·Client Dispose 1회·메시지 핸들러 해제를 확인한다.

## 수정 전 실패와 수정 후 결과

- 최초 시험 작성 시 MQTTnet.Packets using 누락으로 빌드 실패했다. [원본 로그](../20261020-p2-red-01/initial-build-failure.log)를 보존했다.
- red-01은 동기화 TextWriter 래퍼의 ToString을 내용으로 비교한 시험 코드 오류도 포함했다. 원본 StringWriter를 보유하도록 시험을 바로잡았으며 제품의 기대값을 완화하지 않았다.
- [red-02](../20261020-p2-red-02/test.log)는 제품 수정 전 새 시험 11건 중 6실패·5통과다. 단절을 UserCanceled로 기록하는 오류, 동시 신호의 원인·종료 코드 불일치와 후행 취소에 의한 조기 정리를 검출했다.
- 이후 제품 수정 후 전체591건을 실행했다. 이번에는 실제 Seq/MQTT·브로커 중단·네트워크 장비 장애 시험을 재실행하지 않았으며 서비스를 조작하지 않았다. 이전 실제 연동 결과는 [기존 검증](../20261020-seq-unit-03/README.md)이다.

## 선택 이유·대안·리뷰 질문

종료 요청과 원인을 하나의 원자적 선택으로 연결한다. CompareExchange에서 최초로 None을 바꾼 요청만 큐 중지·종료 코드 기록·Cancel을 수행한다. 늦은 요청은 아무 정리도 앞당기지 않는다. 단절 처리에서 원인과 종료 코드를 정한 뒤 대기 작업을 깨운다.

- 왜 종료 시 사용자 토큰만 보지 않는가? 단절 후 늦은 사용자 취소가 들어오면 이미 결정한 원인이 바뀌기 때문이다.
- 왜 lock 대신 CompareExchange인가? 필요한 상태는 한 번 정해지는 원인 값 하나다. 콜백·출력·Cancel 중 잠금을 유지할 필요가 없다. 여러 단계의 가변 종료 상태가 추가되면 별도 상태 머신을 검토한다.
- 동시에 도착하면 어느 쪽이 우선인가? 프로세스가 원자적으로 먼저 접수한 신호다. 물리적인 네트워크 단절 시각의 우선순위를 보장하지 않는다. 정리 자체가 실패하면 기존 계약대로 종료 코드는1이 될 수 있다.

리뷰 범위는 이 P2 수정과 영향받는 취소·단절 경로다. 전송 큐·HTTP·Seq 정책은 변경하지 않았다. 병합·Issue 종료는 별도 단계다.
