# Issue #49 — 다중 AMR 수량·장애 설정 로드와 실행 검증

## 1. 목표와 적용 기준

- 실행일: 2026-10-31 Asia/Seoul. 작업 브랜치 `feature/49-simulator-fleet-settings`.
- 시험 버전: develop `2339df1b1955b26c10f93295fcb3ee2622fc6131` + 이번 변경. [9개 소스/입력·도구 해시](source-files.json), [환경/바이너리](environment.json).
- 목적: 실제 AMR 없이 설정 파일만으로 장비 수와 장비별 초기 위치·속도·경로·장애 조건을 바꾸어 시험한다. 수량은 `amrs` 배열 길이다.
- 기존 단일 `--config`, 격자와 Mock GUI를 유지한다. 사용자 선택으로 장애 발생/원인 해소 시점은 경과 ms 설정이다. 원인 해소와 알람 해제/RESET 관계는 미정이다.
- 이번 완료 범위는 설정 로드·검사, 장비별 유한 이동·주기 송신/정리다. 장애의 실제 State/Alarm 전이는 #50, 명령 수신/ACK는 #51에서 연결한다. `FaultScenarioConfigured Execution=PendingAlarmImplementation`을 명시한다.

## 2. 환경

Windows x64, .NET SDK10.0.401/net10.0, 기존 MQTTnet5.2.0.1603, PowerShell7. Docker Desktop 시작 후 격리 Mosquitto2.1.2-alpine을 사용했다. 정확한 OS/SDK/시각은 environment.json, 실제 포트/컨테이너/입력은 [실제 실행 폴더](../20261031-fleet-settings-live-03/)에 있다. 실제 로봇·GUI 조작·DB는 사용하지 않았다.

## 3. 입력·예상·실제·판정

| 구분 | 입력·예상 | 실제 결과·증거 |
| --- | --- | --- |
| 전체 솔루션 빌드 | 기존 및 새 소스 빌드, 경고/오류0 | 통과: [solution-build.log](solution-build.log) |
| Simulator 자동시험 | 기존202 + 새42, 수량1/2/3·오류·UTF8·설정 불변·장비별 이동·MQTT 대역·취소/동료 실패 정리 | **244/244 통과**, [TRX](simulator-authorized.trx), [출력](tests-authorized-02.log) |
| 기존 지도 자동 회귀 | 변경 Simulator를 참조하는 기존 지도 시험 | **72/72 통과**, [TRX](map.trx), [출력](map-tests.log) |
| 실제 MQTT | 1/2/3대, 서로 다른 ID·Y좌표·속도, 최종X1/IDLE/속도0 | 기존 네 유형 수신 콘솔의 JSON Received에서 ID별 Heartbeat와 최종 Telemetry를 대조. **17조건 통과**, [판정](../20261031-fleet-settings-live-03/verification.json) |
| 불량 설정 실제 콘솔 | 중복ID → 장비/네트워크 생성 전 오류1 | 통과, 실제 실행 폴더의 duplicate 입력/stdout/stderr |
| 자원 정리 | 소유 Broker만 제거·기존 컨테이너 ID/상태 유지 | [cleanup-check](../20261031-fleet-settings-live-03/cleanup-check.json) true. 관찰용 수신 프로세스는 강제 정리했으며 정상 Ctrl+C 시험으로 집계하지 않음 |

이번 실행한 자동시험은 총316건이며 실제 MQTT17조건과 합산하지 않는다. 통신/GUI/Core의 자동시험은 이번에 재실행하지 않았고 전체 자동시험 통과라고 표시하지 않는다. MQTT 대역 자동시험과 실제 Broker 수신 결과도 구분한다.

## 4. 실패 기록과 한계

- 첫 표준 시험은 결과 없이 대기해 명령행·부모PID로 이번 testhost를 확인한 뒤 종료했다. [tests-01.log](tests-01.log), [중단 TRX](simulator.trx)를 보존한다. 원인은 확정하지 않으며 허용된 환경에서 동일 어셈블리244건이 통과했다.
- 실제 첫 실행은 Docker 명령 경로가 두 개 검색되어 프로세스 시작 전에 실패했다. 검증 도구에서 첫 Application 경로만 선택하도록 수정했다. 제품 실패로 집계하지 않는다.
- [실제02](../20261031-fleet-settings-live-02/)는 수신 콘솔의 출력을 예전 문자열 형식으로 가정해 판정 실패했다. 원시 JSON 로그에는 수신이 있었으며 JSON Received·최종 위치/상태를 대조하도록 도구를 고친 뒤 실제03에서 통과했다. 실패 기록을 삭제하거나03과 합산하지 않는다.
- 실제03 verification.json의 Started는 실행 종료 시 판정 기록을 작성한 시각이다. 정확한 메시지 시각은 송수신 로그에 있다.
- QoS0은 종단 간 유실/중복 보장을 제공하지 않는다. Published 반환만으로 수신을 판정하지 않았다. 부하/장시간·실제 차량 안전·다중 충돌 방지·재접속은 미검증이다.
- 한 장비의 수신/송신 실패에는 전체 fleet 실행을 실패 종료하고 모든 소유 작업의 취소·정리를 기다린다. 정상 도착한 장비의 유한 송신은 종료된다. 장시간 명령 대기 세션은 후속이다.

## 5. 설계 선택과 검토 질문

기존 단일 설정/Alarm 검사기와 이동·MQTT 세션을 재사용하고, 전체 설정 검사 뒤 장비별 작업을 조립했다. 별도 fleet 프로세스/장비별 전용 스레드나 새로운 패키지는 추가하지 않았다. 같은 설정 바이트를 먼저 검증하므로 뒷 장비 오류로 일부만 기동하는 것을 막는다.

- 대안: 장비마다 외부 프로세스를 생성하면 프로세스 장애 격리가 쉽지만 실행/취소·자원 관리 비용이 늘어난다. 현재 소규모 시연은 한 프로세스의 비동기 작업으로 구성했다. 성능·메모리 우열은 측정하지 않았다.
- 왜 수량 필드가 없는가? 명시된 설정 배열 길이를 수량으로 사용해 별도 count와 불일치하거나 ID를 암묵 생성하는 문제를 피한다.
- 장애를 설정했는데 알람이 발생하는가? 이 Issue는 설정 계약까지다. Pending 로그와 문서로 구분하고 #50에서 전이를 연결한다. 원인 해소/알람 해제 정책을 임의 확정하지 않는다.
- 한 장비가 실패하면 다른 장비는? 서로 다른 상태/ClientId를 소유하지만 이번 프로세스 실행은 실패로 끝내며 모두 정리한다. 독립 재시작/장기 유지 요구가 생기면 감독 정책을 별도 설계한다.

## 6. 재현

```powershell
dotnet build AmrControlSystem.slnx --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test tests/Amr.Simulator.Tests --no-build --no-restore
dotnet test tests/Amr.Simulator.Map.Tests --no-build --no-restore
pwsh -NoProfile -File tests/manual/AMR01/Verify-FleetSettings.ps1 -RunId new-fleet-run
```

매번 새 실행 ID를 사용한다. 실제 MQTT에는 실행 중인 Docker Linux 엔진과 기존 캐시 이미지가 필요하다. 새 콘솔 실행은 `src/Amr.Simulator/README.md`의 `--fleet-config` 안내를 따른다.
