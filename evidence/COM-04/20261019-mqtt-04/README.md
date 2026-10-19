# COM04-A 실제 MQTT 첫 실행 — 20261019-mqtt-04

- 실행자: Codex, 2026-10-19 11:54 KST. [환경](environment.json), [소스 해시](source-version.json), [발행 입력·예상](publications.json).
- 실제 [콘솔](console.stdout.log): MissingField → 정상 ts=1791195767000 → IdMismatch → 정상 ts=1791195767002, 정상 2건·거부 2건·수신 시각 보존·정상 종료 로그.
- [시험 스크립트](verify-mqtt.ps1)는 실제 프로세스 종료 코드 0까지 확인했지만, 콘솔 종료 뒤 같은 컨트롤러에서 Docker 로그 수집을 호출하다 Windows 콘솔 핸들 오류 0x6으로 실패했다.
- 판정: 전체 실행 실패(시험 도구). [실패](failure.log), [컨트롤러 종료 1](controller-result.json). 콘솔의 `ExitCode=0`과 컨트롤러 종료 코드를 구분한다.
- 제품 코드는 변경하지 않았다. 로그 수집을 부모 프로세스로 옮긴 [05](../20261019-mqtt-05/README.md)에서 새 MQTT 시나리오를 수행했다. COM01-A의 기존 도구·증거는 수정하지 않았다.
