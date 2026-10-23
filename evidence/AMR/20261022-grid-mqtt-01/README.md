# 격자 장애물 회피 → MQTT → 관제 수신 지도 검증

- 실행자: Codex. 실행일: 2026-10-22 KST.
- 코드: `a8934f0bfed803cee47d7c2e13775ba648e0862a` + 미커밋 변경, `feature/33-simulator-movement`. 앞선 격자 구현을 이어 확장했다. 정확한 파일 SHA256·환경·시험 집계는 [source-version.json](source-version.json).
- 요청: 장애물을 피해 이동하는 좌표를 MQTT로 보내고 관제가 받은 좌표로 기존 격자 지도에 표시한다.
- 단계: 로컬 구현·검증·실행 안내·업무 기록까지. 커밋·푸시·Issue 생성·PR·병합은 미수행이다. 이전 사용자 변경과 원격 이력을 보존했다.

## 1. 적용 기준과 구현 범위

이번 사용자 요청과 로컬 원본 과제2 과제계획 v0.12, 기능명세 v0.9 AMR-02·05, 공통데이터명세 v0.6의 기존 Telemetry·Heartbeat 계약, 결정사항 v0.9 D-02·05·12를 참조했다. GitHub가 참조하는 더 최신 문서를 읽은 것으로 표시하지 않는다. 원문 PDF·HWP는 이번에 재대조하지 않았다. 공식 새 기능 ID·Issue·추적기의 `spec_revision`은 부여하지 않고 실행별 근거와 소스 manifest로 연결한다.

새 코드 `Navigation/GridTelemetry.cs`에서 시연용 **1칸=1m, X=열, Y=-행**을 정의했다. 도착은 Telemetry **(5,-4)**, 지도 **(4,5)**다. 이는 기존 m·m/s 계약으로 연결하기 위한 이번 시연 선택이며 운영 지도 원점·실제 로봇 규격을 확정한 것이 아니다. 기존 제품의 `src/`·메시지 형식·QoS·Retain·운행 State/Alarm 정책은 변경하지 않았다.

`GridMqttConnection`은 기존 `MqttMapReceiver`의 **구독·메시지 검사·AMR별 큐**와 별도 `MqttSimulationTransport` 송신 Client를 사용한다. 관제 수신기는 지도 서버 프로세스 안에 있다. 별도 Core·DB·전체 관제 GUI의 완료로 확대하지 않는다. 서버마다 고유 AMR ID를 사용해 다른 지도 서버·기존 경유점 AMR의 위치가 섞이지 않게 했다.

`GridNavigationSession`은 실제 수신 모델만 `position`과 `receivedTrail`로 내보낸다. 송신·계산 좌표로 이를 채우지 않는다. 경로·이동 횟수는 시뮬레이터 계산값으로 구분한다. 첫 수신 전에는 AMR 표시가 없고, 목적지의 최종 발행 시각·좌표·IDLE·속도 0과 일치하는 Telemetry 수신 및 자원 정리가 끝난 뒤 완료로 표시한다. 발행 반환만으로 도착을 판정하지 않는다.

## 2. 실행 환경과 절차

| 항목 | 실제 값 |
| --- | --- |
| 코드 위치 | C:\amr-control-system-demo |
| 런타임 | Windows, SDK 10.0.401, net10.0, 기존 MQTTnet 5.2.0.1603·xUnit 재사용. 새 패키지 없음 |
| 시연용 Broker | 기존 `amr-demo-mosquitto-1`, 127.0.0.1:1884. 설정 bind는 docker/mosquitto/config/mosquitto.conf이며 해당 Broker를 중단하지 않음 |
| 시연 서버 | http://127.0.0.1:59344/?mode=grid. [교체 전 지도](before-restart.json)의 추가 장애물을 복원하고 재출발 가능한 상태로 기동 |
| 격리 시험 | Mosquitto 2.1.2-alpine, 임시 Broker 49519, 지도 서버 49521. [세션 환경](../20261022-grid-mqtt-host-01/session.json) |
| 주기 | 이동 600ms, 실제 시험 Telemetry 200ms·Heartbeat 1000ms. 같은 루프의 별도 시각 기준으로 송신하며 이동 때마다 추가 Telemetry 발행 |
| 계약 | amr/{id}/telemetry·heartbeat, 기존 JSON, MQTT 5·QoS 0·Retain=false. [Broker 원본](../20261022-grid-mqtt-host-01/broker.log)에 q0·r0와 서로 다른 송신/수신 Client 확인 |
| 실제 구성 | 격자 이동 + 실제 MQTT 송신·Broker·관제 검사/큐 + HTTP 상태. 자동시험의 FakeConnection과 별도 |
| 화면 확인 | 서버 교체 중 오류 페이지로 바뀐 탭은 자동 브라우저 정책으로 조작이 차단됨. 사용자가 주소를 다시 열어 화면 표시를 확인했다고 응답. 새 화면의 자동 시각 검증·스크린샷은 미실행이며 API·표시 소스 검증과 구분 |

기본 실행:

