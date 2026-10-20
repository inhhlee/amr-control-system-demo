# C3a 중간 실행 20261020-c3a-proxy-02 — 통과

2026-10-20 10:07 KST, [결과](verification.json)·[응답 전달/폐기](proxy.jsonl)·[차단 중 TCP](sockets-blocked.json)에서 실제 접속·구독 이후 응답 2바이트 폐기·Client→Broker 전달 지속·TCP 유지·전달 재개 후 정상 종료를 확인했다. 시험 자원을 정리하고 기존 컨테이너 3개 상태를 유지했다([cleanup.json](cleanup.json)).

이후 stop이 응답 쓰기 gate를 기다리지 않도록 취소 경로를 보완했다. 이 실행을 수정 후 코드의 시험으로 표시하지 않는다. 당시 [소스](source-version.json)를 보존하며 최종 도구의 실제 재실행은 [실행 03](../20261020-c3a-proxy-03/README.md)이다. C3b 무응답 감지 판정은 하지 않았다.
