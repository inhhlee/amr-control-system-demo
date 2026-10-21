# PR #39 명세 대조 리뷰·접속 경계 수정·재검증

## 1. 목표와 적용 기준

- 요청: PR #39를 기존 명세·시험 근거로 리뷰하고 결함을 수정·재검증한다. 병합은 사용자 결과 확인 뒤 별도 요청 단계다.
- 프로젝트·기능: 과제2 AMR-05 일부 / Issue #38. Simulator → Broker → 기존 관제 수신 콘솔, 단일 AMR의 Telemetry·Heartbeat만 검토한다.
- 리뷰 기준 코드: `68c66d53cd168ecbbea3248c8f3b8182211bff60`, base develop `2ccdd2511fed8a146f8338250cbaa813198c0fca`. 실행 코드는 이 HEAD에 아래 미커밋 수정·회귀시험을 더한 상태다. GitHub의 기존 커밋 자체가 수정 후 시험을 통과했다고 표시하지 않는다.
- 원본 기준: `C:\process-plan\관제시스템 과제 진행\과제2\과제계획.md` v0.15 초안의 `mqtt-map-next-deliverable`, 기능명세 v0.12 초안 AMR-05, 공통데이터명세 v0.9 초안 2·3절 및 현재 속도 계약, 공통규칙 v0.9 초안 C-01·03·05, 검토 및 결정사항 v0.12 초안. Issue 게시 때의 과제2 v0.14보다 최신인 v0.15는 #38 분리 결과·근거를 추가한 버전이며 이번 송신 계약을 변경하지 않는다.
- 원문 R-02·R-04와 전체 AMR-05의 Alarm·ACK·재접속 요구는 유지하되, 이 PR에 후속 기능을 추가하지 않는다. 격자·지도·명령/ACK·Core/DB는 별도 범위다.
- 검토: 변경 제품9파일, MQTT 자동시험, 수동 검증·Ctrl+C 도구, 안내와 기존 시험 근거. 스냅샷 공유, 최초 종료 원인, 이동/송신 작업의 시작·취소·합류, 연결·이벤트·Client의 수명을 확인했다. 코드 추적기의 별도 spec_revision/verification_index 파일은 이 작업에 없으므로 이 문서와 integrity.json을 실행 색인으로 사용한다.

## 2. 확인된 결함과 수정

**[P2] 접속 반환 직전 발생한 단절 알림을 세션이 무시함 — 로컬 수정 완료.**

- 기존 위치: `src/Amr.Simulator/Telemetry/MqttSimulationSession.cs`의 `Lost()`가 `connected` 값이 true일 때만 단절을 접수했다. 이 값은 `await transport.ConnectAsync(...)`가 반환한 다음 설정되었다.
- 발생 조건: 실제로 연결되었던 Client의 단절 알림이 ConnectAsync 반환 직전에 도착한다. 해당 알림은 무시되고 다음 발행에서 일반 예외가 발생한다.
- 영향: 이미 연결된 뒤의 단절을 `ConnectionLost` 대신 `IOException`/일반 접속 예외로 기록하여 Issue #38의 최초 실패·연결 후 단절 구분을 어긴다. 이번 재현에서 종료 코드는 1이었으며 정상 도착으로 오인된 결과는 관찰하지 않았다.
- 수정: 전송 객체가 MQTTnet의 `ClientWasConnected`로 실제 연결의 단절만 `ConnectionLost`로 통지한다. 세션은 이 알림을 즉시 최초 종료 원인으로 접수하고, 접속 반환 뒤 취소 상태를 확인해 연결 성공 로그·발행·이동을 시작하지 않는다. 기존 Interlocked 최초 원인 보존과 finally의 작업 합류·이벤트 해제·연결 정리를 유지한다.
- 시험 경계: 기본 생성자는 실제 Client를 만들고, 추가 생성자는 전달받은 IMqttClient의 수명도 소유한다. 실제 전송 객체와 기존 MqttConnection을 통과하는 고정 순서 대역으로 접속 성공 후 단절/최초 거절 알림을 각각 시험한다. 새 패키지·재시도·프로젝트는 추가하지 않았다.

수정 파일은 `Telemetry/ISimulationTransport.cs`, `MqttSimulationSession.cs`, `MqttSimulationTransport.cs`이며, 시험은 기존 `MqttSimulationTests.cs`와 새 `MqttTransportConnectionTests.cs`다. 이 검토 범위에서 추가로 재현·확인한 결함은 없다. 기존 원격 커밋에는 위 P2가 남아 있으므로 수정 게시 전 병합 가능한 최종 버전으로 판단하지 않는다.

