# Visual Studio F5 동시 실행 — 2026-10-27 보존 근거

이 색인은 2026-10-28에 기존 10/27 증거를 정리해 추가했다. 아래 실행은 10/27 당시 수행한 결과이며 오늘 재실행 횟수에 합산하지 않는다.

- 코드 기준: HEAD `4e44fa8` + 당시 미커밋 격자/Heartbeat/GUI. `.slnLaunch`의 **AMR Grid + GUI**, 두 프로젝트의 **Grid MQTT**, VS 전용 Map 출력 경로를 구성했다. 기본 GUI Mock은 유지했다.
- [verification.json](verification.json): 솔루션 재열기 후 F5, Map49076/GUI25468, Broker1884·Subscribed·Alive·Heartbeat101. 당시 기존 Map29664/15464/20932 유지.
- [F5 GUI 화면](f5-gui-final.png) · [VS 상태](vs-final.ax.txt) · [종료 검증](f5-stop-02.json) · [기존 파일·소스 ZIP·참조 감사](final-audit.json). 마지막 실행은 당시 사용자 확인용으로 유지했으며 현재 실행 중이라는 뜻은 아니다.
- 자동1095건은 같은 제품 소스의 앞선 Heartbeat 검증 근거를 재사용했다. 이 설정 작업에서 전체 시험을 재실행한 것으로 집계하지 않는다.

실행 절차는 [BUILDING](../../../BUILDING.md#visual-studio에서-f5로-함께-실행), 통신 세션 분리 후 **2026-10-28 신규 F5/GUI·자동1116건**은 [별도 실행 기록](../../COM-02/20261028-control-session-01/README.md)을 따른다. 커밋·푸시·PR·병합은 수행하지 않았다.
