# COM03-B 실제 TCP/MQTT 실패·취소·새 실행

- 실행: 2026-10-19 KST, Codex. [적용 기준·최종 코드·자동시험](../20261019-connect-failure-unit-03/README.md).
- [환경·사전 기준](environment.json), [소스 해시](source-version.json), [전체 판정](verification.json), [Broker 로그](broker.log).

| 시나리오 | 입력·예상 | 실제 결과 |
| --- | --- | --- |
| refused | 127.0.0.1:59022 독점 Bind·Listen 없음, 10초 제한 | TransportFailure / SocketError=ConnectionRefused / 종료 1, Connected 없음 |
| timeout-1 | 임시 TCP 서버가 CONNECT 수신 후 CONNACK 미전송, 1초 | 1043.4677ms / Timeout / 종료 1 / EOF 확인 |
| timeout-2 | 같은 조건, 2초 | 2040.8842ms / Timeout / 종료 1 / EOF 확인 |
| recovery-1 | 실패 뒤 새 프로세스로 실제 Mosquitto 접속·네 Topic 구독·Ctrl+C | 정상 연결·구독·해제·Dispose·종료 0, Broker disconnect 확인 |
| cancel | CONNECT 수신 후 대기, 30초 제한 이전 CTRL_C_EVENT | UserCanceled / 종료 0 / Connected 없음 / EOF 확인 |
| recovery-2 | 취소 뒤 새 프로세스로 실제 Mosquitto 접속·구독·Ctrl+C | 정상 연결·구독·해제·Dispose·종료 0, 새 Client ID·Broker disconnect 확인 |

시간 측정은 Connecting → ConnectFailed이며 사전 허용 오차 -50ms/+2000ms를 충족했다. 각 이름의 `*.stdout.log`·`*.stderr.log`와 [scenarios.json](scenarios.json)에 실제 포트·종료 코드·콘솔 구성원·Client ID를 남겼다. CTRL_C_EVENT는 이 도구가 생성한 콘솔 프로세스와 보호된 제어 프로세스만 포함하는 콘솔에 전송했다.

무응답 대역은 **최초 MQTT 승인 대기** 시험이다. 연결 후 KeepAlive 무응답 감지 시험으로 집계하지 않는다. MQTT 거부와 늦은 대역 응답은 자동시험에서 확인했다. 실행 도구 자체는 Broker를 기동·중단하지 않으며 전체 작업의 생성·복구 내역은 최종 검증결과에 연결했다.
