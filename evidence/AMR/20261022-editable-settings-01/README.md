# 지도 시연 설정 분리 검증결과

- 실행일: 2026-10-22. 구현·시험 위치: `C:\amr-control-system-demo`.
- 요청: 사용자가 바꿀 설정을 별도로 묶어 관리한다. 이번 로컬 지도 도구 확장이며 공식 운영 설정 계약의 확정과 구분한다.
- 기준: 기존 단일 AMR 경유점·우회·MQTT 수신 표시 계약, 로컬 과제2 과제계획 v0.12 초안 AMR2-F01/02/05, 기능명세 v0.9 AMR-01/02/05, 공통데이터명세 v0.6, 결정사항 v0.9 D-12. 원본은 `C:\process-plan`이며 공식 계획·명세의 범위·완료 상태는 변경하지 않았다.
- 코드: `feature/33-simulator-movement`, 기준 HEAD `a8934f0bfed803cee47d7c2e13775ba648e0862a` + 미커밋 변경. 정확한 소스·빌드 산출물 해시는 [source-version.json](source-version.json), 소스 복사본은 [source.snapshot.zip](source.snapshot.zip). Windows·.NET SDK 10.0.401·net10.0·MQTT5·Mosquitto 2.1.2-alpine 환경이다.

## 변경·완료조건

기존 `GridMissionSettings`·`GridNavigator`·`GridNavigationSession`·`GridTelemetry`·지도 Program/화면을 변경하고, `config/map-demo/settings.json`과 같은 폴더의 수정 안내를 새로 만들었다. 기존 경유점 전용 `config/simulator.grid.json`과 제품 콘솔 설정 `config/simulator.single.json`은 보존한다. `src/` 제품 코드는 변경하지 않았다.

- [x] 지도·ID·출발점·순서 있는 경유점·이동 간격·지도 포트·MQTT 접속/송신 주기를 한 설정 파일에서 읽는다.
- [x] 지도 행·열과 초기 고정 장애물도 코드 수정 없이 바꾼다. 탐색·초기화·수신 좌표 범위·화면 칸 수가 해당 지도를 따른다.
- [x] 파일은 시작 때 한 번 읽고 동일 바이트를 실행 증거에 보관한다. 실행 중 파일 변경은 자동 반영하지 않으며 재시작 후 적용한다. 재빌드는 필요 없다.
- [x] 잘못된 지도·좌표·주기·포트·추가/중복/누락 필드를 시작 전에 거부한다. 잘못된 파일은 이유를 표시하고 종료1이다.
- [x] 같은 AMR이 경유점을 순서대로 방문하고 장애물을 우회하며, MQTT 모드의 위치·이동 흔적은 관제가 받은 Telemetry만 사용한다.

실행은 `dotnet run --no-build --project tools/Amr.Simulator.Map`이며 기본 파일의 포트59344·MQTT1884를 사용한다. `--config`로 다른 파일을 선택할 수 있고, `--port` 및 기존 MQTT 옵션 4개 묶음은 파일보다 우선한다. 파일의 `mqtt.enabled=false`는 직접 계산 표시 모드다. 이전 3필드 형식은 기존 5×6·600ms·포트5088·MQTT 미사용 기본값으로 호환한다.

## 입력·예상·실제·판정

| 시험 종류 | 입력과 예상 | 이번 실제 결과·증거 | 판정 |
| --- | --- | --- | --- |
| 자동시험 | 기존 이동/MQTT + 새 설정·맵 복사·오류 입력·최종 수신 전 완료 금지 | [최종 TRX](settings-tests-03.trx), [출력](test-03.log): 103/103, 실패·건너뜀0. 이전 단계79와 합산하지 않는다. | 통과 |
| 실제 파일 설정 | 3×8, CONFIG_AMR_A, 간격100ms, Broker/주기/빈 웹 포트를 파일에만 지정. `(0,7)→(2,7)` 도착,9회, 명목속도10 | [파일 적용](../settings-file-live-20261022-01/file-only-settings-applied.json), [도착](../settings-file-live-20261022-01/expanded-map-mqtt-arrival.json): 기존 열5 한계를 넘는 열7도 실제 관제 수신으로 표시 | 통과 |
| 실제 파일 변경·재시작 | 실행 중 CONFIG_AMR_B·2×3·180ms·목적지(1,2)로 수정. 기존 프로세스는 A 유지, 재시작 후 B 적용·3회 도착 | [유지](../settings-file-live-20261022-01/file-edit-waits-for-restart.json), [재시작](../settings-file-live-20261022-01/restart-applies-edited-file.json), [도착](../settings-file-live-20261022-01/edited-route-mqtt-arrival.json) | 통과 |
| 실제 잘못된 파일 | stepIntervalMs=0, 웹/MQTT 실행 전 오류1 | [오류 결과](../settings-file-live-20261022-01/invalid-file-rejected.json), [stderr](../settings-file-live-20261022-01/invalid.stderr.log) | 통과 |
| 실제 MQTT 회귀 | 기본9회·우회15회·경유 순서·차단대기/제거복귀·불량 격리·취소/초기화·Broker 단절·최초 실패 | [15개 집계](../settings-mqtt-live-20261022-02/verification.json), [실행 출력](mqtt-regression-02.log), [송신/수신/API 대조](../settings-mqtt-live-20261022-02/sender-receiver-api-equality.json) | 통과 |
| 실제 브라우저 | 기존 주소에서 새 설정 위치·0.6초·5×6·W1/W2/G·수신 전 AMR 없음 확인 | [지도](map-settings-ready.png), [설정 안내](map-settings-footer.png). 이번에는 Codex 브라우저에서 직접 확인했다. | 통과 — 기본 지도 준비 화면 |
| 정리·서비스 유지 | 시험 자원만 종료하고 기존 사용자 Broker·지도 유지 | [정리 결과](../settings-mqtt-host-20261022-02/cleanup.json): 종료0·포트 닫힘·소유 Broker 제거·기존 컨테이너 유지. 사용자 지도59344·Broker1884 유지. | 통과 |

