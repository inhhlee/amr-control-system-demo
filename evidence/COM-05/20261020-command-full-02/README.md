# COM05-A / Issue #29 구현·검증

## 1. 목표와 적용 기준

- 사용자 요청: 관련 Issue 확인·신규 생성, develop 기반 feature 브랜치, MOVE·STOP·RESET 발행·입력 검증 구현 및 시험까지.
- [Issue #29](https://github.com/inhhlee/amr-control-system-demo/issues/29), `feature/29-mqtt-command-publish`. 선행 #23은 PR #24로 반영 완료.
- 공통데이터명세 v0.4 / DN-08 Command 계약 v1, 기능명세 v0.7 COM-05, 검토 및 결정사항 v0.7 D-06·D-07을 적용했다. 기존 계약 출처는 문서 저장소가 참조하는 C:\practice의 Command/CONTRACT.md이며 제품 소스는 이 저장소에 새로 작성했다.
- 과제계획 v0.39에 과제1 보류가 있었으나 현재 사용자의 COM05-A 구현 요청을 이번 작업의 재개 지시로 적용했다. 다른 보류 기능·과제2 일정 변경을 자동 수행하지 않는다.
- 실제 AMR 동작·ACK 대조(COM05-B)·자동 재접속·재시도·운영 전달 보장은 제외한다.

## 2. 실행 환경과 코드

| 항목 | 값·근거 |
| --- | --- |
| 기준 커밋 | develop `64df6f0e639c7c9fbb1c474711940ab2eeabb287` + 미커밋 #29 변경 |
| 위치·브랜치 | `C:\amr-control-system-demo`, `feature/29-mqtt-command-publish` |
| 자동시험 | 2026-10-20 01:40:47~01:40:54 KST, [TRX](tests.trx) |
| 실제 MQTT·Seq | 2026-10-20 01:43:12~01:43:31 KST, [환경](../20261020-command-mqtt-03/environment.json) |
| 환경 | Windows 10.0.26200, .NET SDK 10.0.401 / net10.0, MQTTnet 5.2.0.1603 |
| Broker | 기존 `amr-demo-mosquitto-1`, `eclipse-mosquitto:2.1.2-alpine`, 127.0.0.1:1884 |
| Seq | 기존 `amr-seq`, 127.0.0.1:5341, 2026.1.17114 |
| 코드 식별 | [자동시험 시점 소스 해시](source-version.json), [실제 시험 최종 해시](../20261020-command-mqtt-03/source-version.json) |

자동시험 이후 제품 C# 소스·C# 시험 변경은 없다. 실제 시험 PowerShell 도구의 출력 버퍼링·Seq 배열 집계는 별도로 수정했으며 최종 실행 03의 해시를 따른다. 기존 AGENTS.md 변경은 그대로 보존했다(SHA-256 `280B2FEFF14A7EE8098982A7FD23C3FC0FD3F900CF871766A2ED9626ABA1F0FF`). 서비스 시작·중단·설정 변경 없이 재사용했다.

## 3. 입력·절차·예상 결과

| 시험 | 입력·절차 | 예상 |
| --- | --- | --- |
| 전체 자동 회귀 | `dotnet test AmrControlSystem.slnx --no-restore --logger "trx;LogFileName=tests.trx" --results-directory evidence/COM-05/20261020-command-full-02` | 기존 수신/큐/연결/로그와 명령 신규 시험 모두 통과 |
| 형식 경계 | 필수 누락·추가·중복, 잘못된 Topic/ID/Unicode/UTF-8/JSON/자료형·정수 표기·범위·깊이·크기, MOVE 좌표/STOP·RESET param | 고정 오류 코드·필드로 거부, MQTT 발행 호출 0 |
| 발행 수명주기 | 미접속·전송 예외·거절 반환·이미 취소·발행 중 취소·시간 초과·취소 무시 후 늦은 반환/실패 | 실패 원인 보존, 대기 종료, 재시도 없음·늦은 결과 관찰 |
| 보존·메모리 경계 | 발행 대기 후 원본 byte[] 변경 | 실제 Payload는 검사된 복사본이며 cmdId/Topic/QoS/Retain 보존 |
| 콘솔 | 세 명령 각 2회, 잘못된 옵션·NaN·중복 옵션, 접속/발행 실패·취소·시간 초과·해제 실패 | 요청별 ID 생성, 입력 오류 때 Client 생성 0, 소유 Client 정리, 성공/실패/취소 구분 |
| 실제 MQTT·Seq | `pwsh -NoProfile -File tests/manual/COM05-A/Verify-Commands.ps1 -RunId 20261020-command-mqtt-03` | SUBACK 후 세 Topic으로 발행, 요청/수신 ID와 필드 일치·불량 추가 발행 없음·Retain 없음·Seq 검색 일치 |

실제 입력은 MOVE `x=-30.5, y=15.25`, STOP/RESET `{}`이며 실행 ID별 독립 AMR ID를 사용했다. [요청과 실제 수신 Payload](../20261020-command-mqtt-03/verification.json)에 ID·Topic·시각을 함께 보존했다. 수신자는 Mosquitto CLI 대역이며 실제 AMR이 아니다.

## 4. 실제 결과와 판정

| 항목 | 결과 | 판정·증거 |
| --- | --- | --- |
| 빌드 | .NET 빌드 경고 0·오류 0, 전체 시험 실행의 빌드도 경고 없이 완료 | 통과; 실행 명령 출력 직접 확인 |
| 전체 자동시험 | **758/758 통과, 실패 0, 건너뜀 0**. 기존 591 + 신규 167 | 통과; [TRX](tests.trx) |
| 실제 발행 | MOVE·STOP·RESET 각각 종료 0, 서로 다른 cmdId, 대상·형식·좌표·시각 일치 | 통과; [수신 로그](../20261020-command-mqtt-03/subscriber.stdout.log)·[판정 JSON](../20261020-command-mqtt-03/verification.json) |
| 불량 입력 | NaN MOVE 종료 1, 요청/발행 출력 없음, 실제 수신 추가 0 | 통과; [오류 출력](../20261020-command-mqtt-03/invalid.stderr.log) |
| Retain | 최초 수신 qos=0/retain=0, 새 구독자의 이전 명령 수신 0 | 통과; [늦은 구독](../20261020-command-mqtt-03/late-subscriber.stdout.log)·[제한 종료](../20261020-command-mqtt-03/late-subscriber.stderr.log) |
| Seq | 발행 이벤트 3, 각 AmrId+CommandId 검색에서 요청/발행 2건 | 통과; [Seq 이벤트](../20261020-command-mqtt-03/seq-events.json)·시험 도구의 서버 필터 검사 |
| 기존 AGENTS·Git | 사용자 AGENTS 해시 동일, develop 기반 feature 브랜치 | 보존; 커밋·푸시·PR·병합·Issue 종료 미수행 |

## 5. 실패 이력·설계 검토

- [unit-01](../20261020-command-unit-01/tests.trx): 168 중 166 통과·2 실패. xUnit Theory 직렬화가 짝 없는 surrogate를 대체 문자로 변경했다. 기대를 완화하지 않고 Fact 내부에서 해당 문자를 구성해 검증·무발행을 확인했다. 공유 Theory 2건을 Fact 1건으로 대체하여 신규 시험은 167건이다.
- [mqtt-01](../20261020-command-mqtt-01/failure.log): 구독 준비 출력이 파이프에서 버퍼링되어 SUBACK 대기 제한에 도달했다. 제품 명령 발행 전 실패. Docker exec PTY로 준비 출력 flush를 확보했다.
- [mqtt-02](../20261020-command-mqtt-02/failure.log): 실제 세 명령/불량/Retain 검사는 통과했으나 PowerShell `@(Invoke-RestMethod ...)`가 API 배열을 한 항목으로 감싸 최종 Seq 개수 검사가 실패했다. 직접 대입 후 배열 개수 검사로 수정했다. 원본 로그·실패는 보존하고 mqtt-03에서 재실행했다.
- 샌드박스의 최초 빌드는 오류 설명 없이 종료 1, Docker 소켓은 접근 거부였다. 권한 있는 실행 환경에서 동일 도구로 빌드·시험·서비스 조회를 수행했다. 이 환경 실패를 제품 결함이나 제품 통과로 집계하지 않는다.

**선택 이유·대안:** 순수 Validator를 공유하여 발행 전과 시험 수신에서 같은 계약을 검사한다. Publisher는 연결 소유권을 갖지 않고 단일 발행만 담당하며, 콘솔이 생성·접속·해제·정리를 맡는다. Raw byte 경계를 유지하면 중복 필드와 UTF-8도 검증할 수 있다. 타입 모델만 받는 API보다 유연하지만 직렬화와 검증 비용이 추가된다. 버퍼 복사는 최대 64KiB의 비용으로 검증 후 호출자 변경을 격리한다. 별도 송신 큐·백그라운드 재시도는 아직 명령 중복·재전송 정책이 없고 한 실행 한 요청인 이번 범위에서 필요하지 않다.

**PR 예상 질문과 답변:**

1. 발행 성공인데 왜 동작 SUCCESS가 아닌가? → QoS 0의 MQTTnet 반환은 AMR 수신/실행 증거가 아니다. `Delivery=Unconfirmed Execution=NotTracked`를 표시하고 요청/ACK 연결은 COM05-B에서 검증한다.
2. 왜 시간 초과에 자동 재시도하지 않는가? → 이미 송신됐을 수 있다. 중복 수행·멱등성 정책 없이 재시도하면 명령을 두 번 수행할 수 있어 `Delivery=Unknown`으로 반환한다.
3. Client를 Publisher가 Dispose하지 않는 이유는? → 기존 연결/수신과 공유할 수 있는 자원의 소유권은 호출자에게 있다. 콘솔은 종료 시 Dispose하고 Publisher는 늦은 Task fault를 관찰한다.

## 6. 종합 결과와 남은 범위

COM05-A의 로컬 구현·자동 검증·실제 Broker 수신 검증을 완료했다. 기존 수신 모드와 Seq 선택 설정을 유지한다. 실제 AMR MOVE/STOP/RESET 수행·ACK 상관관계·자동 재접속·운영 QoS/중복 정책·MQTT Explorer GUI 실행은 검증하지 않았다. COM-05 전체와 과제1 완료를 의미하지 않는다. Issue는 open이며 커밋·푸시·PR은 다음 사용자 요청 단계다.
