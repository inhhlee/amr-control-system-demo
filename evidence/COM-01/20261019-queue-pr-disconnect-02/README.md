# COM01-C Broker 중단 시험 — 도구 실패 02

- 기준 `1060c64` + 미커밋 종료 경합 보완. [환경](environment.json)·[소스 해시](source-version.json).
- 초기 로그 값을 문자열로 선언했지만 첫 출력 전 조건 함수의 null 오류가 계속됐다. [실패 로그](runner-failure.log).
- Broker 중단·결과 대조까지 진행하지 못했으므로 제품 판정은 미검증이다. 도구가 만든 콘솔은 finally에서 정리했다.
- 빈 로그에서는 조건 함수를 호출하지 않도록 보완하고 새 실행 03에서 통과했다. [최종 결과](../20261019-queue-pr-disconnect-03/README.md).
