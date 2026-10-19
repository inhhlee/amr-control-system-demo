# COM01-C 순서·독립성 첫 실행

- 실행 ID: 20261019-queue-order-01 / 실행자: Codex / 자동시험, 실제 MQTT 미사용.
- 기준: Issue #23, DN-09, develop b8af05738d08b76db84f2b020789539c8062ad07 + 미커밋 큐·시험 코드.
- 입력·예상: AmrReceiveQueueTests의 9건과 기존 529건. 같은 AMR 순서 유지·다른 AMR 독립 처리와 기존 계약 유지.
- 실제: [빌드](build.log) 오류 0·xUnit2031 경고 1, [시험](tests.trx) 538건 통과·실패/건너뜀 0.
- 판정: 자동시험 통과. Assert.Single 앞의 Where를 필터 인자 방식으로 정리할 필요가 있어 새 실행 02에서 재검증했다.
- 환경·실행 시각: [environment.json](environment.json), [종료 시각](finished-at.txt). 이전 기록을 덮어쓰지 않는다.
- 최종 결과·재현 명령·미검증 범위: [실행 02](../20261019-queue-order-02/README.md).
