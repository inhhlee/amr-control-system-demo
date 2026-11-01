# 시험 폴더 안내

확인하려는 기능에 맞는 시험 코드를 찾는 안내다. **자동시험 코드**, **실제 서비스와 연결하는 시험 도구**, **실행 결과**의 위치를 구분한다.

| 찾는 것 | 위치 |
| --- | --- |
| 준비·빌드 방법 | [BUILDING.md](../BUILDING.md) |
| 기능의 구현 코드 | [src 안내](../src/README.md) |
| 이미 실행한 결과·완료 근거 | [evidence 색인](../evidence/README.md) |

<a id="automated-tests"></a>

## 프로젝트별 자동시험

| 폴더 | 확인하는 기능 | 대표 시험 |
| --- | --- | --- |
| [Amr.Communication.Tests](Amr.Communication.Tests/) | 접속·구독·메시지 검사·장비별 큐·생존 감시·재접속·명령 발행, 콘솔·로그 분리 | [재접속](Amr.Communication.Tests/Mqtt/Sessions/ReconnectingCommunicationSessionTests.cs) · [Seq/콘솔 분리](Amr.Communication.Tests/Console/Logging/TrialEventLoggerTests.cs) |
| [Amr.Simulator.Tests](Amr.Simulator.Tests/) | 설정 읽기·단일/여러 AMR 생성·이동/도착·격자 탐색·MQTT 송신·MOVE/STOP 결과 | [명령 수행](Amr.Simulator.Tests/Simulation/CommandDrivenAmrTests.cs) · [설정](Amr.Simulator.Tests/Configuration/ConfigurationTests.cs) |
| [Amr.Core.Tests](Amr.Core.Tests/) | 통신 이상·관제 미션 판단, 명령/응답 ID·중복·대기 시간 처리 | [관제 판단](Amr.Core.Tests/Domain/AmrDomainManagerTests.cs) · [명령 추적](Amr.Core.Tests/Commands/CommandTrackerTests.cs) |
| [Amr.ControlGui.Tests](Amr.ControlGui.Tests/) | Mock·지도·대시보드·선택/별도 창, 실제 MQTT 공급자의 상태 변환과 명령 처리 | [공급자](Amr.ControlGui.Tests/Providers/Mqtt/MqttDataProviderTests.cs) · [화면](Amr.ControlGui.Tests/Dashboard/MqttDashboardTests.cs) · [명령](Amr.ControlGui.Tests/Simulation/MqttControlTests.cs) |
| [Amr.Simulator.Map.Tests](Amr.Simulator.Map.Tests/) | 격자 웹 호스트의 설정·좌표 변환·수신값 투영·여러 AMR 설정 | [좌표 투영](Amr.Simulator.Map.Tests/Projection/ProjectionTests.cs) · [지도 설정](Amr.Simulator.Map.Tests/Configuration/MapConfigurationTests.cs) |

통신 시험의 내부 폴더는 소스의 `Command`·`Monitoring`·`Mqtt`·`Validation`에 대응한다. `Console`은 콘솔 동작, `TestDoubles`는 Broker 대신 사용하는 시험 대역이다. Infrastructure의 공급자 시험은 현재 `Amr.ControlGui.Tests`에 있다.

그 밖의 C# 시험 파일도 역할별로 배치한다.

| 프로젝트 | 내부 기능 폴더 |
| --- | --- |
| Amr.Simulator.Tests | `Configuration` 설정 · `Execution` 실행 조립 · `Simulation` 이동/명령 · `Navigation` 격자 탐색 · `Mqtt` 송신 |
| Amr.Core.Tests | `Commands` 명령 추적 · `Domain` 관제 판단 |
| Amr.ControlGui.Tests | `Providers/Mock` 예시 공급자 · `Providers/Mqtt` 실제 공급자 · `Dashboard` 화면 · `Simulation` 실행/제어 |
| Amr.Simulator.Map.Tests | `Configuration` 지도 설정 · `Mqtt` 수신 · `Projection` 좌표 변환 |

`GuiTestAssembly.cs`는 전체 GUI 시험의 동시 실행 설정이므로 프로젝트 루트에 둔다. [파일 이동 전후 목록](../evidence/layout/20261101-role-folders-01/moves.json)으로 이전 경로를 찾을 수 있다.

Broker가 없는 자동시험의 예는 아래와 같다. 저장소 루트에서 실행한다.

```bash
dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj
```

