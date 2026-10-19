# COM01-C AMR별 순서·독립성 검증결과

## 1. 목표와 적용 기준

- 실행자: Codex / 실행 ID: 20261019-queue-order-02 / 2026-10-19.
- [Issue #23](https://github.com/inhhlee/amr-control-system-demo/issues/23)의 첫 단계: AMR마다 네 유형을 한 줄로 처리하고 다른 AMR은 독립 진행한다.
- 기능명세 v0.7 COM-01, 요구사항정의 v0.5 R-04, 공통규칙·공통데이터명세 v0.4, 결정사항 v0.5, 개발용 DN-09를 적용한다. 새 스키마·실제 Core 업무는 추가하지 않는다.
- 코드 기준: b8af05738d08b76db84f2b020789539c8062ad07 + feature/23-amr-receive-queue의 미커밋 변경. 소스·프로젝트·SDK 정의는 [SHA256 목록](source-hashes.json)으로 식별한다. 별도 spec_revision 추적기는 이 저장소에 없다.

## 2. 실행 환경

- 실제 SDK·OS·시작 시각: [environment.json](environment.json). 종료 UTC: [finished-at.txt](finished-at.txt).
- .NET SDK 10.0.401, net10.0, MQTTnet 5.2.0.1603. 추가 패키지 없음.
- 실행 위치: C:\amr-control-system-demo. 기존 MessageSubscription·네 Validator와 FakeMqttClient 사용. Broker·컨테이너·서비스 실행 없음.
- 실제 MQTT·사용자 직접 조작·브라우저 화면 시험이 아닌 자동시험이다.

## 3. 입력과 절차

원본 입력과 제어 신호는 [AmrReceiveQueueTests.cs](../../../tests/Amr.Communication.Tests/AmrReceiveQueueTests.cs)에 있다. 일정 시간을 자고 성공으로 추정하지 않고 TaskCompletionSource로 처리 시작·해제·완료 시점을 확인한다. 10초는 시험이 잘못됐을 때의 안전 대기 한계이며 제품 종료 제한이 아니다.

```powershell
dotnet build AmrControlSystem.slnx --no-restore --verbosity minimal
dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-build --no-restore
# 첫 단계 9건만 확인
dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~AmrReceiveQueueTests
```

재실행 증거는 새 폴더에 `--logger "trx;LogFileName=tests.trx" --results-directory <새 실행 경로>`로 저장한다.

## 4. 실제 결과와 판정

| 입력·조건 | 예상·실제 결과 | 판정 |
| --- | --- | --- |
| 불량 Heartbeat 후 정상 4유형, 001 첫 처리 대기 중 002 ACK | 불량 1건만 검사 거부. 002 완료 당시 001은 첫 건만 시작. 해제 후 heartbeat → telemetry → alarm → ack 순서 | 통과 |
| 같은 001의 ts 40 → 30 → 20 → 10, id 없는 FAILURE ACK | ts로 정렬하지 않음. 원래 ReceivedMessage 객체·모델·수신 UTC 유지, null Id·FAILURE·reason 보존 | 통과 |
| 처리자의 동기 구간 대기 | 접수 함수가 처리자 완료를 기다리지 않음. 다른 AMR 처리 진행 | 통과 |
| 같은 AMR에 8개 생산자가 각 10건 접수 | 80건 중복·누락 없음, 처리 동시 실행 0, 생산자별 접수 순서 유지 | 통과 |
| 대소문자·Unicode 조합 형태가 다른 네 AMR ID | 정규화/대소문자 통합 없이 네 줄이 각각 처리 시작 | 통과 |
| 대기 한도 2, 처리 중 1 + 대기 2 뒤 새 입력 | 새 입력 QueueFull, 기존 1·2·3만 순서대로 처리 | 통과 |
| AMR 한도 1, 기존 큐가 빈 뒤 새 AMR | 새 AMR만 AmrLimitReached, 기존 AMR 접수 유지 | 통과 |
| 동기 throw / await 뒤 throw | 다음 메시지 처리 지속. 두 경우를 별도 시험 | 통과 |
| CompleteAsync 뒤 접수 | NotAccepting, 이미 접수한 2건은 모두 처리하고 같은 완료 Task 반환 | 통과 |

- [빌드 로그](build.log): 경고 0·오류 0.
- [시험 로그](test.log)·[TRX](tests.trx): **538건 통과·실패 0·건너뜀 0**. 기존 529건 + 새 9건.
- 실행 01의 경고는 시험 단언 표현을 바꿔 해결했다. [이전 실행](../20261019-queue-order-01/README.md)을 보존했다.

## 5. 문제와 후속 조치

- AmrReceiveQueue는 아직 internal이다. 기존 콘솔·연결·구독 구현을 바꾸지 않았고 수신부와의 연결은 Fake 자동시험에서만 확인했다.
- CompleteAsync는 접수된 작업을 끝까지 처리하는 시험 정리 수단이다. DN-09의 연결 종료(취소·잔여 폐기·제한 시간)를 구현한 것으로 보지 않는다. 처리자가 끝나지 않으면 기다리므로 실제 종료 경로에서 사용하지 않는다.
- 기본 용량/AMR 제한과 HandlerFailed Trace 출력은 최소 기반이다. 자원 초과 로그·처리 로그 주입·로그 출력 자체 오류·취소/종료 경합·늦은 완료/실패 관찰은 다음 단계에서 구현·검증한다.
- 실제 MQTT 두 AMR·네 유형 큐 전달, Ctrl+C·연결 끊김·새 세션 정리는 미검증이다. CPU를 계속 점유하는 처리자에 대한 운영 성능·전용 스레드 격리는 보장하지 않는다.

## 6. 종합 결과

첫 단계의 자동시험 통과. #23 전체 완료조건은 아직 완료 처리하지 않는다. 커밋·푸시·PR 생성·제품 통합 완료는 수행하지 않았다. 다음 단계는 과부하 기록과 취소·폐기·종료 한계이며, 해당 경계를 갖춘 뒤 콘솔의 실제 수신 경로에 연결한다.
