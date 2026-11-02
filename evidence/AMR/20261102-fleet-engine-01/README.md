# AMR-01~05 Fleet 공통 실행 구현·검증 — 20261102-fleet-engine-01

## 1. 목표와 적용 기준

사용자 요청에 따라 단일 명령 모드의 이동·상태·장애/알람·최종 응답을 장비별 공통 실행 구조로 확장했다. **요청한 독립 Fleet 실행 범위는 구현·검증 통과**다. 과제2 전체(A* 비교 등), GUI 다중 장비 제어, 과제5 전체 통합 완료 판정과 구분한다.

- 기준: 요구사항정의 v0.6 R-02/R-03, 기능명세 v0.14 AMR-01~05, 공통규칙/데이터 v0.12, 결정사항 v0.17, 과제2 계획 v0.20 + 2026-11-02 사용자 장애/RESET·Fleet 추가 결정. 원문 회사 PDF는 앞선 점검에서 접근되지 않아 사용자 제공 기준과 원본 Markdown을 적용했다.
- 이번 반영: 단일 명령 모드·Fleet route/command 공통 엔진. 단일 `--config` 자동 경로와 별도 격자 시연은 기존 모드다. 기존 사용자 변경·미추적 자료와 branch를 유지했다.
- 버전: `feature/50-fault-reset`, HEAD `4aa0b8a3ba4941b872f30ce1619b18d106023daa` + 미커밋 변경. `before.patch`·`before-hashes.json`은 이번 확장 직전 단일 장애/RESET 구현 기준선이다. 이번 커밋·게시·PR·병합·Issue 종료는 없다. 관련 Issue #50/#51은 요청 범위와 연결하며 전체 완료로 닫지 않는다.

## 2. 구현과 수명·동시성

| 단계 | 실제 코드 | 변경·선택 이유 |
| --- | --- | --- |
| 1 설정 | [FleetSettings](../../../src/Amr.Simulator/Configuration/FleetSettings.cs), [Loader](../../../src/Amr.Simulator/Configuration/FleetSettingsLoader.cs) | mode=route/command, 기존 생략 기본 route, 장비별 faults. 전체 검사 후 생성 |
| 2 도메인 | [CommandDrivenAmr](../../../src/Amr.Simulator/Simulation/CommandDrivenAmr.cs), [FaultScenarioRunner](../../../src/Amr.Simulator/Simulation/FaultScenarioRunner.cs) | 기존 상태/RESET 계약 재사용. 초기 설정 경로는 외부 명령 없이 한 번 시작하며 가짜 ACK를 만들지 않음 |
| 3 실행 소유자 | [AmrDeviceContext](../../../src/Amr.Simulator/Execution/AmrDeviceContext.cs) | 한 장비의 상태·큐·시계·CTS·전송·실행 Task 소유. 콜백은 검사/큐 접수, 루프만 도메인 변경 |
| 4 Fleet 조립 | [FleetApplication](../../../src/Amr.Simulator/Execution/FleetApplication.cs) | 각 장비의 ClientId·연결 분리. 한 장비 실패로 부모 CTS를 취소하지 않음. 전체 종료 시 Task/Dispose 모두 대기 |
| 5 통신 | [IAmrDeviceTransport](../../../src/Amr.Simulator/Telemetry/IAmrDeviceTransport.cs), [MqttCommandSimulationSession](../../../src/Amr.Simulator/Telemetry/MqttCommandSimulationSession.cs) | MQTT 연결·동적 구독/발행만 담당. 기존 단일 RunAsync 진입점도 같은 컨텍스트 사용 |
| 6 실행 입력 | [예제 JSON](../../../config/simulator.fleet-control.json), [Git Bash·Explorer 절차](../../../tests/manual/FLEET-CONTROL/README.md) | 2대 command, A 장애30초/해소60초, B 정상. mode별 동작·수치·실패 판정 제공 |

각 장비는 ThreadPool의 비동기 Task 하나로 실행되며 전용 OS 스레드를 만들지 않는다. 개별 큐/시계/연결은 상태와 작업 수명을 분리한다. CPU·ThreadPool·콘솔·Broker는 공유하므로 하드 실시간 정지나 무제한 성능 보장은 아니다. 한 장비에서 느린 발행은 그 장비의 다음 tick을 늦출 수 있다. 단조 시간 계산은 늦은 tick에서도 장애 예정 시점까지만 이동시킨다.

