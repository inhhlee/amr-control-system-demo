# COM01-C Broker 중단 시험 — 도구 실패 01

- 기준 `1060c64` + 미커밋 종료 경합 보완. [환경](environment.json)·[소스 해시](source-version.json).
- 콘솔의 첫 출력 전 빈 로그를 조건 함수가 읽으면서 null 메서드 호출 오류가 발생했다. [실패 로그](runner-failure.log).
- Broker 중단·결과 대조까지 진행하지 못했으므로 제품 통과/실패로 판정하지 않는다. 도구가 만든 콘솔은 finally에서 정리했다.
- 초기 로그 처리 보완은 02에서 재시도했고, 최종 03에서 통과했다. [최종 결과](../20261019-queue-pr-disconnect-03/README.md).
