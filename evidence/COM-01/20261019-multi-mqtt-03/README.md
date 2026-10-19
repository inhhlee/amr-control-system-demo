# COM01-B 구현·실행 검증결과 — 2026-10-19

관련 Issue: [#21](https://github.com/inhhlee/amr-control-system-demo/issues/21). feature/21-multi-amr-subscription, 기준 커밋 e40e2b4a72f5901a32e723a04661482f51f23f9a + 미커밋 변경. 커밋·푸시·PR·GitHub 결과 게시·병합은 하지 않았다.

## 목표와 적용 기준

네 MQTT Topic을 기존 검사기에 연결해 정상 모델만 대상 AMR·유형·콜백 진입 UTC와 함께 동기 Core 대역에 전달한다. 기능명세 v0.7 COM-01, 요구사항정의 v0.5 R-04, 공통규칙 v0.4 C-01·C-03·C-05·C-06·C-10, 공통데이터명세 v0.4 3절·수신 계약 v3, 결정사항 v0.5 D-03·D-04 및 #21을 적용했다. 착수 때 읽은 과제계획 v0.27·전체일정 v0.20은 Issue 준비 당시 v0.26·v0.19보다 최신이며 범위·완료조건은 같다.

## 완료조건과 실제 결과

[브라우저 보고서](report.html)는 같은 근거의 정적 보기본이다. 브라우저에서 Ctrl+O로 report.html을 열면 된다. MQTT 포트 1884는 웹 페이지가 아니다.

내장 브라우저 도구는 file:// 프로토콜을 보안 정책으로 차단했다. 브라우저 화면 렌더링은 미확인이며 HTML 내용·근거 링크는 파일 기준으로 검사했다.

| 완료조건 | 실제 확인·판정 | 근거 |
| --- | --- | --- |
| 1. 두 AMR × 네 유형 전달 | 통과. 세션 0 정상 12건(기본 8 + 오류 뒤 정상 4), 세션 1 정상 8건. 대상·유형·모든 모델 필드·수신 시각 일치, 제어된 실행에서 추가 전달 없음 | [입력/예상](inputs.json), [실제 발행](publications.json), [콘솔 0](console-0.stdout.log), [콘솔 1](console-1.stdout.log) |
| 2. ACK 원형 보존 | 통과. 실제 MQTT에 AMR_001의 id/reason 없는 FAILURE와 AMR_002의 id/reason 포함 SUCCESS 전달. 전체 선택 조합은 Fake 16건 | [종합 판정](verification.json), [자동시험](../20261019-multi-01/tests.trx) |
| 3. 거부·오류 경계 | 통과. 실제 MQTT 불량 4건 거부 후 각각 정상 전달. command/unknown/ACK/추가 경로 4건은 Broker 필터 밖이라 앱 진단도 없음. 수신·진단·거부 콜백 예외 및 다음 정상 처리는 Fake에서 확인 | [콘솔 0](console-0.stdout.log), [시험 코드](../../../tests/Amr.Communication.Tests/MessageSubscriptionTests.cs) |
| 4. 승인·정리 | 통과. 실제 네 필터 승인 뒤 발행, 두 실행 모두 CTRL_C_EVENT → 구독 정리 → 연결 해제 → Dispose → 종료 0, 서로 다른 Client ID와 Broker disconnect 확인. 일부 승인 거부·취소·해제 실패는 Fake에서 확인 | [종합 판정](verification.json), [Broker](broker.log), [자동시험](../20261019-multi-01/tests.trx) |
| 5. 기존 회귀 | 통과. 기존 463 + 새 수신부 66 = 529건, 실패·건너뜀 0. 빌드 경고·오류 0 | [빌드](../20261019-multi-01/build.log), [자동시험](../20261019-multi-01/test.log) |
| 6. 결과 연결 | 로컬 README·증거 작성 완료. GitHub Issue 결과 게시·PR 연결은 아직 미수행이므로 전체 체크하지 않음 | [저장소 안내](../../../README.md#com01b-run) |

시험 수신자 출력의 줄 맨 앞 시각은 로그 작성 시각이다. JSON의 ReceivedAtUtc와 Model.ReceivedAtUtc가 MessageDiagnostic의 수신 시각과 같은지 비교했다. ts는 별도로 원래 값을 보존했다. 수신 이벤트당 1회 호출은 네트워크 무손실·중복 제거 보장이 아니다.

## 코드·환경·재현

- 자동시험: [환경](../20261019-multi-01/environment.log), [소스 해시](../20261019-multi-01/source-version.json), [빌드 실행](../20261019-multi-01/build-result.json), [시험 실행](../20261019-multi-01/test-result.json).
- 실제 MQTT: [환경](environment.json), [소스 해시](source-version.json), [Broker 상태](broker-state.log), [마운트](broker-mounts.json), [컨트롤러 종료](controller-result.json).
- 기존 amr-demo Broker를 127.0.0.1:1884로 재사용했다. MQTT 5, QoS 0, Retain=false, 구독 시 보관 메시지 재전송 안 함. Broker 시작·재시작·설정 변경 없음.
- 검증 도구: [Verify-Mqtt.ps1](../../../tests/manual/COM01-B/Verify-Mqtt.ps1), [시험 입력](../../../tests/manual/COM01-B/inputs.json). Windows PowerShell 7, Docker Desktop 및 기존 Broker 실행 필요.

저장소에서 빌드·시험 후 아래를 실행한다. 실행 ID는 매번 새로 지정한다. 도구가 자신의 수신 콘솔 두 개를 순차로 열고 발행·검사·Ctrl+C 신호·로그 수집까지 수행한다.

```powershell
dotnet build AmrControlSystem.slnx --no-restore
dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-build --no-restore
pwsh -NoProfile -File tests/manual/COM01-B/Verify-Mqtt.ps1 -RunId <새-실행ID>
```

통과 시 새 폴더의 verification.json result=passed를 확인한다. 이 보고서는 이번 실행의 고정 보기본이며 재실행 시 자동 갱신되지 않는다.

## 실패 기록·미검증 범위

[직전 실행 02](../20261019-multi-mqtt-02/README.md)는 첫 세션 정상 종료 뒤 두 번째 세션의 PowerShell native command 콘솔 핸들 오류로 중단했다. 도구의 Docker 실행을 ProcessStartInfo의 명시적 표준 입출력으로 보완해 03에서 전체 재실행했다. 제품 C# 소스는 이 보완으로 바뀌지 않았다.

자동시험과 실제 MQTT는 별도 근거다. 부분 구독 거부·취소·해제 실패·수신자 예외는 실제 Broker 장애를 유발하지 않고 Fake로 확인했다. 사용자의 직접 키보드 입력 대신 격리된 시험 프로세스에 Windows CTRL_C_EVENT를 보냈다. AMR별 큐·느린 수신자 격리·과부하·Core 업무·GUI·DB·재접속·Timeout·운영 QoS 보장·실장비 호환성은 구현·검증하지 않았다. COM-01 전체·과제1 전체 완료로 판정하지 않는다.

