# C3b 측정 1 / 20261020-c3b-noresponse-01

2026-10-20 10:50:04.496~10:50:17.593 KST, 같은 #6 브랜치의 제품·C3a 프록시를 사용했다. 접속·구독 뒤 추가 대기 0초로 응답 차단을 요청했다. [사전 기준](criteria.json)은 장애 전에 저장했다.

- 차단 ACK 10:50:08.891947 → 감지 10:50:16.221729 KST, **7.3297821초**. 마지막 송신 이후 차단 위상 0.2447141초를 반영한 사전 범위 7.1552859~8.5052859초 안이다.
- 마지막 송신→감지 7.5744957초(허용 7.4~8.75), 첫 실제 응답 폐기→감지 2.5027692초(허용 2.4~3.5), 폐기→Client EOF 단조 시간 2.4998850초다.
- MqttCommunicationTimedOutException·ClientWasConnected=True·ConnectionLost 1건·정리·종료 1. 감지 후 프로세스 종료 관찰 26.9204ms. 응답 재개·Ctrl+C·감지 전 Broker 중단은 없다.
- Broker 127.0.0.1:52556 / proxy 127.0.0.1:52558 / Client ID amr-demo-318d536fd57d41ea958d7c593251f44e. 기존 컨테이너 3개 상태 보존·시험 자원 정리 완료.

[실제 측정](measurement.json)·[전체 결과](verification.json)·[프록시](proxy.jsonl)·[콘솔](console.stdout.log)·[Broker](broker.log)·[차단 중 TCP](sockets-blocked.json)·[환경](environment.json)·[정리](cleanup.json).

두 위상 비교·판정 근거·한계와 코드/시험 유효성은 [종합 검증결과](../20261020-c3b-noresponse-02/README.md)를 따른다. 운영 SLA로 적용하지 않는다.
