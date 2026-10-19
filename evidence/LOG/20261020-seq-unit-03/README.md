# LOG #26 Serilog·Seq 전송·장애·종료 검증

## 1. 목표와 적용 기준

- 관련 Issue: [#26](https://github.com/inhhlee/amr-control-system-demo/issues/26). 2026-10-20 사용자 요청으로 기존 Seq 재사용, 기존 콘솔 출력 유지, 로그 전송과 장애·종료를 구현·시험했다.
- 기준: 기능명세 v0.7 COM-01·03·04, C-10, 결정사항 v0.6 D-10, 개발환경 v0.8의 도구 선택. 이번 작업 범위는 과제계획 v0.36과 개발환경 v0.9에 반영한다.
- 브랜치 `feature/26-serilog-seq`, develop 기준 `b4265aff198ead9d234c592cfaf9b89f9e217024` + 미커밋 구현이다. 기존 AGENTS.md 변경은 보존하고 이번 기능과 구분한다. 커밋·푸시·PR·병합·Issue 종료 없음.
- [최종 소스 해시](source-version.json). 실제 MQTT 시험 당시 소스 스냅샷과 현재 C#·프로젝트 파일 해시 일치를 확인했다. 이후 시험 도구의 서버 측 AMR 필터 확인을 추가하고 동일 실행 데이터를 별도 조회해 검증했으며, MQTT 전체 시나리오를 다시 실행한 것으로 표시하지 않는다.

## 2. 실행 환경

| 항목 | 실제 값·근거 |
| --- | --- |
| 날짜 | 2026-10-20 KST. 세부 시각은 TRX·environment.json·verification.json의 UTC/offset 기록 |
| PC·SDK | Windows 10.0.26200, .NET SDK 10.0.401, net10.0 |
| 패키지 | Serilog 4.4.0, Serilog.Formatting.Compact 3.0.0, MQTTnet 5.2.0.1603 |
| Docker·Broker | Docker Client/Server 24.0.6, Mosquitto 2.1.2, 프로젝트 amr-demo의 127.0.0.1:1884 |
| Seq | 기존 amr-seq, 이미지 datalust/seq:2026.1, 실제 API 버전 2026.1.17114, http://127.0.0.1:5341 |
| Seq 관리·데이터 | 기존 C:/practice/docker/docker-compose.yml 및 C:/practice/docker/seq/data 재사용. 초기화·인증 변경·새 Seq 설치 없음 |
| 실제 서비스 변경 | 구현·시험 요청에 따라 기존 Seq를 시작하고 프로젝트 Broker를 기동했다. 각 검증 스크립트는 서비스 시작·중단을 하지 않는다. 최종 두 서비스 실행 중 |
| MQTT | Heartbeat·Telemetry·Alarm·ACK, AMR_001/AMR_002, QoS 0, Retain=false, 실행별 Client ID |

과거 개발환경 문서의 Docker 버전과 이번 조회값은 다르다. 이번 시험 환경값은 [실제 조회](../../COM-01/20261020-seq-mqtt-03-online/environment.json)를 따른다.

## 3. 입력·예상·실제·판정

| 시험 | 입력과 예상 | 실제 결과·판정 | 증거 |
| --- | --- | --- | --- |
| 전체 자동시험 | 기존 수신·연결·취소·큐와 로그 신규 14사례 회귀 | 580 통과, 실패/건너뜀 0. 최종 컴파일 경고 없음 | [실행 로그](test.log), [TRX](tests.trx) |
| 동시 구조화·flush | 동시 AMR 100개 + 마지막 ExitCode, 속성 혼합 없이 전송 | 101건과 RunId·AmrId·Sequence·마지막 이벤트 일치, handler 정리. 통과 | SeqLoggingTests/TRX |
| HTTP 장애·회복 | 401/500·전송 예외와 이후 정상 응답 | 실패 계수, 이후 새 이벤트 전송, 예외 정보 비노출. 통과. HTTP handler 대역 | 위 TRX |
| 과부하·종료 경합 | 용량 8, 첫 요청 정지 후 1000건, 동시 Stop | 992건 즉시 폐기, 종료 취소 후 Failed=1/Dropped=1000, 실제 handler 취소·Dispose. 통과 | 위 TRX |
| 크기·설정·개인정보 | 16KB 초과, 512자 초과 속성, 잘못된 URL, 비밀 포함 예외 | 큰 이벤트 폐기·잘림 표시·로그만 비활성화·비밀 미기록. 통과 | 위 TRX |
| 실제 Seq + MQTT | 두 세션: 정상 20, 거부 4, 구독 밖 4, Ctrl+C 두 번 | 기존 콘솔/모델/수신시각/종료 0 유지. Seq 114건 중 Received=20, Rejected=4, ExitCode=0 두 건. 통과 | [Seq 검증](../20261020-seq-mqtt-03/verification.json), [실제 이벤트](../20261020-seq-mqtt-03/seq-events.json), [MQTT 근거](../../COM-01/20261020-seq-mqtt-03-online/verification.json) |
| Seq 서버 측 속성 검색 | RunId·AmrId=AMR_001·EventType=Received 동시 필터 | 예상 12건, 실제 12건. 통과 | [검색 결과](../20261020-seq-mqtt-03/seq-amr-query.json) |
| 실제 Seq 접속 불가 + MQTT | 별도 bind-only 루프백 포트. 공유 Seq는 유지 | 정상 20·거부 4·구독 밖 4 유지, 두 번 Ctrl+C 종료 0. 로그 Failed 69/45·Dropped 0. 통과 | [MQTT 근거](../../COM-01/20261020-seq-mqtt-03-offline/verification.json), [오류 요약 1](../../COM-01/20261020-seq-mqtt-03-offline/console-0.stderr.log), [오류 요약 2](../../COM-01/20261020-seq-mqtt-03-offline/console-1.stderr.log) |
| 실제 HTTP 무응답 | 로컬 TCP 대역이 요청만 수신. --port 0 입력 실패의 종료 코드 1 유지, 프로세스 7초 이내·소켓 EOF 예상 | 약 2.858초 종료, 요청 385바이트 수신 뒤 클라이언트 EOF, Failed=2. 통과 | [검증](../20261020-seq-slow-01/verification.json), [stderr](../20261020-seq-slow-01/stderr.log) |

## 4. 실행 방법과 실패 이력

```powershell
dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-restore --logger 'trx;LogFileName=tests.trx' --results-directory evidence/LOG/<새-실행ID>
pwsh -NoProfile -File tests/manual/LOG/Verify-Seq.ps1 -RunId <새-실행ID>
pwsh -NoProfile -File tests/manual/LOG/Verify-SeqShutdown.ps1 -RunId <다른-새-실행ID>
```

- `seq-unit-01`: 579건 통과, xUnit2031 경고 1개. Assert.Single의 필터 overload로 수정.
- `seq-unit-02`: 579건 통과, 해당 경고 없음. 긴 식별자 잘림 표시 시험 추가 후 `seq-unit-03`에서 580건 통과.
- `seq-mqtt-01`: 실제 MQTT와 Seq 저장 성공, 시험 도구가 Invoke-RestMethod 배열을 한 항목으로 감싸 건수를 잘못 판정했다. [당시 실패](../20261020-seq-mqtt-01/failure.log)를 보존했다.
- `seq-mqtt-02`: 두 번째 세션 발행 중 전체 controller 55초 제한 초과. 제품 수신 실패가 아니라 전체 시험 도구 제한이었다. [당시 실패](../20261020-seq-mqtt-02/failure.log). 전체 한도만 180초로 조정했으며 개별 수신·발행·종료 8초는 유지했다.
- `seq-mqtt-03`: 위 두 도구 문제 수정 후 실제 온라인/접속불가 시나리오 통과. 공유 Seq 중단·네트워크 장비 장애 시험으로 확대하지 않는다.
- 샌드박스의 Docker·Windows 서비스·MSBuild 접근 제한은 승인된 실제 환경에서 재실행했다. 문서 보기본은 제목 양식 검사 실패를 수정한 뒤 생성했다. 제품 실패와 구분한다.

## 5. 설계 검토·한계

- 선택: 실행 앱에서 기존 콜백을 Serilog 속성으로 연결. 통신 라이브러리에 저장소 의존성을 추가하지 않는다. 전역 static logger/AsyncLocal 문맥을 사용하지 않고 실행별 로거와 이벤트별 속성을 사용한다.
- 전송: 공식 CompactJsonFormatter와 Seq CLEF API + 유한 큐. 기본 Seq sink의 일반 재시도·디스크 버퍼보다 기능은 작으며, 대신 유한 종료와 명시적인 Failed/Dropped 계수를 직접 관리한다. 자체 전송 코드·API 호환성 검증 책임이 남는다.
- 실패 배치 재전송 없음. Failed는 응답 확인 실패이며 서버에 기록되지 않았다는 증명이 아니다. 진단 로그는 best-effort이고 SQLite 업무 History나 명령 수신 보장을 대체하지 않는다.
- 로그 큐 포화는 새 로그를 버려 MQTT 경로의 대기를 막는다. 직렬화 CPU·기존 동기 콘솔 출력 비용까지 없애는 구현은 아니다.
- HTTP 1초·종료 flush 3초는 취소 기한이다. 실제 작업 종료와 자원 해제를 기다리며 OS 스케줄링·정리 시간을 포함한 절대 실시간 보장은 아니다. 시험용 HttpMessageHandler도 취소 계약을 지켜야 한다.
- 401/500·대량 포화·동시 기록은 대역 시험. 장시간 실장비 부하, 실제 인증 Seq 구성, 강제 프로세스 종료 시 로그 보존, 디스크 spool, 서비스 중단/복구, Dot 연결은 미검증/미구현이다.

예상 리뷰 질문: “왜 로그를 버리는가?” → 이번 진단 요구에서 MQTT 처리 가용성을 우선하며 유실을 집계한다. 무손실 요구가 생기면 저장·재전송 정책을 별도로 설계한다. “종료 시간만 기다리고 작업을 버리는가?” → HTTP 취소 후 실제 worker 종료와 HttpClient.Dispose를 확인했다. “기존 시험을 완화했는가?” → 정상 모드 stderr/출력 검사는 유지하고, 별도 SeqUnavailable 모드에서 오직 양수 실패 집계 한 줄만 허용한다. 전체 Docker 도구 시간만 늘렸으며 앱별 제한은 유지했다.

## 6. 종합 결과

#26의 구현·자동시험·실제 Seq/MQTT/HTTP 무응답 검증 완료. 요청 범위의 로컬 작업이며 Issue는 open, 커밋·푸시·PR·리뷰 게시·병합은 하지 않았다. #6·COM-06·과제1 전체 완료를 뜻하지 않는다.