```powershell
dotnet build tools/Amr.Simulator.Map
dotnet run --no-build --project tools/Amr.Simulator.Map -- --port 59344 --mqtt-host 127.0.0.1 --mqtt-port 1884 --telemetry-ms 200 --heartbeat-ms 1000
```

재검증은 사용자 서버를 조작하지 않도록 별도 세션을 사용한다. 아래 ID는 매번 새 값으로 지정한다.

```powershell
dotnet test tests/Amr.Simulator.Map.Tests/Amr.Simulator.Map.Tests.csproj
pwsh -NoProfile -File tests/manual/AMR01/Start-MqttMap.ps1 -RunId <새-호스트ID>
pwsh -NoProfile -File tests/manual/AMR01/Verify-GridMqttMap.ps1 -SessionRunId <새-호스트ID> -RunId <새-검증ID>
pwsh -NoProfile -File tests/manual/AMR01/Stop-MqttMap.ps1 -RunId <새-호스트ID>
```

## 3. 입력·예상·실제 결과

| 구분 | 입력·예상 | 실제 결과·판정 | 증거 |
| --- | --- | --- | --- |
| 빌드 | 지도 프로젝트 성공 | 경고 0·오류 0, 통과 | [build.log](build.log) |
| 자동시험 | 기존 지도36 + 신규 MQTT19. 수신 전 null, 송신 대체 금지, 좌표 변환, 불량/타 AMR 격리, 최초/직후 단절, 대기 중 독립 송신, 취소·초기화의 실제 정리 대기, 최종 미수신 실패 | 55건 통과. 최종 수신을 버린 대역 시험은 계산상 도착해도 Failed이며 표시 좌표는 마지막 수신 칸 유지 | [TRX](grid-mqtt-tests-02.trx), [로그](test-02.log) |
| 실제 MQTT 기본 | 동일 맵, 9회 이동, (4,5) 수신·정지 확인 | 9회, ReceivedTrail 10칸, 최종 시각 일치·IDLE·속도0, 통과 | [기본 도착](../20261022-grid-mqtt-live-01/normal-arrival.json) |
| 실제 MQTT 우회 | 이동 후 (2,2) 차단, 아래 통로 우회 | 11회, ReceivedTrail 12칸, 차단 칸 수신0, 통과 | [우회](../20261022-grid-mqtt-live-01/moving-detour.json) |
| 실제 MQTT 차단·복귀 | (1,1) 차단 시 이동0·IDLE·속도0, Heartbeat 지속. 제거 후 Start 재요청 없이 이동 | 대기 중 두 송신 주기 유지, 자동 재출발·9회 도착, 통과 | [대기](../20261022-grid-mqtt-live-01/blocked-heartbeat.json), [복귀](../20261022-grid-mqtt-live-01/automatic-resume.json) |
| 실제 MQTT 불량 격리 | 잘못된 State, 반 칸 좌표, 타 AMR (999,999) | 두 입력 거부, 타 AMR 무시, 표시 (0,0) 유지·실행 실패 없음, 통과 | [입력 격리](../20261022-grid-mqtt-live-01/invalid-isolation.json) |
| 실제 MQTT 중단·초기화 | Cancel 뒤800ms 추가 이동·발행 없음. 이전 연결 종료 후 Reset 수신값 비우기 | Canceled 뒤 변화0, Ready에서 position null·수신0, 통과 | [취소](../20261022-grid-mqtt-live-01/cancel-cleanup.json), [초기화](../20261022-grid-mqtt-live-01/active-reset-no-stale-position.json) |
| 실제 MQTT 연결 후 단절 | 소유 시험 Broker만 중단, 이동 정지·마지막 수신 유지 | Failed, 단절 사유·위치 유지, 800ms 추가 이동0, 통과 | [단절](../20261022-grid-mqtt-live-01/broker-loss.json) |
| 실제 MQTT 최초 실패 | 중단된 Broker에 새 실행 | ConnectionFailed, 이동0·position null, 통과 | [접속 실패](../20261022-grid-mqtt-live-01/initial-connect-failure.json) |
| 송신↔수신↔지도 응답 | 모든 API 수신 좌표를 실제 관제 로그와 송신 Payload에 대조 | 358개 상태 표본 대조 통과. 송신109·관제 원시 Telemetry110 중 추가1개는 반 칸 좌표 시험 입력으로 표시 단계에서 거부됨 | [대조 집계](../20261022-grid-mqtt-live-01/sender-receiver-api-equality.json), [표본](../20261022-grid-mqtt-live-01/samples.jsonl), [실제 원본](../map-live-20261022-092151-0ef0e4bfdbe94d709c5ab0c5540bc40b/grid-navigation.jsonl) |
| 직접 연결 회귀 | 기존 Verify-GridNavigation의10개 시나리오 | 10개 통과 | [집계](../20261022-grid-local-regression-01/verification.json) |
| 격리 환경 정리 | 소유 서버 종료0·포트 닫힘·소유 Broker 제거·기존 컨테이너 유지 | 모두 확인, 통과 | [cleanup.json](../20261022-grid-mqtt-host-01/cleanup.json) |
| 사용자 시연 서버 | 기존1884 Broker 경유 실제 수신 | 목적지 (4,5), Telemetry29·Heartbeat6, Completed·최종 정지 수신 확인 | [인계 시점 스냅샷](handoff-state.json) |

