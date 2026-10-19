# COM01-C PR 준비 — 실제 MQTT 정상 경로

- 실행자 Codex / 2026-10-19 / 기준 `1060c64` + 종료 경합 보완의 미커밋 변경.
- [환경](environment.json)·[소스 해시](source-version.json)·[입력](inputs.json)·[발행 기록](publications.json)·[판정](verification.json)·[Broker 로그](broker.log).
- 입력: 두 AMR 네 유형, 001 처리만 5초 지연, 불량 JSON, 처리 중 Ctrl+C, 새 Client ID의 id 없는 FAILURE ACK. `Verify-Mqtt.ps1 -RunId 20261019-queue-pr-mqtt-01` 실행.
- 실제: 세션 0 정상 8·검사 거부 1, 세션 1 정상 1·처리 취소 1·대기 폐기 3, 세션 2 정상 1. 총 15건, 세 세션 종료 0. AMR별 순서·독립 처리·모델/수신 시각 보존·정리 통과.
- Ctrl+C는 시험이 시작한 콘솔에 Windows CTRL_C_EVENT를 보냈으며 사용자 키보드 직접 조작 실적이 아니다. 실제 Core/로봇 실행도 아니다.
- 전용 Broker는 이번 작업에서 준비했고 시험 종료 후 정리했다. 상세 환경·한계·완료조건은 [종합 결과](../20261019-queue-pr-unit-02/README.md)를 따른다.
