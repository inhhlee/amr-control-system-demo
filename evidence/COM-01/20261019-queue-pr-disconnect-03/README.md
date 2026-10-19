# COM01-C PR 준비 — 실제 Broker 중단

- 실행자 Codex / 2026-10-19 / 기준 `1060c64` + 미커밋 종료 경합 보완.
- [환경](environment.json)·[소스 해시](source-version.json)·[입력](inputs.json)·[콘솔](console.stdout.log)·[Broker](broker.log)·[판정](verification.json).
- 명령: `pwsh -NoProfile -File tests/manual/COM01-C/Verify-Disconnect.ps1 -RunId 20261019-queue-pr-disconnect-03 -AllowBrokerStop`.
- 입력: 001의 처리를 60초 지연시키고 네 유형 4건과 002 ACK 1건을 발행했다. 002 정상 전달과 001 처리 중 1·대기 3 상태에서 전용 amr-demo Broker를 중단했다.
- 예상/실제: ConnectionLost 후 실행 1건 취소·대기 3건 폐기, 새 처리 시작/전달 0, 구독/Client 정리, 비정상 연결 종료를 나타내는 종료 코드 **1**. 전부 통과했다. 정상 전달 1 + 취소 1 + 폐기 3 = 발행 5건.
- 프로토콜을 유지한 장시간 무응답·패킷 손실 시험은 아니다. Broker 프로세스 중단으로 실제 연결 끊김을 검증했다.
- finally의 [Broker 재시작](broker-restore.log)을 확인했고 전체 작업 종료 때 새 컨테이너·네트워크를 정리했다. [종합 결과](../20261019-queue-pr-unit-02/README.md).
