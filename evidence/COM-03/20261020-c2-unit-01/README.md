# COM03-C2 자동시험 20261020-c2-unit-01

- 실행: Codex, 2026-10-20 KST / feature/6-mqtt-keepalive / d777ab7 + C1 보존·C2 미커밋 변경.
- 단절 진단과 안전한 로그 필드·시간·NormalDisconnection의 사용자 취소 오분류 방지 3사례를 추가했다.
- 전체 761건 통과·실패/건너뜀 0. 빌드는 성공했으나 새 시험의 MqttUserProperty 문자열 생성자 사용 중단 경고 1건이 있었다.
- [환경](environment.json)·[소스](source-version.json)·[build.log](build.log)·[test.log](test.log)·[TRX](tests.trx).
- 해당 생성자를 지원되는 byte segment 생성자로 바꾼 뒤 [unit-02](../20261020-c2-unit-02/README.md)에서 최종 761건·경고/오류 0을 확인했다. 이 최초 통과·경고 이력은 보존한다.
- 실제 MQTT 결과·적용 기준·한계는 [C2 상세 검증결과](../20261020-c2-mqtt-01/README.md)에서 관리한다.