장비별 연결 방식은 연결 수·자원을 더 쓰지만 독립 수명과 실패 처리가 단순하다. 단일 Client 다중 Topic 방식은 연결 수를 줄이는 대안이지만 공유 연결 실패·라우팅·종료의 결합이 생긴다. 현재 3대 검증에 맞추어 전자를 사용했으며 장비 수·메모리·처리량 부하 측정 후 재검토한다.

종료 시 접수 중지 → 콜백 해제 → 구독 해제 → 연결 종료·Dispose → 잔여 큐 정리 후 반환한다. 이미 캡처된 늦은 단절 콜백도 정상 종료를 실패로 바꾸지 않도록 closing 상태로 차단한다. 단일 연결 종료·큐 초과 시 해당 장비만 실패하며 정상 장비는 실행을 계속한다. 전체 종료 코드는 장비 실패가 있으면1, 없으면0이다.

## 3. 환경과 재현 입력

Windows, .NET SDK10.0.401, net10.0, MQTTnet5.2.0.1603, 구성 FleetEngine. 독립 EXE + Docker Mosquitto2.1.2 + 범용 mosquitto_pub/sub로 시험했다. 기존 사용자 GUI·127.0.0.1:1884 Broker·Seq를 유지했다.

- 최종 Fleet 실제 시험: **2026-11-02 16:59:35~17:00:05 KST**, 전용 `127.0.0.1:57292`. [환경·결과](mqtt-02/results.json) · [설정](mqtt-02/fleet.json) · [인자](mqtt-02/simulator-arguments.json) · [소스 해시](mqtt-02/source-hashes.json).
- A/B command, R route. A=(4,2), B=(0,5), 각2m/s. R=(0,10), 5m/s, (1,10)→(1,11). A 장애15초/해소23초. Telemetry100ms, Heartbeat250ms. QoS0·Retain=false.
- MQTT 없는 로컬 경로는 자동시험에서 장비 수1/2/3 및 각 최종 좌표로 확인했다. 실제 독립 Fleet 프로세스 검증은 MQTT 경로다.
- [자동 실행 스크립트](../../../tests/manual/FLEET-CONTROL/verify.ps1)는 새 Evidence 경로를 요구하고 시험 소유 컨테이너/프로세스만 정리한다. Ctrl+C는 PID·시작 시각·콘솔 소유를 확인한 기존 도구로 전송한다.

## 4. 입력 → 예상 → 실제 결과

| 시험 | 입력/예상 | 실제·판정·근거 |
| --- | --- | --- |
| 설정·독립 실행 | 3대 JSON, GUI/Core 참조 없음 | 독립 EXE 생성·실행, runtime dependency closure에 GUI/Core/Infrastructure 없음. FLEET-01/02 통과 |
| 모드·경로 | command 대기, route 경유점2개·도착 후 Heartbeat | A/B IDLE, R 도착(1,11) 후 계속 송신, 자동 경로 ACK 없음. FLEET-02 통과 |
| 대상·ID | A/B 동일 cmdId, B STOP·BUSY RESET | B의 결과만 반환, A 이동 유지. FLEET-03 통과 |
| 장애 격리 | A15초 장애, B 이동 중 | A FAULT/속도0/ACTIVE/원 MOVE 실패, B 좌표·주기 메시지 계속 갱신. FLEET-04 통과 |
| RESET | 미해소 실패, 원인 해소 후 새 ID | 원인 해소 후 FAULT 유지, 새 RESET에서 CLEARED·같은 좌표 IDLE·SUCCESS. FLEET-05 통과 |
| 최종 결과 | 정상 세션 명령10건 | 10건 최종 ACK, 장비 ID/cmdId/사유·Topic/payload ID 대조, Alarm ACTIVE/CLEARED 각1. FLEET-06 통과. [ACK 매핑](mqtt-02/ack-mapping.json) |
| 전체 종료 | 소유 Ctrl+C | 3장비 종료·프로세스0·stderr 없음. FLEET-07 통과. [종료](mqtt-02/normal-shutdown.json) |
| 연결 실패 격리 | R ClientId 중복 접속으로 R만 단절 | R 종료1, B 신규 MOVE 도착·ACK 유지, 전체 종료1로 집계. FLEET-08 통과. [단절 로그](mqtt-02/disconnect.log) |
| 자동 동시성/회귀 | 장비별 큐·동시 콜백·시계·발행 지연·종료 | Simulator **271/271**, GUI **85/85**, Map **72/72**, 합계 **428/428** 통과. 아래 TRX |
| 기존 단일 GUI MQTT | Start→fault→실패RESET→해소→성공RESET→MOVE | 실제 WinForms + 동일 프로세스의 simulator Task + 전용 Broker18889, **FR-01~10 통과**. 독립 두 프로세스 수동 클릭 시험과 구분. [결과](single-gui/results.json) |

