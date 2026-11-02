# 장애·Alarm·RESET 구현 검증 — 2026-11-02

실행자: Codex. 사용자 요청으로 단일 명령 대기 AMR의 장애 주입과 RESET 연동을 구현하고 검증함.
관련 기능: AMR-01~05, GUI 알람·명령 표시, CORE-04 요청 추적. 관련 Issue: [#50](https://github.com/inhhlee/amr-control-system-demo/issues/50), [#51](https://github.com/inhhlee/amr-control-system-demo/issues/51).
이번 로컬 단일 장비 결과로 두 Issue 전체나 과제 전체를 완료 처리하지 않음.

## 적용 기준·변경 범위

- 적용 기준: 2026-11-02 본 대화에서 사용자가 지정한 장애 즉시 정지, 원인 해소와 알람 해제 분리, 명시적 RESET 복구 규칙. 기존 문서의 RESET 미정 기록보다 이 사용자 결정을 적용함.
- 기존 코드 계약 유지: MQTT5, QoS0, Retain=false, `amr/{AmrId}/{kind}`, ACK 결과 `SUCCESS/FAILURE`, 요청 cmdId, 기존 STOPPED 및 STOP 예약 용량 정책.
- 단일 설정의 선택 `faults`를 기존 Fleet 설정 검사기로 읽음. FaultScenarioRunner가 Stopwatch를 Poll하며 MQTT 콜백은 상태를 변경하지 않음.
- 장애: 속도 0·FAULT·ACTIVE, 진행 MOVE는 FAILURE/FAULT_INTERRUPTED. 원인 해소만으로 상태/알람이 바뀌지 않음.
- RESET: BUSY/미해소/비장애 상태는 거부. 모든 원인이 해소된 FAULT에서 CLEARED·현재 좌표의 IDLE·RESET SUCCESS 생성. 이전 MOVE는 재개하지 않음.
- 동일 코드의 겹친 원인은 시나리오 인덱스로 구분. 관제 알람 캐시는 `(AmrId, Code)` 키 사용. 명령 대기 Fleet 확장을 뜻하지 않음.
- Core CommandTracker는 이미 RESET 종류와 대상/ID 대조를 지원하여 생산 코드를 중복 수정하지 않음. RESET에 대한 잘못된 대상·중복 ACK·원 MOVE 실패 보존 시험을 추가함.
- GUI MainForm/별도 실행창에 실제 MQTT RESET 버튼 연결. 기존 `InvokeAsync` UI 전달 경계를 유지하며 활성 오류 알람·미확인 RESET 중에는 이동을 차단함.

## 실행 환경·버전

| 항목 | 실제 값 |
| --- | --- |
| 기준 커밋 | `4aa0b8a3ba4941b872f30ce1619b18d106023daa` + 미커밋 변경 |
| 브랜치 | `feature/50-fault-reset` |
| 코드 위치 | `C:\amr-control-system-demo` |
| 변경 파일 식별 | [source-hashes.json](source-hashes.json)의 SHA-256. 커밋·푸시·PR·병합 미실행 |
| OS / SDK / Runtime | Windows 10.0.26200 / .NET SDK 10.0.401 / Runtime 10.0.12 |
| 구성 | `FaultReset` 별도 빌드. 기존 실행 중인 Debug GUI 파일 잠금 회피 |
| 최종 실제 연동 실행 | `20261102-fault-reset-live-04`, 2026-11-02 15:47:44~15:47:54 KST |
| Broker | Docker `eclipse-mosquitto:2.1.2-alpine`, 전용 `127.0.0.1:18887` |
| 실제 구성 | 실제 WinForms·MqttAmrDataProvider·CommandTracker·Simulator 명령 세션·Mosquitto |
| 시험 배치 | GUI는 STA, Simulator는 같은 시험 프로세스의 별도 Task. 소켓은 실제 MQTT 사용 |
| 입력 | [simulator.config.json](../20261102-fault-reset-live-04/simulator.config.json), AMR_001, 시작 (4,2), 목표 (100,2), 속도 1, 발생 4초/원인 해소 8초 |
| 정리 | 시험용 컨테이너 종료·제거 확인. 기존 Mosquitto 1884·Seq 5341 유지 |

## 자동 시험

각 명령에 `-c FaultReset --no-restore --logger "trx;LogFileName=..." --results-directory evidence/AMR/20261102-fault-reset-01` 적용.

| 실행 | 실제 결과 | 증거 |
| --- | --- | --- |
| `dotnet test tests/Amr.Simulator.Tests` | 256 통과, 실패/생략 0 | [최종 TRX](simulator-final.trx) |
| `dotnet test tests/Amr.Core.Tests` | 17 통과, 실패/생략 0 | [TRX](core.trx) |
| `dotnet test tests/Amr.ControlGui.Tests` | 85 통과, 실패/생략 0 | [최종 TRX](gui-display-final.trx) |
| 최종 E2E 빌드 | 경고 0, 오류 0 | [build.log](../20261102-fault-reset-live-04/build.log) |

합계 358개. 지연 Tick의 장애 시점 절단, 같은 코드 중첩 원인, 해제 후 재발생, 0시점 장애,
도착/장애 동시 시각 순서, BUSY RESET, STOP의 FAULT 우회 방지, 새 RESET ID,
좌표 보존, 중복 ACK, 기존 이동/설정/GUI 회귀를 포함함. 단위 대역 시험과 아래 실제 MQTT를 구분함.

## 실제 MQTT·GUI 결과

실행 명령:

```powershell
./tests/manual/FAULT-RESET/verify.ps1 -Docker 'C:/Users/Admin/AppData/Local/Programs/DockerDesktop/resources/bin/docker.exe' -Evidence 'C:/amr-control-system-demo/evidence/AMR/20261102-fault-reset-live-04'
```

| 시험 | 입력·예상 | 실제·판정 |
| --- | --- | --- |
| FR-01 | GUI 시작 → MOVE·BUSY, 완료 전 ACK 성공 없음 | 동일 ID 요청 미확인, BUSY 수신 — 통과 |
| FR-02 | BUSY 중 직접 RESET → 실패, 이동 유지 | BUSY_RESET_REJECTED — 통과 |
| FR-03 | 4초 장애 → 정지·FAULT·ACTIVE·원 MOVE 실패·GUI 경고 | 속도 0, 빨간 FAULT/알람, 시작 비활성 — 통과 |
| FR-04 | MainForm RESET, 원인 지속 → 실패·알람 유지 | FAULT_CAUSE_ACTIVE, RESET 요청 1개 — 통과 |
| FR-05 | 8초 원인 해소만 → FAULT·알람 유지 | 좌표 고정·Heartbeat 생존 유지 — 통과 |
| FR-06 | 실행창의 새 RESET → CLEARED·IDLE·SUCCESS | RESET_COMPLETED, 요청 총 2개, 좌표 보존·시작 활성 — 통과 |
| FR-07 | 성공 RESET 동일 ID 재전송 | 캐시 ACK 재송신, CLEARED 1회 유지 — 통과 |
| FR-08 | 복구 후 GUI 새 MOVE | 보존 좌표에서 +1m 이동·ARRIVED — 통과 |
| FR-09 | 실제 수신 규격 검사 | 거부 메시지 0, Heartbeat/Alarm 수신, Retain=false — 통과 |
| FR-10 | Simulator 취소 | unsubscribe/disconnect 대기 후 ExitCode=0 — 통과 |

[기계 판정](../20261102-fault-reset-live-04/results.json) · [원본 MQTT](../20261102-fault-reset-live-04/mqtt.jsonl) ·
[시뮬레이터 로그](../20261102-fault-reset-live-04/simulator.log) · [최종 수신 상태](../20261102-fault-reset-live-04/final-snapshot.json)

[FAULT 대시보드](../20261102-fault-reset-live-04/01-fault-dashboard.png) ·
[미해소 RESET 실패](../20261102-fault-reset-live-04/02-reset-rejected.png) ·
[RESET 복구](../20261102-fault-reset-live-04/03-reset-recovered.png)

## 이전 시도·한계

- 최초 Debug 빌드: 사용자가 실행 중인 GUI PID 23204의 DLL 잠금으로 복사 실패. 프로세스를 종료하지 않고 FaultReset 구성으로 분리하여 해결함.
- [live-01](../20261102-fault-reset-live-01/results.json): FR-01~04 통과 후 시험 도구의 열린 로그 읽기 공유 모드 오류. 실패 기록 보존, FileShare.ReadWrite로 수정함.
- [live-02](../20261102-fault-reset-live-02/results.json): 10항목 통과했으나 예정하지 않은 추가 RESET 1건이 원본 로그에 존재함. 원인은 확정하지 않음. 화면 입력을 분리하고 요청 개수 검사를 강화한 live-03에서 재확인함. 최종 화면 검토에서 지도 옆 설명의 FAULT/대기 불일치를 추가 수정하고, 해당 표시와 복구 표시까지 검사한 live-04를 최종 근거로 채택함.
- 도착과 장애가 정확히 같은 시각이면 경계까지 이동/도착을 먼저 확정하고 FAULT를 적용함. 이미 성공한 MOVE를 실패로 덮지 않음.
- 설정의 `faults`는 작업 중단 장애 주입임. INFO/WARN 등급을 지정해도 이 시나리오의 FAULT 전이는 동일함. 이번 실제 시험은 ERR 사용. 일반 비중단 경고 발생기는 별도 범위임.
- 단일 루프의 MQTT 발행 지연은 다음 Tick을 늦출 수 있음. 단조시계의 사건 시점 계산이며 실제 장비의 하드 실시간 안전 정지 보장은 아님.
- QoS0의 ACTIVE/CLEARED/Telemetry/ACK는 원자적 묶음이 아님. 발행 성공을 관제 수신 성공으로 간주하지 않으며, 유실 시 UI의 미확인 결과를 임의 성공으로 바꾸지 않음. 재접속 후 알람 전체 재동기화·프로세스 재시작 간 중복 보장은 추가하지 않음.
- 명령/중복 결과는 세션 내 128건(기존 STOP 예약 1자리), 알람은 관제 256코드 한도 유지. 원인 해소 후 기존 실패 ID로 재시도하면 같은 실패가 재생됨.
- 여러 AMR의 실제 동시 명령·장애 격리, Fleet 자동 경로의 장애 실행, 실제 장비 센서/복구, DB 이력, 독립 두 프로세스의 수동 클릭 절차 실행은 이번 근거에 포함하지 않음.
- 계획·공통 기능명세·결정사항 원본의 정책/진도 반영은 별도 문서 후속이며, 오늘 일일업무에는 이번 사용자 결정과 실제 코드 증거를 연결함. Issue #50/#51 전체 완료나 원격 병합 완료를 주장하지 않음.

## 설계 이유·리뷰 참고

1. **왜 단일 루프인가?** 기존 명령 큐 소유권을 유지하여 장애·MOVE·RESET 경합을 한 순서에서 판정함. 별도 타이머+Lock은 송신 지연과 상태 보호를 분리할 수 있으나 스레드/잠금·취소 책임이 늘어남. 더 엄격한 지연 상한이 필요하면 bounded 송신 큐·불변 이벤트를 별도 검토해야 함.
2. **왜 ACK만으로 UI를 복구하지 않는가?** 최종 응답과 Telemetry/Alarm은 다른 메시지임. ACK는 요청 결과만 확정하고, 수신 상태와 활성 알람을 따로 보존함. 원자적 복구가 필요하면 프로토콜 세대/스냅샷 계약이 먼저 필요함.
3. **왜 같은 RESET 실패를 다시 반환하는가?** 같은 ID의 요청을 재실행하지 않는 기존 계약을 유지함. 원인 해소 뒤 새 사용자 행동은 새 ID이며, 시뮬레이터 재시작 이후까지 중복 보존하는 저장소는 범위 밖임.

직접 실행은 [Git Bash Runbook](../../../tests/manual/FAULT-RESET/README.md)을 따른다.
