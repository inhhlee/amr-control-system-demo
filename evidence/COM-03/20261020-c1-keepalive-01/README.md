# COM03-C1 / 20261020-c1-keepalive-01 — 시험 도구 오류

- 실행: Codex, 2026-10-20 KST / feature/6-mqtt-keepalive / 기준 d777ab7.
- 목표: 격리 Broker의 업무 메시지 없는 KeepAlive 12초 관찰.
- 실제: 빌드 경고·오류 0 이후 컨트롤러가 아직 비어 있는 콘솔 출력 파일을 읽고 null에 .Contains를 호출해 실패했다. 제품 관찰과 PING 검증 이전의 시험 도구 오류다.
- 판정: 실패. 제품 연결 유지 통과로 집계하지 않는다.
- [제어 프로세스 오류](controller.stderr.log), [실패 기록](controller-failure.log), [전체 도구 오류](runner-failure.log), [build.log](build.log), [Broker 원본](broker.log), [cleanup.json](cleanup.json)을 보존했다.
- 생성한 시험 컨테이너는 정리했고 기존 컨테이너 3개 상태는 보존했다. 별도 persistence volume은 생성하지 않았다.
- 조치: 출력 존재·null 확인을 보완하고 [새 실행 02](../20261020-c1-keepalive-02/README.md)에서 전체 C1을 다시 수행했다. 이전 증거를 덮어쓰지 않았다.
