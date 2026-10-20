# C3b 도구 변경 후 C3a 기본 모드 회귀

2026-10-20 10:51:10.519~10:51:20.832 KST, 같은 최종 Verify-ResponseGate.ps1의 기본 모드를 실제 실행했다. 신규 MeasureTimeout 분기와 별개로 기존 승인·구독·응답2바이트 폐기·반대 방향 전달·양쪽 TCP/제품 소켓 유지·응답 재개·Ctrl+C 정상 종료0·정리를 확인했다([결과](verification.json)·[프록시](proxy.jsonl)·[콘솔](console.stdout.log)·[소켓](sockets-blocked.json)·[정리](cleanup.json)).

기존 컨테이너3개 상태를 보존했다. 콘솔 빌드 경고/오류0이며 [소스](source-version.json)·[바이너리](binary-version.json)·[환경](environment.json)을 기록했다. 제품 단위시험을 재실행한 결과는 아니다. C3b 두 측정과 판정은 [종합 검증결과](../20261020-c3b-noresponse-02/README.md)를 따른다.
