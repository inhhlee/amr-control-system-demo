# COM04-A 실제 MQTT 시나리오 — 20261019-mqtt-05

- 실행자: Codex, 2026-10-19 11:55:08~11:55:12 KST. [환경](environment.json), [코드 해시](source-version.json), [Broker 상태](broker-state.log), [마운트](broker-mounts.json).
- [실행 스크립트](run-mqtt.ps1) → [별도 숨김 콘솔 시험](verify-mqtt.ps1). 기존 127.0.0.1:1884 Broker를 재사용했고 시작·중단·설정을 변경하지 않았다. 발행·구독 QoS 0, Retain=false, 실행별 Client ID.

| 입력(발행 순서) | 예상 | 실제 | 판정 |
| --- | --- | --- | --- |
| ts 없는 30바이트 JSON | MissingField·$.ts, 정상 전달 0 | 해당 거부 1건, 원문 숨김 | 통과 |
| 정상 ts=1791195767000 | 정상 모델 1회 | ID·ts·status·콜백 수신 UTC 시각 보존 | 통과 |
| Topic과 다른 AMR_002 ID | IdMismatch·$.id, 정상 전달 0 | 해당 거부 1건, 원문 숨김 | 통과 |
| 공백 300바이트 + 정상 49바이트 JSON | 256바이트 미리보기와 무관하게 전체 검사·정상 1회 | 349바이트·미리보기 잘림·정상 ts=1791195767002 전달 | 통과 |
| Windows CTRL_C_EVENT | 구독→연결 정리, 실제 종료 0 | 순서·프로세스 종료 0, 콘솔 stderr 비어 있음 | 통과 |

[입력·발행 결과](publications.json), [콘솔 로그](console.stdout.log), [시나리오 판정](scenario-result.json), [컨트롤러 종료 0](controller-result.json).

부모의 Broker 로그 조회는 JSON에서 자동 변환된 날짜가 로컬 문자열로 전달되어 실패했다([오류 로그](broker.log)). 이 실행 전체를 로그 수집까지 성공했다고 표시하지 않는다. [06](../20261019-final-06/README.md)에서 ISO UTC 형식을 사용해 로그 수집·해제 대조를 완료했다. 06은 새 MQTT 발행·재시험이 아니다. 컨트롤러 stderr의 Mosquitto 종료 안내는 `mosquitto -h` 버전 조회 자식 명령 출력이며 제품 콘솔 stderr와 구분한다.
