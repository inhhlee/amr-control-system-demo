# COM04-A 첫 빌드 — 20261019-validator-01

- 실행자: Codex, 2026-10-19. 검사기 단독 구현의 첫 빌드.
- 기준·입력: #17, COM-01-Heartbeat-v0.2·수신 v3. [소스 해시](source-version.json), [환경](environment.json).
- 명령·예상: `dotnet build AmrControlSystem.slnx --no-restore --verbosity minimal`, 종료 0.
- 실제: MSB3491, 기존 obj의 임시 파일 쓰기 권한 거부, 종료 1. [로그](build.log), [시각·종료 코드](build-result.json).
- 판정: 빌드 실패(환경 권한). 자동시험·실제 MQTT는 미실행이며 통과로 집계하지 않는다.
- 후속: 승인된 샌드박스 밖 빌드와 단독 자동시험은 [새 실행 02](../20261019-validator-02/README.md)에 보존했다.
