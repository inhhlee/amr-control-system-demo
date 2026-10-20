# C3a 접속 전 stop 20261020-c3a-stop-01

2026-10-20 10:08:42 KST, 최종 프록시를 BrokerPort=1로 실행하고 Client를 접속시키기 전에 `1 stop`을 보냈다. Broker는 사용하지 않았다. pending Accept 취소·Stopped 1건·프로세스 종료 0·포트 63069 재bind 성공을 확인했다([결과](verification.json)·[로그](proxy.jsonl)). stderr는 비어 있다. 취소 요청과 실제 종료·listener 반환을 구분해 확인한 시험이다.

소스 SHA-256은 verification.json에 기록했다. 정상 전달·응답 차단 실제 MQTT 결과는 [proxy-03](../20261020-c3a-proxy-03/README.md)에 연결한다. 실제 backpressure 부하나 제품 무응답 감지 시험은 아니다.
