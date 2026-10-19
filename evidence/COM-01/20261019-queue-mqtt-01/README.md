# COM01-C 실제 MQTT 콘솔 큐 시험

## 목표·기준과 환경

Issue #23 / 기능명세 v0.7 COM-01·R-04·공통규칙/데이터 v0.4·결정사항 v0.5·DN-09. 실행자 Codex, 2026-10-19 18:40:58~18:41:24 KST. 기준 b8af05738d08b76db84f2b020789539c8062ad07 + feature/23-amr-receive-queue 미커밋 변경.

- [환경](environment.json): Windows 10.0.26200, SDK 10.0.401, MQTTnet 5.2.0.1603, Docker 29.7.2, Mosquitto 2.1.2. MQTT 5, QoS 0, Retain=false.
- 기존 Broker 127.0.0.1:1884 재사용. [상태](broker-state.log)·[마운트](broker-mounts.json). 시작·재시작·설정 변경 없음. 버전 확인용 mosquitto -h의 종료 문구는 가동 중인 Broker 종료를 뜻하지 않는다.
- [시험 소스 해시](source-version.json)·[입력](inputs.json)·[발행 기록](publications.json)·[최종 판정](verification.json)·[Broker 로그](broker.log).

## 절차·입력·예상

실제 콘솔과 Broker를 연결하고 001에는 건당 5초 비동기 지연을 주었다. 002는 지연 없이 처리한다. 모델 필드·최초 수신 UTC를 입력 및 진단 로그와 대조한다. 세 세션 각각 도구가 생성한 콘솔에만 Windows CTRL_C_EVENT를 보낸다. 사용자 직접 키보드 조작 실적은 아니다.

```powershell
dotnet build AmrControlSystem.slnx --no-restore
pwsh -NoProfile -File tests/manual/COM01-C/Verify-Mqtt.ps1 -RunId <새-실행ID>
```

기존 실행 ID 사용 시 도구가 중단한다. 입력·도구 원본은 tests/manual/COM01-C/에 있다. Broker가 이미 실행 중이어야 한다.

## 실제 결과

| 세션 | 입력·예상 | 실제·판정 |
| --- | --- | --- |
| 0 | 두 AMR의 네 유형 + 불량 JSON 1건. 002 독립 처리·AMR별 순서 유지 | 발행 9 → 정상 8·검사 거부 1. 002 네 건이 001 첫 완료보다 먼저 처리. 각 AMR 네 유형 순서·모델·수신 시각 보존. 통과 |
| 1 | 001 실행 1·대기 3, 002 ACK 1 뒤 Ctrl+C | 발행 5 → 002 정상 1, 001 HandlerCancelled 1·ShutdownDiscarded 3. 이후 새 Received 없음. 통과 |
| 2 | 새 Client ID로 id 없는 FAILURE ACK 1 | 정상 1, Id=null·FAILURE·reason 보존. 이전 메시지 재전달 없음. 통과 |

**총 발행 15 = 정상 전달 10 + 검사 거부 1 + 처리 중 취소 1 + 대기 폐기 3.** 세 세션 모두 종료 코드 0. 서로 다른 Client ID·구독/이벤트/연결 정리와 Broker disconnect 기록 확인.

직접 관찰할 로그: [세션 0](console-0.stdout.log), [세션 1](console-1.stdout.log), [세션 2](console-2.stdout.log). 세션 0에서 001 첫 수신은 09:41:01.569 UTC, 첫 완료는 09:41:06.586 UTC다. 그 사이 002 네 건이 완료됐다. 세션 1의 종료는 QueueStopped → Unsubscribing → SubscriptionStopped → Disconnecting → Disconnected → Disposed → ExitCode=0 순서다.

## 한계·남은 범위

- 실제 로봇·Core 업무·GUI를 실행한 시험이 아니다. 콘솔은 정상 모델을 출력하는 처리 대역이다.
- 취소·대기 제한·늦은 성공/실패·로그 오류는 [자동시험 543건](../20261019-queue-console-unit-01/README.md)에서 별도로 확인했다. 실제 MQTT 과부하·Broker 강제 단절·장시간 부하·종료 경합 반복은 미실행이다.
- 취소 무시 처리자는 강제 중단하지 않는다. 라이브러리의 정리 Task는 늦은 종료를 관찰하지만 프로세스 자체 종료 이후까지 관찰을 보장하지 않는다. 이번 콘솔 대역은 취소에 협조한다.
- 이번 요청의 콘솔 연결·실제 시험은 통과. #23 전체 완료조건 체크·커밋·푸시·PR은 수행하지 않았다.
