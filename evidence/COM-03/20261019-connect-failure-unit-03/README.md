# COM03-B #5 접속 실패·대기 제한·취소 검증

## 1. 목표와 적용 기준

- 실행자: Codex, 2026-10-19 KST. [Issue #5](https://github.com/inhhlee/amr-control-system-demo/issues/5)의 구현·시험 요청.
- 기준: 기능명세 v0.7 COM-03, 요구사항정의 v0.5 R-04, 공통규칙 v0.4 C-04·C-10, 결정사항 v0.5, 과제계획 v0.32, COM03-B 로컬 학습 안내 v0.6. 원본은 `C:\process-plan`이다. Issue의 계획 v0.11·연결 코드 미생성 문장은 이전 상태이며 현재 파일과 v0.32를 적용했다. 완료조건의 범위는 유지했다.
- 입력 검사 → 전송 실패/MQTT 거부 → 대기 제한 → 사용자 취소/늦은 완료 → 정상 새 프로세스를 확인한다. 연결 후 KeepAlive·단절 경합은 #6, 자동 재접속은 COM-06으로 남긴다.

## 2. 실행 환경과 코드

| 항목 | 실제 값 |
| --- | --- |
| 위치·브랜치 | C:\amr-control-system-demo / feature/5-mqtt-connect-failure |
| 기준 코드 | develop e2076145615d1310e1a52405cd5c35d66ca71a5e + 이번 미커밋 변경 |
| 브랜치 생성 | origin fetch·로컬 develop 동일 확인 후 ff-only 대조, `git flow feature start 5-mqtt-connect-failure`; 기존 #5 브랜치·PR 없음 확인 |
| OS·SDK | Microsoft Windows 10.0.26200 / .NET SDK 10.0.401 |
| 패키지 | MQTTnet 5.2.0.1603, xUnit 2.9.3, Test SDK 17.14.1 |
| 실제 연동 | Docker 24.0.6 / Mosquitto 2.1.2 / 프로젝트 Broker 127.0.0.1:1884 |
| 대역 | IMqttClient Fake, 실제 루프백 TCP 서버(CONNECT 수신 후 CONNACK 미전송) |
| 코드 식별 | [53개 소스·프로젝트·도구 해시](source-version.json), [실제 MQTT 실행본과 불일치 0](source-comparison.json) |

[자동시험 시각·환경·집계](environment.json), [실제 연동 환경](../20261019-connect-failure-mqtt-02/environment.json)을 참조한다. 실행 시각은 UTC 원본으로 보존하며 KST는 +09:00이다. 별도 spec_revision 추적기는 없다.

## 3. 변경·입력·절차

- MqttConnection: 실패 유형/승인 거부 코드 제공, 사용자와 제한의 첫 취소 원인 유지, 대기 상한, 승인/취소의 원자적 결과 결정, 실패 시 Dispose·재사용 금지, 늦은 결과/예외 관찰.
- ConsoleRunner: 기존 실제 콘솔 흐름에 Client·취소·출력 주입 경계 추가. Program은 실제 Ctrl+C를 같은 토큰에 전달한다. 최초 연결 실패 이벤트와 연결 후 상실을 구분하고 안전한 원인 코드·종료 코드를 기록한다.
- 시험 전 기준: 1초·2초 제한은 ConnectAsync 진입부터 완료까지 -50ms/+2000ms, 자동 사용자 취소 후 2초 이내, 실제 CTRL_C_EVENT 후 3초 이내 종료. 실제 제한은 Connecting 로그부터 ConnectFailed 로그까지 측정한다. 단순 TCP 거절을 제한 시험으로 집계하지 않는다.

```powershell
dotnet restore AmrControlSystem.slnx
dotnet build AmrControlSystem.slnx --no-restore --verbosity minimal
dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-build --no-restore --logger "trx;LogFileName=tests.trx" --results-directory evidence/COM-03/<새 실행ID>
# 프로젝트 Broker를 기동한 환경에서
pwsh -NoProfile -File tests/manual/COM03-B/Verify-Connection.ps1 -RunId <새 실행ID>
```

## 4. 실제 결과와 판정

| 입력·기대 | 실제 결과·판정 | 근거 |
| --- | --- | --- |
| 빈/공백 host, port 0/65536, 제한 0/-1 및 잘못된 숫자 | 8건: Client 생성·Connect 호출 0, InputRejected·종료 1 | ConsoleConnectionFailureTests, [TRX](tests.trx) |
| 전송 실패와 MQTT NotAuthorized | 각 사유·코드 구분, 초기 Disconnected 알림에도 취소/ConnectionLost로 오분류 없음, 종료 1·Dispose 1회 | ConsoleConnectionFailureTests, TRX |
| 취소를 무시하는 Fake에 1초·2초 제한 | 1010.0ms / 2023.5ms, 사전 허용 범위 충족·Timeout·Dispose, 늦은 승인에도 연결 안 됨 | MqttConnectionFailureTests, TRX stdout |
| 사용자 취소·완료 직전 취소·사전 취소·늦은 성공/실패 | 사용자 토큰 보존, 대기 종료·재사용 차단·Connected 없음, 콘솔 종료 0 | 신규 자동시험 |
| 요청하지 않은 OperationCanceledException | 사용자 취소로 취급하지 않고 TransportFailure | 신규 자동시험 |
| 전체 빌드·회귀 | 경고 0·오류 0, 기존 547 + 신규 19 = 566 통과, 실패/건너뜀 0 | [빌드](build.log), [시험](test.log), TRX |
| 실제 TCP 거절·MQTT 승인 대기·Ctrl+C·새 프로세스 | 6개 시나리오 통과. 제한 1043.4677ms / 2040.8842ms, 실패 종료 1, 취소/정상 종료 0, 소켓 EOF | [실제 결과](../20261019-connect-failure-mqtt-02/README.md) |

시험 대역은 정상 MQTT 거부 코드 및 취소를 무시한 늦은 통지를 제어한다. 실습 패키지의 접속 결과·정리 동작은 [MQTTnet 공식 소스](https://github.com/dotnet/MQTTnet/blob/v5.2.0/Source/MQTTnet/MqttClient.cs)와 대조했으며 실제 네트워크 정리는 TCP EOF로 별도 확인했다.

## 5. 이전 실패·환경 복구·제약

- [unit-01](../20261019-connect-failure-unit-01/README.md): 제한된 실행 환경의 restore 종료 1·빈 로그. 승인된 환경으로 재실행했다.
- [unit-02](../20261019-connect-failure-unit-02/README.md): 중간 코드 566건 통과. 이후 최종 승인/취소 원자화·소켓 원인 표시 변경으로 unit-03에서 회귀했다.
- [mqtt-01](../20261019-connect-failure-mqtt-01/README.md): TCP 거절·제한 두 건은 통과했지만 정상 세션의 빈 출력 로그 처리에서 도구 오류. 수정 후 mqtt-02에 전체 결과를 새로 남겼다.
- 시작 당시 amr-demo는 없었고 기존 amr-mosquitto·amr-seq는 중지 상태였다. [이번 전용 Broker 생성](../20261019-connect-failure-unit-02/broker-start.log) 후 시험했고 [컨테이너·네트워크 정리](broker-cleanup.log)와 [기존 상태 보존](broker-after.log)을 확인했다. 데이터·로그 폴더는 삭제하지 않았다.
- 실제 MQTT 승인 거부, 연결 후 무응답/KeepAlive·단절 경합, 자동 복구는 이번 실연동 통과 범위가 아니다. MQTT 승인 거부는 Fake 시험이다. 주입 Client의 Dispose는 전송 자원을 해제해야 한다. 무기한 블로킹하는 동기 코드/Dispose를 강제 중단하는 기능은 제공하지 않는다.

## 6. 완료조건과 남은 단계

| #5 완료조건 | 현재 결과 |
| --- | --- |
| 서비스 없는 로컬 포트 실패·원인·Connected 없음 | 실제 거절·SocketError=ConnectionRefused·종료 1 확인 |
| MQTT 거부·전송 실패·대기 제한·사용자 취소 구분 | 자동시험 및 실제 콘솔 결과 확인 |
| 입력 오류는 연결 시도 전 거부 | 8개 입력에서 factory/Connect 호출 0 |
| 끝나지 않는 대역의 제한·취소·대기 상한 | 취소 무시 Fake 및 실제 TCP 승인 대기 시험 통과 |
| 정리와 늦은 완료 차단 | Dispose·재사용 금지·늦은 결과 시험, 실제 소켓 EOF |
| 실패/취소 뒤 새 정상 실행·회귀 | 서로 다른 Client ID의 두 새 MQTT 세션·566건 회귀 |
| Issue·PR에 증거 연결 | 로컬 README·증거 준비. 커밋·푸시·PR 요청 단계에서 게시 예정 |

이번 요청의 브랜치 생성·구현·시험을 마쳤다. 변경은 미커밋이며 PR 생성·리뷰·병합·Issue 종료는 수행하지 않았다. COM-03 전체·과제1 전체는 미완료다. 업무 기록은 C:\process-plan의 2026-10-19 일일업무에 연결한다.