실제 MQTT 시나리오 집계는 [11개 전체 결과](../20261022-grid-mqtt-live-01/verification.json)다. 기존 Simulator151건·통신768건은 이번에 재실행하지 않았다. `src/` 변경이 없어 앞선 근거를 재사용하며 이번 55건과 합산하지 않는다.

## 4. 실패·재시험 구분

- 최초 두 빌드는 실행 중인 지도 프로세스의 apphost/DLL 파일 잠금 때문에 실패했다. 현재 지도를 저장하고 기존 서버를 종료한 뒤 빌드 성공·새 서버 기동·장애물 복원을 확인했다. 초기 실패를 제품 컴파일 오류로 분류하지 않는다.
- 첫 자동시험은 시험 코드가 실제 HTTP와 다른 JSON 이름 정책을 사용해 10건이 실패했다. [첫 TRX](grid-mqtt-tests.trx)·[로그](test.log)를 보존했다. 시험 직렬화를 실제 HTTP의 Web 설정과 맞춘 뒤 두 번째55건이 통과했다. 합격 기준을 완화하거나 실패 항목을 제외하지 않았다.
- 새 브라우저 화면의 자동 시각 검증은 정책 차단으로 미실행이다. 사용자가 화면이 보인다고 응답했고 사용자 서버에서도 실제 최종 수신을 확인했다. 이를 자동 브라우저 시험 통과로 표시하지 않는다.

## 5. 설계 판단·대안·예상 PR 질문

지도 편집·재탐색·이동·수신 반영·스냅샷은 같은 잠금에서 짧게 처리하고 네트워크 I/O는 잠금 밖에서 기다린다. 한 MQTT 실행 루프가 이동과 발행 순서를 소유하므로 각 이동 칸을 발행하고 발행 지연이 생기면 다음 이동도 늦어진다. 대규모·실시간 제어를 보장하는 방식은 아니다. 단일 프로세스의 잠금이 실제 장비 사이의 충돌·메시지 순서·유실을 해결하지 않는다.

취소 요청과 실제 연결 정리를 구분해 Canceling/Resetting 상태에서 새 실행을 거부한다. Reset은 이전 수신 큐·연결 정리가 끝난 뒤 수신 모델을 비운다. 실행마다 새 연결을 만들고 이벤트 해제·송신 Client 해제·수신 큐 종료를 기다린다. 서버별 고유 AMR ID로 다른 프로세스의 송신을 격리한다. QoS0 유실·중복·재시도 보장은 추가하지 않았다.

현실적인 대안은 이동 루프와 별도 송신 작업 사이에 최신 스냅샷을 공유하는 기존 연속 Simulator 방식이다. 네트워크 지연과 이동을 분리할 수 있지만 빠른 중간 칸은 주기 송신 사이에 생략될 수 있고 두 Task의 취소·종료 책임이 늘어난다. 큐로 모든 칸을 보존하면 메모리 상한·역압·유실 정책을 정해야 한다. 이번 작은 지도는 순서 있는 단일 루프로 처리했다. 탐색 한 번의 비용은 O(V log V)·O(V), 이동·수신 흔적의 보관 공간은 이동 횟수에 비례한다. 장시간 운용 시 흔적 보관 상한이 필요하다.

1. **계산 좌표를 표시한 것 아닌가?** `position`·`receivedTrail`은 관제 모델만 사용한다. 송신만 하고 수신을 막은 자동시험에서 표시 좌표는 null이며 최종 수신이 없으면 계산상 도착해도 Failed다. 실제 358개 표본도 관제 원시 로그·발행 Payload와 대조했다.
2. **행·열과 MQTT X/Y를 어떻게 맞췄나?** 시연용1m 셀, X=열·Y=-행이다. 수신 좌표가 정수 칸·맵 범위에 맞지 않으면 지도 투영에서 거부한다. 실제 건물 지도·로봇 크기·좌표 원점 계약은 별도이며 임의의 실수 좌표를 반올림해 칸으로 바꾸지 않는다.
3. **취소·단절·도착을 어떻게 구분하나?** 취소는 실제 자원 정리 뒤 Canceled, 단절은 이동 정지 후 Failed·마지막 수신, 도착은 최종 시각·목적지·IDLE·속도0 수신 뒤 Completed다. 3초 최종 수신 대기는 도구용 제한이며 QoS0 전달 보장이 아니다. 자동 재접속·재전송은 없다.

## 6. 남은 범위

명령/ACK·실제 로봇 STOP 확인·Core/DB·전체 관제 GUI·다중 로봇 충돌·센서·제동·건물 지도·대규모/장시간 성능·실제망 장애 복구는 이번 범위가 아니다. 현재 구현은 단일 가상 AMR의 격자 이동과 실제 MQTT 관제 수신 지도까지다. 최신 develop으로 정식 반영할 때 해당 기능 Issue·브랜치·PR로 분리하고 문서 원본의 공식 범위를 갱신한다. 이번 결과만으로 과제2 전체·정식 반영 완료를 표시하지 않는다.
