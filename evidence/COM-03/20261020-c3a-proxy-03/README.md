# 검증결과 COM03-C3a 응답 차단 환경 20261020-c3a-proxy-03

- 실행자: Codex, 2026-10-20. 같은 Issue #6 브랜치에서 사용자 요청의 C3a만 수행했다.
- 제품 코드·기존 C1·C2 도구를 변경하지 않았다. 새 도구·README·근거는 미커밋이다.

## 1. 목표와 적용 기준

- [Issue #6](https://github.com/inhhlee/amr-control-system-demo/issues/6), 기능명세 v0.7 COM-03, 공통규칙 C-09·C-10, COM03-C 안내 v0.8 C3a, 과제1 계획 v0.44를 착수 기준으로 적용했다. 원본은 C:/process-plan이다.
- 사전 성공 조건: 격리 Broker에 프록시 경유 실제 접속 승인·구독, 같은 세션에서 Broker→Client만 차단, 실제 응답 폐기·Client→Broker 전달 지속, 차단 중 프록시 양쪽 TCP와 제품 소켓 Established, 전달 재개·정상 종료·자원 정리와 기존 컨테이너 상태 보존.
- 폐기 관찰 상한 9초, 제어 ACK 2초, 정리용 Ctrl+C 후 5초. 제품의 무응답 감지 기준·허용 오차는 정하지 않았으며 C3b에서 측정한다. 응답 폐기 확인 후 전달을 재개하므로 감지 시험이 아니다.
- 별도 추적기는 없어 실행 ID·기준 커밋·소스 해시로 식별한다. 자동 재접속·AMR Heartbeat·GUI·Seq·운영 SLA는 확인 범위 밖이다.

## 2. 실행 환경

| 항목 | 실제 값 |
| --- | --- |
| 실행 시간 | 2026-10-20 10:08:18.726~10:08:29.431 KST. 원본 시간은 UTC |
| 코드 | C:/amr-control-system-demo / feature/6-mqtt-keepalive / d777ab7d8ef034d66a862343231268cd1476af24 + 기존 C1·C2 및 새 C3a 미커밋 변경 |
| 환경 | Windows 10.0.26200 / PowerShell 7.6.5 (.NET 10.0.11) / SDK 10.0.401 / MQTTnet 5.2.0.1603 |
| Broker | Docker Client/Engine 29.7.2 / Mosquitto 2.1.2 / eclipse-mosquitto:2.1.2-alpine 설치 이미지 재사용. 이미지 ID는 environment.json |
| 격리 자원 | amr-c3a-20261020-c3a-proxy-03, 컨테이너 ID 9890caa2a553a4b1de02298b654715c28584422efb1ee9611a37f757f2250165 |
| 주소·PID | Broker 127.0.0.1:62515 / proxy 127.0.0.1:62516, PID 41588 / 제품 PID 27292 |
| MQTT | MQTT 5 / Clean Start / Session Expiry 0 / KeepAlive 5초 / 네 기존 Topic QoS 0 구독 / 업무 발행자·Payload·Retain 입력 없음 |
| Client ID | amr-demo-147d173a2da849a185c2769569db6f9d |
| 구성 | 실제 제품 콘솔·실제 Mosquitto·새 TCP 프록시. MQTT 대역 없음. 자식 콘솔에서만 Seq 비활성화 |
| Broker 설정 | 실행별 broker.conf 읽기 전용 mount, persistence false, 별도 데이터 volume 없음. 기존 Compose·서비스·방화벽 변경 없음 |
| 소스·바이너리 | [소스 72개](source-version.json)·[바이너리 4개](binary-version.json), [최종 소스 대조](source-comparison.json)에서 72개 일치. 기존 C2 최종 자동시험 소스 69개도 전부 일치 |
| 직접 근거 | [환경](environment.json)·[명령](commands.jsonl)·[입력 설정](broker.conf)·[결과](verification.json)·[정리](cleanup.json) |

Docker 실행은 sandbox에서 거부돼 허용된 실행 환경으로 수행했다. 기존 컨테이너 3개는 전후 모두 중지 상태다. 새 설치·이미지 다운로드·기존 서비스 시작/중단은 없었다.

## 3. 입력과 절차

```powershell
pwsh -NoProfile -File tests/manual/COM03-C/Verify-ResponseGate.ps1 -RunId <새-실행ID>
```

[도구 안내](../../../tools/COM-03/README.md)에 수동 시작·block/resume/stop·재실행 절차를 연결한다. 실행 ID는 덮어쓰지 않는다. 새 프록시는 한 세션만 받으며 MQTT를 해석하지 않는다. 기존 C1·C2의 격리 Broker·숨김 콘솔/CTRL_C_EVENT 패턴을 재사용했다.

## 4. 실제 결과와 판정

| 시험·예상 | 실제 결과 | 판정·근거 |
| --- | --- | --- |
| C3a-BUILD / 컴파일·구문·제품 빌드 성공 | Add-Type 컴파일·PowerShell parse 성공, 콘솔 빌드 경고/오류 0 | 통과, 실제 프록시 실행·[build.log](build.log) |
| C3a-FORWARD / 실제 승인·구독·양방향 전달 | 같은 Client ID로 Connected·Subscribed, 차단 전 응답 16+9바이트 전달 | 통과, [콘솔](console.stdout.log)·[프록시](proxy.jsonl)·[Broker](broker.log) |
| C3a-BLOCK / 응답만 폐기·TCP 유지 | block 10:08:22.751524 KST → 같은 Client PINGREQ/PINGRESP 10:08:27.734 → 응답 2바이트 폐기 10:08:27.735071. 차단 중 응답 전달 0건·반대 방향 2바이트 전달 | 통과, [proxy.jsonl](proxy.jsonl)·[broker.log](broker.log)·[verification.json](verification.json) |
| C3a-TCP / 즉시 닫지 않은 연결 | 10:08:27.779245 KST 조회 시작, 프록시 양쪽과 제품의 TCP 3개 행이 Established(State=5). resume 이전 EOF 없음 | 통과, [sockets-blocked.json](sockets-blocked.json) |
| C3a-RESUME / 이후 전달·정상 종료 | resume 10:08:28.015770 KST, 이후 UNSUBACK 응답 9바이트 전달. UserCanceled·구독 해제·Disconnected·Dispose·ExitCode=0, 프록시 EOF·종료 0, stderr 비어 있음 | 통과, [프록시](proxy.jsonl)·[콘솔](console.stdout.log)·[종료](console.session.json) |
| C3a-CLEAN / 격리 자원 정리 | 생성한 콘솔·프록시 종료, 시험 컨테이너 삭제, 기존 컨테이너 3개 상태 보존 | 통과, [정리](cleanup.json)·[전](containers-before.jsonl)·[후](containers-after.jsonl) |
| C3a-STOP / 접속 전 대기 취소 | 별도 stop-01에서 Client 접속 전 stop → 종료 0, 63069 포트 다시 bind 성공 | 통과, [stop-01](../20261020-c3a-stop-01/README.md) |

proxy.jsonl은 TCP read 단위의 바이트 수다. 같은 세션·시각의 Broker PINGRESP 기록과 함께 2바이트 폐기를 해석했으며 자체 MQTT 해석기를 구현하지 않았다. TCP 관찰은 위 짧은 차단 구간의 사실이며 장기 생존·제품 감지 시간의 근거가 아니다.

## 5. 문제와 후속 조치

- [실행 01](../20261020-c3a-proxy-01/README.md)은 빈 출력에 Contains를 호출한 도구 오류로 접속 전에 실패했다. 제품 결함·C3a 통과로 표시하지 않는다. 자원 정리와 기존 상태 보존을 확인하고 새 ID로 재시험했다.
- [실행 02](../20261020-c3a-proxy-02/README.md)는 실제 환경 통과다. 이후 정적 검토에서 stop이 응답 쓰기 gate를 기다릴 수 있는 점을 보완했다. stop은 gate와 무관하게 취소하며 최종 실행 03과 별도 stop-01을 확인했다. backpressure 부하를 실제 재현한 시험으로 확대하지 않는다.
- 선택 이유: 프로토콜 해석 없이 양방향 스트림 두 개·각 4KB 버퍼로 필요한 조건만 만든다. block/resume는 응답 쓰기와 직렬화하고 stop·EOF는 두 전달 작업의 실제 종료·소켓 반환까지 소유한다. 폐기된 응답은 저장·재생하지 않는다.
- 대안: 범용 프록시는 여러 장애·세션 지원에 유리하나 설치·설정과 검증 범위가 커진다. 여러 Client·지연·속도 제한이 필요해지면 검토한다. 현재 도구는 운영 처리량·무손실·재접속을 제공하지 않는다.
- PR 질문: 왜 TCP를 닫지 않는가? C2의 즉시 단절과 C3의 응답 미전달을 구분하기 위해서다. 왜 감지 통과가 아닌가? 실제 폐기 확인 뒤 전달 재개·Ctrl+C 정상 종료했기 때문이다. 명령 적용과 쓰기가 겹치면? 응답 gate의 ACK 이후에는 응답을 폐기하지만 ACK 전에 OS에 전달된 바이트를 회수하지는 않는다.
- 제품·기존 시험 소스 69개는 C2 최종 스냅샷과 일치한다. 기존 761건 자동시험을 이번에 재실행·신규 실적으로 집계하지 않는다. C1·C2의 기존 제품 근거는 유지된다.
- 남은 범위: C3b의 사전 감지 기준·관찰 상한·허용 오차·실제 감지 시간/비정상 종료, C4 추가 중복·늦은 알림 및 최종 근거 취합. 실제 Seq·MQTT Explorer GUI·자동 복구 미실행.

## 6. 종합 결과

- C3a 도구·실제 환경 검증 통과. #6 전체 및 COM-03 전체·과제1 전체는 진행 중이다.
- 같은 #6 브랜치에서 기존 변경을 보존했다. 커밋·푸시·PR 생성·Issue 수정/종료·외부 메시지 전송은 수행하지 않았다.
- 원본 기록 위치: C:/process-plan/일일업무/2026-10-20.md, 과제1 계획, COM03-C 담당 문서. 상세 실행 증거는 이 코드 저장소에서 관리한다.