통신·로그 시험은 시험 대역/로컬 시험용 서버도 사용한다. 이 결과만으로 실제 Docker Broker·Seq 수집이나 GUI 시연이 통과한 것으로 판단하지 않는다. GUI 시험은 Windows 환경이 필요하다.

<a id="integration-tests"></a>

## manual — 실제 연결·실행 시나리오 도구

`manual`에는 실행 안내와 PowerShell 자동 확인 도구가 함께 있다. 폴더 이름은 과제 기능 ID를 따른다. 실행 전 각 안내/스크립트의 입력·환경·대상 자원을 확인한다.

| 폴더 | 확인하는 상황 | 시작 위치 |
| --- | --- | --- |
| [COM01-B](manual/COM01-B/) | 여러 AMR의 메시지 구독·검사·구독 밖 메시지 처리 | [Verify-Mqtt.ps1](manual/COM01-B/Verify-Mqtt.ps1) |
| [COM01-C](manual/COM01-C/) | 장비별 수신 큐와 단절 중 처리·종료 | [Verify-Mqtt.ps1](manual/COM01-C/Verify-Mqtt.ps1) · [Verify-Disconnect.ps1](manual/COM01-C/Verify-Disconnect.ps1) |
| [COM02-A](manual/COM02-A/) | 생존 신호·공통 통신 세션·두 AMR의 상태 구분 | [Verify-Heartbeat.ps1](manual/COM02-A/Verify-Heartbeat.ps1) · [Verify-ControlSession.ps1](manual/COM02-A/Verify-ControlSession.ps1) · [Verify-TwoAmr.ps1](manual/COM02-A/Verify-TwoAmr.ps1) |
| [COM03-B](manual/COM03-B/) | 최초 접속 실패·접속 대기 제한·취소 | [Verify-Connection.ps1](manual/COM03-B/Verify-Connection.ps1) |
| [COM03-C](manual/COM03-C/) | 접속 유지·Broker 종료·응답 차단·복구 | [도구·실행 안내](../tools/COM-03/README.md) · [Verify-KeepAlive.ps1](manual/COM03-C/Verify-KeepAlive.ps1) |
| [COM05-A](manual/COM05-A/) | 이동·정지·복구 명령의 실제 발행 | [Verify-Commands.ps1](manual/COM05-A/Verify-Commands.ps1) |
| [COM06](manual/COM06/) | trial 송수신·재접속·명령/응답과 대화형 입력 유지 | [실행 안내](manual/COM06/README.md) · [Verify-TrialConsole.ps1](manual/COM06/Verify-TrialConsole.ps1) · [Run-TrialInputProbe.ps1](manual/COM06/Run-TrialInputProbe.ps1) |
| [AMR01](manual/AMR01/) | 설정·수량·이동 실행·MQTT 송신·격자 지도 | [시뮬레이터 시험 안내](manual/AMR01/README.md) · [MQTT 시연](manual/AMR01/MQTT-DEMO.md) |
| [GUI-MQTT](manual/GUI-MQTT/) | 실제 MQTT 위치·상태·생존 신호·단절/재접속의 GUI 표시 | [실행·시험 안내](manual/GUI-MQTT/README.md) |
| [GUI-CONTROL](manual/GUI-CONTROL/) | 독립 AMR과 GUI의 MOVE/STOP·도착 응답·창 닫기·재접속 | [실행·시험 안내](manual/GUI-CONTROL/README.md) |
| [LOG](manual/LOG/) | 실제 Seq 수집·구조화 검색·종료/전송 실패·trial 출력 분리 | [Seq 준비·조회](manual/COM06/README.md#docker-seq에서-구조화-상세-로그-조회) · [Verify-TrialSeq.ps1](manual/LOG/Verify-TrialSeq.ps1) |

`COM02-A` 아래 `HeartbeatProbe`·`FleetProbe`, `GUI-MQTT`·`GUI-CONTROL`의 프로젝트는 연동 결과를 확인하는 시험용 프로그램이다. `LOG`는 Seq 연동 시험 도구 위치이며, 현재 PC의 기존 Docker Seq 관리 파일은 `C:\practice\docker\docker-compose.yml`이다. 이 저장소의 `docker/compose.yaml`은 Mosquitto를 관리한다.

실행별 입력·환경·코드 버전·예상/실제 결과·판정은 `evidence`에 남긴다. 기존 실행 결과는 [색인](../evidence/README.md)으로 찾고, 시험 파일의 존재를 실행 성공이나 과제 전체 완료로 판단하지 않는다.