## 3. 실행 환경·버전·보존

2026-10-22 KST, `C:\amr-control-system-demo`, Windows 10.0.26200 / SDK 10.0.401 / .NET 10.0.12 / net10.0, MQTTnet 5.2.0.1603 / xUnit 2.9.3 / Test.Sdk 17.14.1 / adapter 3.1.4. 정확한 명령·시각은 before/after JSON, 실제 Broker 이미지 ID·loopback 포트는 [실제 환경](../20261022-pr39-review-live-01/environment.json)을 따른다.

표준 testhost의 로컬 통신 제한 이력이 있어 빌드·VSTest와 Docker 시험은 일반 실행 권한으로 수행했다. 테스트 대역의 경계 재현과 실제 Broker 연결 시험을 구분한다. 실제 Broker는 기존 로컬 Mosquitto 2.1.2-alpine 이미지로 격리 생성했으며 새 설치·이미지 다운로드는 하지 않았다.

기존 README 변경과 미추적 재확인 자료72개는 정리 직후 보존 목록의 SHA256과 전부 일치한다. 기존 분리 실행의 근거49개도 원래 manifest와 원시 바이트가 일치한다. 이번 실행 결과는 새 폴더에 기록했고 과거 실패·성공 원본을 덮어쓰지 않았다. 현재 소스100개·DLL·보존 목록·시험 수치는 [integrity.json](integrity.json), 증거 해시는 [artifact-sha256.json](artifact-sha256.json)을 따른다. `before.*.cs.txt`·`after.*.cs.txt`와 `fix.patch`로 재현 당시와 수정 소스를 연결한다.

## 4. 입력·예상·실제 결과

| 시험 | 입력·예상 결과 | 실제 결과·판정 | 근거 |
| --- | --- | --- | --- |
| 수정 전 결함 재현 | Connect 반환 직전 단절 → ConnectionLost·이동/발행 없음·종료1 | 종료1이지만 Reason=IOException. 기대 사유 불일치 1건 실패, 결함 재현 | [실패 TRX](before-reproduction.trx), [로그](before-reproduction.log), before 소스 복사본 |
| 수정 후 전체 빌드 | 동일 솔루션·기존 복원 결과, 제품·시험 성공 | 경고0·오류0·종료0, 통과 | [빌드 로그](after-build.log), [명령·시각](after-build.json) |
| Simulator 전체 회귀 | 기존151 + 새3, 기존 설정·이동·실행/취소·MQTT 유지 | 154통과·실패0·skip0, 통과 | [TRX](after-simulator.trx), [명령·시각](after-simulator.json) |
| 새 세션 경계 1건 | 연결 후 단절을 Connect 반환 직전에 알림 | ConnectionLost·종료1, MqttConnected/SimulationStarted/발행 없음, 해제·Dispose 확인 | TRX의 ConnectionLossBeforeConnectReturns_IsNotMissedOrRelabeledAsPublishFailure |
| 새 실제 전송 객체 경계 2건 | MQTTnet 알림의 ClientWasConnected=true/false, Connect 완료 전 고정 발생 | true는 ConnectionLost, false는 최초 접속 MqttConnectionException. 둘 다 이동/발행 없음·종료1·Dispose1회·구독 해제 | TRX의 DisconnectNotificationDuringConnect 2행 |
| 실제 MQTT 정상 3설정 | 기본/변경/처음부터 도착, Published와 관제 Received 대조 | Telemetry30·Heartbeat7의 ID·시각·위치·State·현재 속도 일치. 기본 최종(1,2)·IDLE·0 | [실제 판정](../20261022-pr39-review-live-01/verification.json), 입력·송수신 원본 |
| 실제 MQTT 나머지 4조건 | Retain 없음, 긴 이동 대기 중 독립 송신/취소, Broker 단절, 최초 접속 실패 | 총7시나리오 모두 통과. 사용자 취소0, 단절/최초 실패1, 단절 사유·이동 미시작 확인 | 같은 실제 판정·cancel/broker-loss/connect-failure 로그, [실행 명령](after-live.json) |
| 실제 종료·환경 보존 | 소유 Broker/프로세스 종료, 기존 컨테이너 상태 유지 | OwnedBrokerRemoved·OwnedProcessesExited·ExistingContainersPreserved 모두 true | 실제 verification.json·containers-before/after.jsonl |
| 기존 통신 회귀 재사용 | Communication/관제 콘솔/통신시험58파일 해시 동일, diff0 | 기존768통과 근거 재사용. 이번 새 실행으로 합산하지 않음 | integrity.json, [기존 TRX](../20261022-mqtt-split-01/final-communication.trx) |