새 파일 연동6개와 기존 MQTT 회귀15개, 총21개 실제 시나리오다. [파일 연동 집계](../settings-file-live-20261022-01/verification.json)·[실행 출력](settings-live.log)을 따른다. 파일 연동은 별도 프로세스·격리 Broker로 시험했으며 사용자 설정을 시험용으로 덮어쓰지 않았다. 원본/적용 설정·송수신 원문은 각 API 증거의 evidenceDirectory와 서버의 config.json/effective-settings.json/grid-navigation.jsonl에 연결된다.

## 발견·수정과 재시험 이력

1. 최초 자동시험 빌드에서 새 시험 파일의 xUnit using 누락을 발견해 수정했다. 제품 시험 실행 전 컴파일 실패이며 통과로 집계하지 않는다. 수정 후 [101건 결과](settings-tests-02.trx)를 보존한다.
2. 첫 실제 MQTT 회귀에서 계산상 도착을 외부에 먼저 노출해, 최종 수신을 기다리기 전에 도착으로 읽히는 시점을 재현했다. [당시 실패](../settings-mqtt-live-20261022-01/verification.json)와 [출력](mqtt-regression.log), 당시 서버 원문은 `../mission-live-20261022-111025-ce3e9013813643b1b5c1e5c7feb5916e/grid-navigation.jsonl`에 보존한다. 좌표는 곧 정상 도착했지만 완료 판정 순서가 잘못됐으므로 실패로 유지한다.
3. `GridNavigationSession`이 동일 잠금 안에서 계산 도착 직후 WaitingForTelemetry를 노출하도록 수정했다. 초기 위치가 이미 모든 목표인 경우도 같은 처리다. 최종 좌표·시각·IDLE·속도0 수신과 연결 정리 후 Completed를 표시한다. 송신 직전 외부 상태를 읽는 결정적 회귀2건을 추가해103건·실제15개를 다시 통과했다. 취소/초기화/단절의 소유권은 동일 실행 루프와 기존 정리 책임을 유지한다.

## 선택한 방식·대안·예상 PR 질문

작은 시연에서 사용자가 편집할 파일을 찾기 쉽도록 JSON 한 파일과 설명을 한 폴더에 두었다. 구성값은 시작 때 읽고 검증해 고정한다. 지도와 임무를 다른 파일로 나누면 여러 임무에서 지도를 공유하기 쉽지만 파일 간 경로·버전 일치 관리가 필요하다. 실행 중 자동 반영은 편리하지만 현재 위치가 벽이 되거나 목표 순서가 바뀌는 경우의 정책과 실행 취소·세션 교체 처리가 추가로 필요하므로 이번에는 재시작 방식을 선택했다. 대안의 성능을 별도로 측정한 것은 아니다.

맵과 경유점은 방어적으로 복사하고 초기화는 해당 실행의 설정 지도로 복귀한다. 파일을 다시 읽어 증거를 남기지 않고 처음 읽은 바이트를 복사해 입력·증거 불일치를 피한다. 네트워크 대기는 잠금 밖이며 기존 단일 실행 루프가 이동·발행·종료를 소유한다. 탐색 1회 O(V log V), 지도·탐색 O(V), 경유점 O(W)이며 실행 흔적·증거는 이동/수신 수에 따라 증가한다. 큰 지도·긴 실행의 메모리/화면 성능은 별도 측정하지 않았다.

- **파일을 저장하면 바로 바뀌나?** 서버 재시작 후 적용한다. 브라우저 새로고침이나 지도 초기화로 파일을 다시 읽지 않는다. 파일 자동 반영이 필요해지면 현재 위치·임무 교체 정책부터 정해야 한다.
- **설정값과 MQTT 명령행 값이 다르면 무엇을 쓰나?** 기존 시험 도구를 유지하기 위해 명령행 묶음이 우선한다. 실제 적용값은 effective-settings.json과 API의 mqttEndpoint/stepIntervalMs로 확인한다. 웹 포트0의 실제 선택 주소는 서버 MapReady 출력으로 확인한다.
- **지도 크기만 바꾸면 수신 범위도 달라지나?** 행·열 수를 탐색/화면/수신 검사에 함께 전달한다. 3×8·2×3의 실제 MQTT 연동을 확인했다. 기본 지도 준비 화면은 브라우저에서 직접 확인했으며 변경 크기별 화면 배치·수동 클릭의 시각 시험은 별도로 하지 않았다.

## 남은 범위·인계

설정 수정은 [settings.json](../../../config/map-demo/settings.json), 안내는 [설정 폴더 README](../../../config/map-demo/README.md)를 따른다. 사용자 서버는 파일만 읽는 실행으로 기존 주소에 복구했으며 출발 준비 상태다. 브라우저의 새 화면에서 설정 경로도 확인했다. 화면 클릭 장애물은 파일에 저장하지 않는다.

이번에 전체 솔루션·Simulator151·통신768을 재실행하거나 합산하지 않았다. 실제 로봇·센서·제동·연속 이동·명령/ACK·Core/DB·여러 AMR·장시간/큰 지도·실제망 복구는 미검증/후속 범위다. 기존 .slnx 등록 변경과 사용자 변경을 보존했다. 커밋·푸시·새 Issue·PR·리뷰·병합은 수행하지 않았다.
