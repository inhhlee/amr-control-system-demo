# COM01-C 종료 경합 보완·PR 준비 검증

## 1. 목표와 적용 기준

- 실행자: Codex, 2026-10-19. Issue #23 / COM01-C.
- 기능명세 v0.7 COM-01, 요구사항정의 v0.5 R-04, 공통규칙·공통데이터명세 v0.4, 결정사항 v0.5, DN-09와 과제계획 v0.31을 적용했다. 문서 원본은 C:\process-plan이며 공유 URL이 없어 문서명·버전으로 식별한다.
- 기본 큐 구현은 `1060c6450f19d42e3fea0cb5d858500a8ab6aa93`에 포함되어 있다. 최신 origin/develop 확인·fast-forward 대조 후 `git flow feature start 23-amr-receive-queue`로 작업 브랜치를 생성했다. 기존 #23 브랜치·PR은 조회되지 않았다.
- 이번 범위: 메시지를 꺼낸 뒤 호출 직전에 종료되는 경합, 기존 순서·독립성·오류/종료 회귀, 수신부의 작은 제한, 실제 MQTT 정상/끊김. 실제 Core·AMR 이동·GUI·DB·Command 발행·자동 복구는 제외한다.

## 2. 코드·실행 환경

| 항목 | 확인 결과 |
| --- | --- |
| 위치·브랜치 | C:\amr-control-system-demo / feature/23-amr-receive-queue |
| 코드 | 1060c64 + AmrReceiveQueue 수정·BoundaryTests 4건 등 미커밋 변경 |
| 시작 | 2026-10-19 22:24:45 KST; 개별 실행 종료 시각은 TRX·verification.json 참조 |
| OS·SDK | Windows 10.0.26200 / .NET SDK 10.0.401 |
| 라이브러리·시험 | MQTTnet 5.2.0.1603 / 기존 xUnit 패키지 유지 |
| 실제 Broker | Docker Client/Server 24.0.6 / Mosquitto 2.1.2 / MQTT 5·QoS 0·Retain=false / 127.0.0.1:1884 |
| Git Flow | 현재 PC의 AVH 1.12.3 사용. CONTRIBUTING 예시의 next 2.0.0과 다르며 공유 설정 파일을 새로 만들지 않고 로컬 main/develop·feature/ 설정만 초기화 |

[환경](environment.json), [시험 소스 해시](source-version.json), [MQTT 실행 당시 소스와 현재 비교](source-comparison.json)를 연결한다. 자동시험 뒤 제품/자동시험 소스는 변경하지 않았다. MQTT 실행 전 47개, 끊김 실행 전 48개 파일은 각각 현재와 불일치 0건이다. 수집 파일 수 차이는 새 끊김 재현 도구 추가 때문이며 기존 실행 기록은 수정하지 않았다.

## 3. 변경과 입력·예상 결과

`AmrReceiveQueue`의 접수 잠금과 호출 진입 잠금을 분리했다. 서로 다른 AMR은 읽기 잠금으로 함께 호출할 수 있고 Task 대기는 잠금 밖이다. 종료 시 호출 진입 전에 멈춘 메시지는 폐기 수량에 포함한다. 취소 신호는 진입 중인 동기 호출이 Task를 반환한 뒤 전달한다. 동기 구간은 짧아야 하며 토큰 취소를 동기로 기다리지 않는다. 반환하지 않는 동기 구간도 StopAsync 제한 시간에 오류를 반환하고 늦은 반환 뒤 정리한다.

| 입력·절차 | 예상 결과 |
| --- | --- |
| 호출 직전 잠금을 시험에서 잡아 메시지 하나를 꺼낸 상태로 고정한 뒤 종료 | 처리자 0회, 폐기 1건, 새 접수 차단 |
| 동기 구간을 대기시킨 뒤 50ms 제한으로 종료, 나중에 해제 | Timeout 보고 후 늦은 취소·정리; 새 호출 없음 |
| 네 AMR 접수와 종료를 5,000회 경합 | 이미 취소된 토큰으로 처리자 동기 본문에 진입한 횟수 0 |
| 실제 수신부/Validator + Fake MQTT, 대기 2·AMR 2 제한 | 처리 중 1 + 대기 2 유지, 새 입력 QueueFull, 새 AMR만 AmrLimitReached, 검사 오류와 분리 |
| 전체 복원·빌드·자동시험 | 기존 543 + 신규 4 = 547 통과 |
| 실제 MQTT 세 세션 | 정상 10·거부 1·처리 취소 1·폐기 3, 종료 0 |
| 실제 Broker 중단 | 정상 1·취소 1·폐기 3, 중단 뒤 호출/전달 없음, 정리 후 종료 1 |