새3건은 154건에 포함되며 합산하지 않는다. 수정 전 실패1건과 수정 후154건도 별개 실행이다. 실제7시나리오·정상3설정의 메시지37개·자동154건을 합산하지 않는다. 접속 반환 경계는 타이밍을 고정한 대역에서 재현했으며 실제 네트워크에서 그 미세한 경합 시점을 강제로 재현했다고 표시하지 않는다. 실제 시험은 수정한 전송 객체의 정상/실패/단절 동작과 기존 회귀를 확인했다.

## 5. 설계 판단·예상 PR 질문

선택 이유: 연결의 과거 상태는 MQTTnet 알림을 가진 전송 객체가 판별하고 세션은 의미가 정리된 단절을 받는다. 세션 안에 별도의 접속 플래그를 두면 알림 순서와 플래그 갱신의 경계가 다시 생긴다. 현실적인 대안은 세션이 접속 중/접속됨/종료 상태와 보류 단절을 직접 관리하는 방식이다. 그 대안은 재접속·세션 교체에는 유리할 수 있으나 현재 한 연결에 상태와 동기화 책임을 더한다. 두 방식의 성능·메모리 벤치마크는 하지 않았다. 재접속/다중 세션이 요구되면 세션 세대 식별과 늦은 알림 격리를 포함해 다시 설계한다.

1. **플래그를 없애면 최초 접속 실패를 단절로 오인하지 않나?** 전송 객체가 ClientWasConnected=false 알림을 걸러낸다. 실제 전송 객체를 사용하는 새 true/false 2건과 실제 최초 실패/연결 후 Broker 단절로 확인했다.
2. **단절 알림 뒤 이동·송신 작업이나 이벤트 구독이 남지 않나?** 접속 경계에서는 작업을 시작하지 않으며, 실행 중에는 기존 stop 취소 → 두 작업 합류 → 이벤트 해제 → Disconnect/Dispose 순서를 유지한다. 기존 느린 발행·사용자 취소·완료 후 늦은 취소 시험을 포함한154건과 실제 정리 결과로 확인했다. 여러 연결 세대의 늦은 이벤트 격리는 이번 범위가 아니다.
3. **수정한 메시지가 관제에 도착했다고 보장하나?** QoS0·Retain=false와 Published=Unconfirmed 계약은 그대로다. 이번 실제 정상 설정의 송신/수신37개와 최종 좌표·IDLE·속도0을 직접 대조했다. 전달·중복·유실 보장이나 DB/화면 완료로 확대하지 않는다.

## 6. 종합 결과와 남은 단계

- 명세 대조 리뷰 완료, P2 결함1건 로컬 수정·재현/회귀 완료. 빌드0경고/0오류·자동154건·실제7시나리오 통과, 기존 미커밋72개 보존 확인.
- GitHub CI 조회에는 PR head 68c66d5의 check/status가 없었다. 로컬 결과를 CI 통과·타인의 승인 리뷰로 표시하지 않는다.
- 현재 로컬 HEAD와 원격 PR head는 여전히68c66d5다. 위 수정·시험·이번 근거는 **미커밋·미푸시**이며 원격 PR #39는 Draft·미병합 상태다. 커밋·푸시·PR 본문 갱신과 병합·Issue 종료는 수행하지 않았다. 사용자 확인 후 수정 게시·최종 head 대조가 필요하다.
- 명세 범위·기한은 변경하지 않았다. 정식 GUI/지도·격자·명령/ACK·자동 재접속·운영 부하·Core/DB와 사용자 직접 키보드/화면 확인은 이번 미실행이다. 기존 과제 전체 미완료 상태를 유지한다.
- 업무 기록: `C:\process-plan\일일업무\2026-10-22.md`의 `pr39-review-results`에 이번 리뷰와 로컬 수정 상태를 연결한다. 과거 게시·실행 기록은 보존한다.


## 7. Publication authorization after the recorded review

On 2026-10-22 the user authorized committing and publishing this fix and its evidence, updating PR #39, and merging it into develop. Section 6 and the integrity.json PR state describe the review-completion snapshot before that authorization. They are preserved as execution history. The published commit is checked against the tested source hashes; current publication and merge state is recorded on PR #39. The unrelated 72-file recheck material remains outside this PR and is handled in a separate evidence PR.
