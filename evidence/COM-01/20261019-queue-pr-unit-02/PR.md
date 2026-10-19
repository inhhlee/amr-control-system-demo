# PR 본문 초안

제목: `fix: AMR 수신 큐 종료 경합과 검증 보완 (#23)`

아래 본문은 게시 전 초안이다. 커밋·푸시·PR 생성은 수행하지 않았다. 게시 시 최종 커밋 SHA와 저장소의 증거 링크를 연결한다.

## 관련 Issue

- 관련 Issue: #23
- 대상 브랜치(base): develop
- 작업 브랜치: feature/23-amr-receive-queue
- 과제·기능 ID: 과제1 / COM-01 / COM01-C
- 명세·계획 근거: 기능명세 v0.7 COM-01, 요구사항정의 v0.5 R-04, 공통규칙·공통데이터명세 v0.4, 검토 및 결정사항 v0.5, 개발용 수신 큐 계약 DN-09, 과제계획 v0.31. 원본 공유 URL은 없으며 필요한 계약은 Issue #23에 기술되어 있다.

## 변경 내용

- 메시지를 큐에서 꺼낸 직후 종료하면, 기존 구현은 이미 취소된 토큰으로 처리자를 새로 호출할 수 있었다. 호출 진입과 취소를 동기화하고 호출 전에 종료를 확인한 메시지는 `ShutdownDiscarded`에 포함한다.
- 접수 잠금과 호출 진입 잠금을 분리했다. 서로 다른 AMR의 동기 호출은 함께 진입하고 반환된 Task는 잠금 밖에서 기다린다. 진행 중 동기 호출이 Task를 반환한 뒤 취소를 전달하므로 처리자의 동기 구간은 짧게 반환해야 한다.
- 호출 직전 종료·동기 구간 지연·5,000회 접수/종료 경합·실제 수신부의 작은 큐/AMR 제한 시험 4건을 추가했다. 전용 Broker 중단 재현 도구와 README·실행 증거를 추가했다.
- AMR별 큐·콘솔 연결의 기본 구현은 이미 base의 `1060c6450f19d42e3fea0cb5d858500a8ab6aa93`에 포함되어 있다. 이번 PR 차이는 종료 경합 보완과 시험·안내·증거다. 실제 Core 업무·Command 발행·자동 재접속은 범위 밖이다.

## 실제 시험 결과

- 시험한 코드 버전: `1060c6450f19d42e3fea0cb5d858500a8ab6aa93` + 미커밋 종료 수정·시험. 실행별 `source-version.json`으로 시험 소스를 식별한다. MQTT 정상/중단 실행 당시 소스와 최종 소스 비교는 각각 불일치 0건이다.
- 실행 일시·환경: 2026-10-19 22:24~22:27 KST, Windows 10.0.26200, .NET SDK 10.0.401, MQTTnet 5.2.0.1603, Docker Client/Server 24.0.6, Mosquitto 2.1.2, MQTT 5·QoS 0·Retain=false.
- 실행 위치: 코드 저장소 루트. 아래 증거 경로는 저장소 기준이다.
- 시험 전용 amr-demo Broker를 생성해 127.0.0.1:1884에서 사용했다. 시험 뒤 전용 컨테이너·네트워크를 제거했고 기존 amr-mosquitto·amr-seq와 바인드 데이터는 보존했다.

| 구분 | 명령·절차와 입력·조건 | 예상 결과 | 실제 결과 | 판정 | 증거 링크·경로 |
| --- | --- | --- | --- | --- | --- |
| 복원·빌드 | `dotnet restore AmrControlSystem.slnx`, `dotnet build AmrControlSystem.slnx --no-restore --verbosity minimal` | 성공, 경고·오류 0 | 종료 0, 경고·오류 0 | 통과 | `evidence/COM-01/20261019-queue-pr-unit-02/{restore,build}.log` |
| 자동 회귀·경계 | `dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-build --no-restore`; 기존 543 + 신규 4 | 전부 통과; 이미 취소된 토큰으로 새 호출 0 | 547 통과, 실패·건너뜀 0. 호출 전 종료 시 처리 0·폐기 1, 경합 5,000회 위반 0 | 통과 | `evidence/COM-01/20261019-queue-pr-unit-02/{test.log,tests.trx,README.md}` |
| 실제 MQTT 정상·종료 | `pwsh -NoProfile -File tests/manual/COM01-C/Verify-Mqtt.ps1 -RunId 20261019-queue-pr-mqtt-01`; 두 AMR·네 유형·세 세션·15건 | 순서·독립 처리·모델/수신 시각 보존, 취소·폐기와 새 세션 정리 | 정상 10·거부 1·취소 1·폐기 3, 모든 세션 종료 0 | 통과 | `evidence/COM-01/20261019-queue-pr-mqtt-01/README.md` |
| 실제 Broker 중단 | `pwsh -NoProfile -File tests/manual/COM01-C/Verify-Disconnect.ps1 -RunId 20261019-queue-pr-disconnect-03 -AllowBrokerStop`; 001 처리 중 전용 Broker 중단, 입력 5건 | 중단 통지, 취소·폐기·정리, 오류 종료 1 | 정상 1·취소 1·폐기 3, ConnectionLost·SubscriptionStopped·Disposed, 종료 1, 중단 후 새 호출/전달 없음 | 통과 | `evidence/COM-01/20261019-queue-pr-disconnect-03/README.md` |

