# COM03-C2 최종 자동시험 20261020-c2-unit-02

- 실행: Codex, 2026-10-20 09:29:42.244~09:29:49.060 KST / feature/6-mqtt-keepalive / d777ab7 + C1·C2 미커밋 변경.
- [unit-01](../20261020-c2-unit-01/README.md)의 사용 중단 API 경고를 시험 파일에서만 제거했다. 제품·실제 시험 도구는 변경하지 않았다.
- `dotnet build AmrControlSystem.slnx --no-restore --verbosity minimal`: 경고·오류 0.
- `dotnet test AmrControlSystem.slnx --no-build --no-restore --logger 'trx;LogFileName=tests.trx' --results-directory evidence/COM-03/20261020-c2-unit-02`: 기존 758+신규 3=761 통과, 실패·건너뜀 0.
- 기존 취소·단절 순서와 동시 32회 신호, 최초 접속 실패·늦은 결과·큐·수신·로그·명령 회귀를 포함한 전체 테스트다. 32회 반복을 모든 가능한 스케줄의 증명으로 확대하지 않는다.
- 신규 세 사례는 ServerShuttingDown·NormalDisconnection·전송 예외의 안전한 사유·UTC 시각·Client ID, 비밀 자유 문구 비노출, 종료 1·Dispose 1회·메시지 핸들러 제거를 확인한다. 실제 MQTT 사유·네트워크 전체를 Fake 결과로 대체하지 않는다.
- [환경](environment.json)·[소스 69개](source-version.json)·[최종 대조](source-comparison.json)·[build.log](build.log)·[test.log](test.log)·[TRX](tests.trx).
- 실제 MQTT 뒤의 차이는 시험 파일 한 개의 생성자 교체다. 제품·도구·바이너리 동일성은 [C2 실제 시험 대조](../20261020-c2-mqtt-01/source-comparison.json)에 기록한다. [상세 검증결과](../20261020-c2-mqtt-01/README.md).
