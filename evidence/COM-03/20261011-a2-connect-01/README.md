# COM-03 A2 최초 복원 시도 — 20261011-a2-connect-01

- 실행자: Codex, 2026-10-11 KST. 정확한 UTC 시각·명령·종료 코드는 [commands.jsonl](commands.jsonl).
- 기준 코드: `855d76d28a8fd096f2328aefe306c3161c13eb26` + 미커밋 A2 소스. 브랜치 `feature/8-mqtt-connect`.
- 입력: `dotnet restore AmrControlSystem.slnx --verbosity minimal`.
- 예상: 기존 프로젝트의 패키지 복원, 종료 0.
- 실제: 샌드박스에서 NuGet 소켓 접근이 차단되어 `NU1301`, 종료 1. [원본 로그](restore-01.log).
- 판정: 복원 실패(환경 권한). 제품 빌드·MQTT 실행은 이 시도에서 미실행.
- 조치: 승인된 샌드박스 밖 실행으로 재시도. [별도 실행 결과](../20261011-a2-connect-02/README.md)에 기록한다. SDK·패키지 버전은 변경하지 않았다.
