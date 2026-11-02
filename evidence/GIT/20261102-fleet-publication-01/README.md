# 장애·RESET·Fleet 공통 맵 게시 대상 — 2026-11-02

사용자의 “현재 내용을 github에 올려줘” 요청에 따라 `feature/50-fault-reset`의 현재 구현과 관련 시험 근거를 게시한다. 기준 HEAD 및 최신 `origin/develop`은 `4aa0b8a3ba4941b872f30ce1619b18d106023daa`다. 관련 Issue는 #50·#51이며 사용자 후속 요청으로 Fleet 엔진·공통 맵 GUI까지 범위를 확장했다. 실제 GUI 완료조건이 남아 있어 `develop` 대상 Draft PR로 검토한다.

## 포함 범위

- 장애 발생·원인 해소 판정·명시적 RESET 복구와 최종 ACK.
- 장비별 독립 컨텍스트·큐·장애 시계·MQTT 세션을 갖는 Fleet 실행 엔진.
- 단일 GUI/MapCanvas의 다중 AMR 표시와 선택 장비 MOVE/STOP/RESET.
- 설정 예제, 재현 절차, 관련 자동시험 및 실제 MQTT·GUI 실행 증거.
- 이전에 남아 있던 `.vscode`, 과제1 보고 그림, 10월 실행/백업 자료 등 이번 변경과 직접 관련 없는 미추적 파일은 보존하고 게시 대상에서 제외한다.
- 작업 전 원문 백업인 두 `before.patch`는 로컬에 보존한다. 게시 근거는 소스 해시·보존 감사·시험 결과를 사용한다.

## 시험 코드와 게시 코드 대조

[최종 소스 해시](../../GUI/20261102-fleet-map-01/final-source-hashes.json)의 140개 파일을 게시 준비 시 다시 대조하여 변경 0건을 확인했다. 게시 작업으로 제품 코드를 바꾸지 않았으며 기존 시험을 새 실행처럼 집계하지 않는다.

| 구분 | 실제 실행 결과 | 근거 |
| --- | --- | --- |
| 최종 GUI·Core 자동시험 | 96 + 17 = 113건 통과 | [GUI 결과](../../GUI/20261102-fleet-map-01/README.md) |
| 앞선 Fleet 엔진 자동시험 | Simulator 271, 당시 GUI 85, Map 72 통과 | [Fleet 결과](../../AMR/20261102-fleet-engine-01/README.md) |
| 독립 Fleet·실제 Broker | 장비별 명령·장애·RESET·단절 격리·종료 8항목 통과 | [최종 실제 MQTT 결과](../../AMR/20261102-fleet-engine-01/mqtt-02/results.json) |
| 독립 GUI·Fleet·실제 Broker | 두 장비 수신, AMR_01 MOVE·FAULT·미해소 RESET 실패 확인. 전체 수동 시험은 미완료 | [GUI 관찰 결과](../../GUI/20261102-fleet-map-01/README.md) |

GUI 관찰 당시 명령/ACK 9쌍 대조 기록은 해당 관찰 구간의 결과다. 게시 준비 시 Simulator 로그에 추가 RESET `GUI-1e9386f9133f4f4d89c7028ba74ef66b`의 `CLEARED` 및 `SUCCESS/RESET_COMPLETED` 송신 기록을 확인했다. MQTT 관찰 로그는 그 전에 종료되어 추가 응답의 관제 수신·화면 복구를 확인한 것으로 집계하지 않는다.

## 남은 확인

지도 보완 버전의 실제 화면, 두 장비 개별 MOVE·STOP, 한 장비 FAULT 중 다른 장비 주행, 복구 결과의 실제 GUI 수신·좌표 보존, 선택 변경 중 늦은 ACK, 실제 GUI 정상 종료를 확인해야 한다. 사용자가 직접 조작 중인 창·Simulator·Broker는 게시 작업에서 조작하지 않는다.

정상 연결의 ACK 확인은 QoS0 단절·과부하 시 무손실 전달 보장이 아니다. Issue 종료와 PR 병합은 이번 게시 요청에 포함하지 않는다. 원본 계획·명세·일일업무는 별도 문서 저장소에서 관리한다.
