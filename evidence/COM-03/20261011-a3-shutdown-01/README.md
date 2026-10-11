# A3 첫 빌드 — 실패 기록

- 실행 ID: 20261011-a3-shutdown-01, 2026-10-11, COM-03 / Issue #8.
- 의존성 복원: 성공. [명령 기록](commands.jsonl), [복원 로그](restore.log).
- 최초 빌드: 실패. 시험 코드가 MQTTnet 5의 TCP 옵션에 존재하지 않는 Server/Port 속성을 참조했다. [빌드 로그](build.log), [당시 시험 소스](MqttConnectionTests.initial.cs.txt).
- 제품 동작 시험은 이 실행에서 미실행이다.
- 시험 코드에서 RemoteEndpoint를 DnsEndPoint로 확인하도록 수정했다. 성공한 재빌드·자동시험은 [다음 실행](../20261011-a3-shutdown-02/README.md), 실제 MQTT 최종 결과는 [03](../20261011-a3-shutdown-03/README.md)에 보존한다.