```powershell
dotnet restore AmrControlSystem.slnx
dotnet build AmrControlSystem.slnx --no-restore --verbosity minimal
dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-build --no-restore --logger "trx;LogFileName=tests.trx" --results-directory evidence/COM-01/<새-실행ID>
pwsh -NoProfile -File tests/manual/COM01-C/Verify-Mqtt.ps1 -RunId <새-실행ID>
pwsh -NoProfile -File tests/manual/COM01-C/Verify-Disconnect.ps1 -RunId <새-실행ID> -AllowBrokerStop
```

## 4. 실제 결과와 판정

| 구분 | 실제 결과 | 판정·근거 |
| --- | --- | --- |
| 복원·빌드 | 종료 0, 빌드 경고 0·오류 0 | 통과: [복원](restore.log), [빌드](build.log) |
| 전체 자동시험 | 547 통과, 실패 0·건너뜀 0 | 통과: [로그](test.log), [TRX](tests.trx) |
| 새 경계 시험 4건 | 위 표의 입력·기대값 충족 | 통과: [시험 코드](../../../tests/Amr.Communication.Tests/AmrReceiveQueueBoundaryTests.cs), TRX |
| 정상 MQTT | 15건 분류 및 세 세션 종료 0 | 통과: [결과](../20261019-queue-pr-mqtt-01/README.md) |
| Broker 중단 | 5건 분류, ConnectionLost·SubscriptionStopped·Disposed·종료 1 | 통과: [결과](../20261019-queue-pr-disconnect-03/README.md) |
| 실행 증거 보존 | 초기 복원 시도·끊김 도구 초기 오류를 별도 실행 ID에 보존 | [복원 첫 시도](../20261019-queue-pr-unit-01/README.md), [끊김 01](../20261019-queue-pr-disconnect-01/README.md), [끊김 02](../20261019-queue-pr-disconnect-02/README.md) |

## 5. 환경 복구·미검증

- 시작 당시 amr-demo Broker는 없었다. 기존 이미지로 시험용 amr-demo/mosquitto를 생성·기동했다. 정상 재현 도구 environment.json의 `brokerStartedOrRestartedByThisTest=false`는 해당 도구 자체의 동작이며, 이 작업 전체에서 기동하지 않았다는 뜻이 아니다.
- 끊김 도구는 이 전용 Broker만 중단하고 finally에서 재시작했다. 전체 시험 뒤 새 컨테이너·네트워크를 제거해 시작 전 상태로 복구했다. 바인드 데이터와 기존 amr-mosquitto·amr-seq는 보존했다. [정리 로그](broker-cleanup.log).
- 실제 MQTT 과부하·장시간 부하·네트워크 무응답/KeepAlive 상세 시험은 미실행이다. 작은 제한은 Fake MQTT와 실제 수신부를 연결한 시험이며 실제 Broker 부하 시험으로 표시하지 않는다.
- 진행 중인 동기 처리자가 Task를 반환하지 않으면 취소 전달도 늦어진다. 종료 호출자는 제한 시간에 오류를 받고, 프로세스가 살아 있는 동안 늦은 반환·정리를 관찰한다. 임의의 동기 코드를 강제 중단하지 않는다.
- 기존 543건과 새 4건 외의 범위를 추가 통과로 집계하지 않는다. 문서 수정·링크 검사는 제품 시험과 별개다.

## 6. 완료조건과 남은 단계

| Issue #23 조건 | 현재 근거·상태 |
| --- | --- |
| AMR별 네 유형 순서·독립 처리·원형 보존 | 기존 회귀 + 새 정상 MQTT 통과 |
| 큐/AMR 제한·즉시 거부·검사 오류 구분 | 새 수신 경로 경계 시험 + 기존 용량 시험 통과 |
| 처리자 실패 뒤 계속 처리·취소 후 전달 차단·폐기 | 기존 실패 시험 + 호출 직전 종료·5천 회 경합 + 실제 MQTT 취소/끊김 통과 |
| 종료 제한·늦은 결과·정상/끊김/새 세션 정리 | 기존 늦은 성공/실패 + 새 동기 구간 제한 + 실제 정상/중단 시험 통과 |
| 회귀·MQTT 근거를 README와 Issue·PR에 연결 | 로컬 README·[PR 초안](PR.md) 준비. Issue·PR 외부 게시는 아직 미수행 |

이번 요청은 브랜치 생성·보완·시험까지다. 커밋·푸시·PR 생성·리뷰 승인·병합·Issue 종료는 수행하지 않았다. 기능 전체·과제1 전체 완료를 선언하지 않는다.