[Simulator TRX](fleet-simulator-shutdown.trx) · [GUI TRX](gui-regression.trx) · [Map TRX](map-regression.trx) · [원본 MQTT](mqtt-02/mqtt.log) · [시뮬레이터](mqtt-02/simulator.log) · [Broker](mqtt-02/broker.log) · [전후 컨테이너](mqtt-02/containers-after.log).

## 5. 실패 기록과 검증 한계

- 샌드박스의 기존 파일/폴더 쓰기·bin/obj 생성 제한을 확인하고 권한을 받아 수행했다. 제품 오류로 집계하지 않는다.
- 새 시험의 첫 컴파일 오류를 수정했다. 최초 실행은 270건 중1건이 시험의 메시지 개수 대기 조건 때문에 실패했다. [최초 TRX](fleet-simulator.trx)를 보존하고 실제 BUSY 상태를 기다리도록 수정해 [270건 재시험](fleet-simulator-final.trx)이 통과했다.
- 종료 중 늦은 콜백을 별도로 검토해 방어와 시험1건을 추가했다. 최종271건과 최종 실제 mqtt-02가 이를 포함한다. mqtt-01의8항목 성공은 수정 전 근거로 보존한다.
- 큐 초과·발행 정체·동시 콜백은 대역 시험이다. 실제 Broker 과부하·대규모 Fleet·CPU/메모리 성능 측정으로 확대하지 않는다.
- QoS0/Retain=false, 자동 재접속·재전송 없음. 정상 연결에서 최종 ACK를 받았다는 사실은 단절 중 미처리 큐·송신 중 ACK 전달 보장이 아니다. 장비별 요청 기록128은 세션 수명 동안 보존하며 영속 복구·만료 정리는 없다.
- 설정 route는 STOP/FAULT 후 자동 재개하지 않으며 MQTT에서는 도착 후 프로세스가 계속 대기한다. 기존 유한 Fleet 시험 스크립트는 과거 기준이고 새 runbook을 사용한다. 로컬 FAULT는 RESET 입력이 없으므로 Ctrl+C가 필요하다.

## 6. 예상 리뷰 질문과 답변

1. **단일 AMR과 Fleet이 다른 상태 로직을 갖는가?** 단일 명령 진입점도 AmrDeviceContext를 생성한다. CommandDrivenAmr/FaultScenarioRunner/최종 ACK 경로가 같다. 기존 단일 자동 경로·격자 엔진까지 합치지는 않았다.
2. **한 장비 실패가 왜 전체를 종료하지 않는가?** 사용자 요구인 독립 실행에 맞춰 장비 CTS만 취소한다. 정상 장비는 부모 취소까지 유지하고 마지막에 실패를 집계한다. Broker 전체 장애는 모든 장비 연결에 영향을 줄 수 있다.
3. **장비 수를 크게 늘리면 안전한가?** 이번 실제 근거는3대다. 연결·큐·기록은 장비 수만큼 증가하고 ThreadPool/Broker는 공유한다. 부하 목표가 정해지면 계측 후 단일 Client 라우팅·전송 분리 등을 검토해야 한다.

제품 구현·시험은 로컬 완료이며 원격 반영은 별도다. 원본 계획·명세·업무 기록은 C:/process-plan에서 관리하고 이 저장소에 중복 원본을 만들지 않는다.

위 게시 상태는 시험 당시 기록이다. 후속 GitHub 게시 범위는 [게시 점검 기록](../../GIT/20261102-fleet-publication-01/README.md)을 따른다. `before.patch` 원문 백업은 로컬에 보존하며, 소스 해시·보존 감사·시험 결과를 게시 근거로 제공한다.
