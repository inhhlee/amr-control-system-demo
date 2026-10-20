# 검증결과 COM03-C2 Broker 종료·새 콘솔 접속 20261020-c2-mqtt-01

- 작성자·실행자: Codex, 2026-10-20. 사용자 요청으로 같은 #6 브랜치에서 C2를 구현·시험했다.
- 제품·시험·도구·증거·README는 로컬 미커밋이며 앞선 C1 변경을 보존했다.

## 1. 목표와 적용 기준

- 과제1 / COM-03 / COM03-C2, [Issue #6](https://github.com/inhhlee/amr-control-system-demo/issues/6).
- 착수 기준: 과제계획 v0.43, 기능명세 v0.7 COM-03, 공통규칙 C-09·C-10, COM03-C 안내 v0.7의 C2. 원본 문서는 C:/process-plan에서 관리한다.
- 기존 C1의 유지·PING 근거와 PR #25 최초 실패·취소 처리, PR #27 최초 종료 원인 선택을 재사용한다. C2는 기존 ConsoleRunner 단절 콜백의 진단만 보완했다.
- 사전 기준: 실제 접속·구독 뒤 격리 Broker를 정상 종료하고, docker stop 요청 직전 UTC 시각부터 단절 감지 10초 이내·프로세스 종료 관찰 15초 이내, 단절 이벤트 1건·종료 1·자원 정리를 확인한다. 같은 컨테이너를 재기동하고 새 PID·Client ID의 명시적 접속·구독·Ctrl+C 종료 0을 확인한다.
- 복구 후 정상 세션에서 C1을 함께 회귀한다. KeepAlive 5초·관찰 12초(최소 10초)·PING 2쌍 이상·업무 PUBLISH 0건을 확인한다.
- 위 시간은 실습 관찰 상한이며 Docker 명령·프로세스 관찰 지연을 포함한다. 최초 접속 제한·KeepAlive의 응답 차단 감지 제한·운영 SLA로 사용하지 않는다. C3 승인 후 응답 차단, C4 상세 중복·늦은 알림, AMR Heartbeat·자동 재접속·재구독은 남아 있다.
- 별도 spec_revision/verification_index 추적기는 없어 실행 ID·Git 기준·소스 및 바이너리 해시로 식별한다.

## 2. 실행 환경

| 항목 | 실제 값 |
| --- | --- |
| 실행 ID·시각 | 20261020-c2-mqtt-01 / 2026-10-20 09:27:50.408~09:28:09.429 KST |
| 위치·브랜치·기준 | C:/amr-control-system-demo / feature/6-mqtt-keepalive / develop d777ab7d8ef034d66a862343231268cd1476af24 + C1 보존·C2 미커밋 변경 |
| OS·SDK·라이브러리 | Windows 10.0.26200 / PowerShell 7.6.5 / SDK 10.0.401, net10.0 / MQTTnet 5.2.0.1603 |
| Docker·Broker | Docker Client/Engine 29.7.2 / Mosquitto 2.1.2, eclipse-mosquitto:2.1.2-alpine 설치 이미지 재사용 |
| 격리 컨테이너 | amr-c2-20261020-c2-mqtt-01 / e77d61a36ddfeca506c07745201f04bf9e64978043809568dc0eb7a3660c7bbd |
| 주소 | 최초 127.0.0.1:56793 → 재기동 후 127.0.0.1:56796. 동적 Host port가 바뀌어 docker port로 다시 조회한 주소에 새 콘솔을 접속했다 |
| 설정·데이터 | 실행별 broker.conf만 읽기 전용 mount, persistence false, stdout debug 로그. 기존 Compose·Broker 데이터·설정 변경 없음 |
| MQTT 계약·입력 | MQTT 5 / Clean Start / Session Expiry 0 / KeepAlive 5초 / 기존 네 Topic QoS 0 구독. 업무 발행자·Payload·Retain 입력 없음 |
| 실제 구성요소·대역 | 실제 제품 콘솔·Mosquitto·OS 프로세스/TCP 조회. MQTT 대역 없음. 구조화 로그의 필드 검증은 별도 자동시험의 Capture sink로 확인 |
| Seq | 실제 C2의 시험 자식 프로세스에서만 비활성화, 실제 Seq 전송·조회 재실행 없음 |
| 코드 식별·증거 | [환경](environment.json)·[소스 71개](source-version.json)·[바이너리 4개](binary-version.json)·[최종 대조](source-comparison.json)·[명령](commands.jsonl)·[mount](broker-mounts.json) |

## 3. 입력과 절차

```powershell
# Windows, PowerShell 7.5 이상, NetTCPIP 모듈, 기동된 Docker Linux Engine 및 설치 이미지 필요
dotnet build AmrControlSystem.slnx --no-restore --verbosity minimal
pwsh -NoProfile -File tests/manual/COM03-C/Verify-BrokerShutdown.ps1 -RunId <새-실행ID>
```

1. 기존 #6 브랜치·C1 미커밋 변경을 확인했다. ConsoleRunner와 ConsoleShutdownReasonTests만 수정하고 전체 빌드·자동시험을 수행했다.
2. 기존 컨테이너 3개 상태·이미지를 기록하고 별도 이름·루프백 동적 포트의 Broker를 준비했다. 기존 서비스를 시작·중단하지 않았다.
3. 실제 콘솔이 접속·구독·대기에 도달한 것을 확인한 뒤 요청 시각을 기록하고 해당 컨테이너에만 docker stop --time 5를 실행했다.
4. 콘솔의 단절 원인·시각·종료 코드와 큐/구독 정리·Dispose를 검사했다. 프로세스 종료와 해당 루프백 포트의 Established 연결 0건도 확인했다.
5. 동일 컨테이너를 docker start로 재기동하고 새 Host port를 조회했다. 기존 C1 제어 프로세스를 재사용해 새 PID·Client ID의 콘솔 접속·구독·12초 유지·PING·CTRL_C_EVENT 정상 종료를 확인했다.
6. 원본 로그를 수집한 뒤 시험 컨테이너만 정리하고 기존 컨테이너 상태를 비교했다. 실패 시에도 생성한 프로세스/컨테이너만 정리하며 기존 실행 ID는 덮어쓰지 않는다.

## 4. 실제 결과와 판정

| 시험·예상 | 실제 결과 | 판정·직접 근거 |
| --- | --- | --- |
| C2-CAUSE 안전한 사유·감지 시각 | Client ID amr-demo-c2785509767c428a9451feabcab28047, Reason=NormalDisconnection, ClientWasConnected=True, ExceptionType=None, SocketError=None | 통과, [lost.stdout.log](lost.stdout.log), [lost.session.json](lost.session.json) |
| C2-DETECT 10초 이내 | 종료 요청 09:27:52.679472 / 감지 09:27:52.732963 KST, 53.4907ms. Broker 정상 종료 코드 0 | 통과, [shutdown.json](shutdown.json), [Broker 종료 로그](broker-after-stop.log) |
| C2-EXIT 종료 원인 일치·정리·15초 이내 종료 1 | ShutdownRequested Reason=ConnectionLost, QueueStopped·SubscriptionStopped·Disposed, 이벤트 1건·종료 1. 종료 관찰 79.4166ms, 프로세스 없음·Established 소켓 0건·stderr 비어 있음 | 통과, [shutdown.json](shutdown.json), [lost.stderr.log](lost.stderr.log) |
| C2-RESTART 동일 Broker·새 실행 | 동일 컨테이너 재기동, 새 콘솔 PID 35848 / Client ID amr-demo-5a004ca1c1c448dfb27790e3bf5ff3b9. 이전 PID 14992와 구분 | 통과, [restart.json](restart.json), [복구 세션](session.json), [전체 Broker 로그](broker.log) |
| C1-REGRESSION 유지·PING·정상 종료 | 재접속 세션 09:27:56.166~09:28:08.250 KST, 12.0829546초·PING 2쌍·PUBLISH 0건. Ctrl+C 후 정상 해제·Dispose·종료 0 | 통과, [console.stdout.log](console.stdout.log), [recovery-ping.log](recovery-ping.log), [verification.json](verification.json) |
| C2-CLEAN 시험 자원 정리·기존 상태 보존 | 시험 컨테이너 삭제, 기존 컨테이너 3개 상태 보존, 새 콘솔도 종료 확인 | 통과, [cleanup.json](cleanup.json), [전](containers-before.jsonl)·[후](containers-after.jsonl) |
| C2-UNIT 최종 빌드·전체 회귀 | 기존 758+신규 3=761 통과, 실패·건너뜀 0, 경고·오류 0 | 통과, [unit-02](../20261020-c2-unit-02/README.md)·[TRX](../20261020-c2-unit-02/tests.trx)·[빌드](../20261020-c2-unit-02/build.log) |

MQTTnet의 NormalDisconnection은 관찰한 프로토콜 사유 값이다. 이를 사용자 Ctrl+C나 애플리케이션의 정상 종료로 치환하지 않는다. 이번에는 외부 Broker가 종료됐고 사용자는 취소하지 않았으므로 애플리케이션 종료 원인은 ConnectionLost·종료 코드는 1이다. 사유 코드만으로 원격 장애의 모든 실제 원인을 판별한 것으로 확대하지 않는다.

## 5. 문제와 후속 조치

- [unit-01](../20261020-c2-unit-01/README.md)은 761건 통과·빌드 경고 1건이었다. 새 시험의 MqttUserProperty(string,string) 사용 중단 경고를 지원되는 byte segment 생성자로 교체한 뒤 unit-02에서 761건·경고/오류 0을 확인했다. 이전 결과를 보존했다.
- 실제 MQTT 실행 뒤 변경한 파일은 해당 시험 파일 한 개뿐이다. 최종 단위시험 소스 69개는 스냅샷과 모두 일치한다. 실제 실행 당시 71개 중 차이는 이 시험 파일 1개이며 제품·수동 도구 소스와 실행 바이너리 4개는 동일하다. 따라서 MQTT를 다시 실행한 것으로 표시하지 않고 기존 실제 근거를 연결한다. [대조](source-comparison.json)
- 선택 이유: 기존 단절 콜백·최초 종료 원인 선택·큐/구독/Client 소유권을 유지하고 필요한 진단 필드만 추가했다. 새 상태 머신·별도 감시 폴링을 추가하지 않는다. 여러 앱이 같은 연결 감시 계약을 사용하게 되면 공통 연결 이벤트로 옮기는 대안이 있으나 현재 단일 콘솔에 새 계층을 추가할 근거는 없다.
- 로그에는 프로토콜 코드·예외 유형·SocketError·UTC 감지 시각·Client ID만 명시한다. Broker 자유 형식 ReasonString·UserProperties·예외 메시지를 전송하지 않는다. 이 경계는 새 세 시험 사례에서 확인했다.
- 리뷰 질문: 왜 NormalDisconnection인데 종료 1인가? 프로토콜 사유와 사용자 요청 여부가 다르기 때문이다. 감지 시간의 기준점은? docker stop 호출 직전 UTC이며 네트워크만의 지연/SLA가 아니다. 실제 시험 뒤 API 교체가 결과를 바꾸지 않는 근거는? 제품·수동 도구·바이너리 해시 동일과 최종 761건 회귀다.
- Broker 재기동 뒤 새 콘솔은 도구가 명시적으로 시작했다. 자동 재접속·재구독 통과가 아니며, 동적 포트가 바뀐 점을 숨기지 않는다. 고정 포트 재기동 시나리오를 별도로 수행한 결과도 아니다.
- C3의 TCP 연결을 유지한 응답 차단·감지 시간 및 C4의 상세 중복·종료 후 알림 검증은 남아 있다. 실제 Seq 새 진단 필드 수집·장시간/부하 시험은 수행하지 않았다.

## 6. 종합 결과

- C2 실제 Broker 종료 → 단절 감지·정리·종료 1 → 같은 Broker 재기동·새 정상 콘솔 종료 0 시나리오 통과. 복구 세션에서 현재 제품 코드의 C1도 회귀 통과했다.
- #6은 C1·C2 부분 검증 완료·진행 중이며 COM-03 전체·과제1 전체 완료가 아니다.
- 커밋·푸시·PR 생성·GitHub Issue 수정/종료·외부 메시지 전송은 수행하지 않았다.
- 결과는 C:/process-plan의 과제계획·#6 담당 문서·일일업무/2026-10-20.md에 요약·연결한다.
