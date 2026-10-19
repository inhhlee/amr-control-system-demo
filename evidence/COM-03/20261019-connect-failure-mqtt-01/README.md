# COM03-B 첫 실제 연동 실행 — 도구 오류

2026-10-19, 실제 TCP 거절과 1초/2초 제한·소켓 EOF까지 통과했다. recovery-1에서 시험 도구가 아직 빈 로그를 읽으며 null에 Contains를 호출해 중단됐다. 제품의 접속 실패로 판정하지 않는다. 정상 새 세션·Ctrl+C 취소는 이 실행에서 미검증이다.

[부분 결과](scenarios.json) · [오류](failure.log) · [환경](environment.json) · [소스](source-version.json). 빈 출력 처리 보완과 최종 제품 빌드 후 [mqtt-02](../20261019-connect-failure-mqtt-02/README.md)에서 전체 시나리오를 재실행했다.
