# MQTT 폴더 이동 검증 — 2026-10-19

Issue #21의 기존 미커밋 구현 위에서 사용자 요청으로 파일 6개를 이동했다. feature/21-multi-amr-subscription, 기준 커밋 e40e2b4a72f5901a32e723a04661482f51f23f9a. 기능·계약 변경 없음. 커밋·스테이징·게시 없음.

| 이동 전 Mqtt/ 파일 | 이동 후 |
| --- | --- |
| MqttConnection.cs | Connection/MqttConnection.cs |
| MqttConnectionSettings.cs | Connection/MqttConnectionSettings.cs |
| MessageSubscription.cs | Subscriptions/MessageSubscription.cs |
| MessageDiagnostic.cs | Subscriptions/MessageDiagnostic.cs |
| HeartbeatSubscription.cs | Subscriptions/HeartbeatSubscription.cs |
| HeartbeatDiagnostic.cs | Subscriptions/HeartbeatDiagnostic.cs |

예상: 이동 전후 파일 내용·namespace 동일, SDK 기본 소스 검색으로 여섯 파일 빌드에 포함, 기존 시험 529건 유지.

실제: [이동 전 경로·해시](../20261019-mqtt-layout-01/moves.json)와 이동 후 SHA-256 모두 일치. [현재 소스 대조](source-check.json)는 직전 MQTT 실행의 파일을 새 위치에 대응시켜 비교했다. namespace와 공개 API는 그대로다. README의 현재 소스 링크 3개를 새 경로로 갱신했다.

- 빌드: 경고 0·오류 0. [로그](build.log), [환경·코드·명령](build-result.json).
- 기존 자동시험: 529건 통과, 실패·건너뜀 0. [로그](test.log), [TRX](tests.trx), [실행 결과](test-result.json).
- 초기 폴더 생성과 첫 빌드는 환경의 쓰기 권한 제한으로 실패했다. 폴더 이동은 권한 승인 후 완료했고, [첫 빌드 실패 기록](../20261019-mqtt-layout-01/build.log)을 보존한 뒤 새 실행 02에서 통과했다.
- 실제 MQTT는 다시 실행하지 않았다. 직전 [실행 03](../20261019-multi-mqtt-03/README.md)은 이동 전 시험 근거이며 원본 기록을 수정하지 않았다. 이번 판정은 파일 구조 변경·빌드·자동 회귀에 한정한다.