초기 복원 시도의 빈 로그와 끊김 재현 도구의 초기 null 처리 오류 두 번은 별도 실행 ID `20261019-queue-pr-unit-01`, `20261019-queue-pr-disconnect-01`, `20261019-queue-pr-disconnect-02`에 보존했다. 해당 실행을 통과로 집계하지 않았다.

## 완료조건

아래는 로컬 검증 판정이며 GitHub Issue의 체크 상태는 변경하지 않았다.

- [x] 같은 AMR의 네 유형이 접수 순서대로 처리되고 다른 AMR은 독립적으로 진행된다. 대상·모델·최초 수신 시각과 id 없는 ACK의 값이 보존된다. — 자동 회귀·실제 MQTT 세 세션.
- [x] 큐/AMR 한계 초과 시 MQTT 수신부가 빈자리를 기다리지 않고 새 입력을 거부한다. QueueFull·AmrLimitReached를 입력 오류와 구분해 기록한다. — 기존 용량 시험·신규 MessageSubscription + Fake MQTT 시험(대기 2·AMR 2).
- [x] 처리자 예외를 HandlerFailed로 기록한 뒤 다음 입력을 처리한다. 취소 후 새 전달은 시작하지 않으며 잔여 폐기 수량을 기록한다. — 기존 실패 회귀·신규 호출 경계/반복 경합·실제 MQTT 취소/중단 시험.
- [x] 종료 대기 한계를 넘으면 오류와 정리 결과를 알리고 늦은 완료·실패를 관찰한다. 정상 종료·연결 끊김·새 세션에서 이전 큐가 재사용되지 않는다. — 기존 늦은 성공/실패·신규 동기 구간 제한·실제 정상/중단/새 세션 시험.
- [ ] 기존 검사·수신부 회귀와 실제 MQTT 연결 결과를 확인하고, 실제 변경·확인 결과·미검증 범위를 README와 Issue·PR에 연결한다. — 로컬 결과·README·본문 초안 준비, 외부 게시와 최종 SHA 연결 대기.

## 미검증 항목과 남은 작업

| 항목 | 미검증·미충족 사유 또는 해당 없음 근거 | 영향 | 후속 확인 방법·관련 Issue |
| --- | --- | --- | --- |
| 실제 MQTT 과부하·장시간 부하 | 작은 제한은 Fake MQTT와 실제 수신부로 확인; 실제 Broker 부하 시험 미실행 | 운영 처리량·장시간 안정성 보장 안 함 | 필요 시 별도 실행 ID·부하 조건으로 확인 |
| 네트워크 무응답·KeepAlive 상세 | 이번 실제 단절은 Broker 정상 중단으로 재현 | 모든 네트워크 장애 형태 검증 아님 | 후속 장애 시험 범위를 정해 확인 |
| 처리자 동기 구간 | Task 반환까지 취소 전달을 기다림; 토큰 취소를 동기로 기다리는 처리자는 지원하지 않음 | 동기 구간이 멈추면 종료 제한 오류, 늦은 반환 후 정리 | 현재 짧은 동기 구간 + 비동기 대기 규칙 준수. 제한/늦은 정리는 자동시험으로 확인 |
| 게시·리뷰·통합 | 이번 요청은 브랜치 생성·보완·시험까지 | Issue 전체 완료와 develop 반영은 미완료 | #23 변경 커밋·푸시, develop 대상 PR 게시, 리뷰·병합·Issue 종료는 요청된 단계에서 진행 |
