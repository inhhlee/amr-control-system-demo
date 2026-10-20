# 검증결과 COM-03 / COM03-C1 KeepAlive / 20261020-c1-keepalive-02

- 작성자·실행자: Codex, 2026-10-20. 사용자 요청으로 #6의 C1만 수행했다.
- 실제 제품 소스는 변경하지 않았다. 신규 수동 시험 도구와 실행 증거·README는 미커밋 상태다.

## 1. 목표와 적용 기준

- 과제1 / COM-03 / COM03-C1, [Issue #6](https://github.com/inhhlee/amr-control-system-demo/issues/6).
- 기능명세 v0.7 COM-03, 공통규칙 C-09, COM03-C 로컬 안내 v0.6의 C1, 과제계획 v0.42를 착수 기준으로 적용했다. 현재 사용자 요청은 앞선 과제1 보류 중 C1 범위를 재개하는 지시로 적용했다. 원본은 C:/process-plan에서 관리한다.
- 사전 성공 기준: 적용 KeepAlive 5초의 2배 이상 관찰(요청 관찰 12초, 최소 10초), 같은 Client ID의 PINGREQ/PINGRESP 최소 2쌍, 업무 PUBLISH 0건, Ctrl+C 후 5초 이내 정상 종료 0, 시험 컨테이너 정리와 기존 컨테이너 상태 보존.
- 확인 범위: 실제 MQTTnet 클라이언트와 격리 Mosquitto의 무업무메시지 연결 유지. 구독·PING·해제 제어 패킷은 사용한다.
- 제외 범위: 연결된 Broker 종료 감지(C2), 승인 후 응답 차단·감지 시간(C3), 상세 중복·종료 경합(C4), AMR Heartbeat 감시·자동 재접속·재구독. 운영 감지 제한과 SLA를 확정하지 않는다.
- 별도 spec_revision/verification_index 추적 파일은 없어 실행 ID·Git 기준 커밋·소스 해시로 식별했다.

## 2. 실행 환경

| 항목 | 실제 값 |
| --- | --- |
| 실행 ID | 20261020-c1-keepalive-02 |
| 실행 시각 | 2026-10-20 09:18:58.211~09:19:15.330 KST. 원본 로그는 UTC 또는 명시된 ISO 8601 오프셋 |
| 위치·브랜치·코드 | C:/amr-control-system-demo / feature/6-mqtt-keepalive / develop d777ab7d8ef034d66a862343231268cd1476af24. 원격 확인·ff-only 갱신 후 git flow feature start로 생성 |
| OS·SDK·라이브러리 | Windows 10.0.26200 / .NET SDK 10.0.401, net10.0 / MQTTnet 5.2.0.1603 |
| Docker·Broker | Docker Client/Engine 29.7.2 / eclipse-mosquitto:2.1.2-alpine, Mosquitto 2.1.2. 이미지 ID는 environment.json 참조. 설치 이미지 재사용, 다운로드 없음 |
| 격리 주소·관리 | 127.0.0.1:64786 → 전용 컨테이너 1883. 이름 amr-c1-20261020-c1-keepalive-02. 기존 Compose·데이터 경로 사용 안 함 |
| Broker 설정 | 실행별 broker.conf만 읽기 전용 mount, persistence false, stdout debug 로그. 별도 영속 데이터 volume 없음 |
| MQTT 설정 | MQTT 5 / Clean Start / Session Expiry 0 / KeepAlive 5초 / 접속·해제 제한 3초 / 실행별 새 Client ID |
| Topic·QoS·Retain·입력 | 기존 네 Topic amr/+/heartbeat, telemetry, alarm, ack에 QoS 0 구독. 업무 발행자·Payload·Retain 입력 없음 |
| 실제 구성요소·대역 | 실제 제품 콘솔·실제 Mosquitto. MQTT 프로토콜 대역 없음. Windows CTRL_C_EVENT는 시험 도구가 만든 격리 콘솔에만 전달 |
| Seq | 시험 자식 프로세스에서만 비활성화. 기존 Seq 설정·컨테이너 변경 없음 |
| 증거 | [환경](environment.json), [소스 해시](source-version.json), [최종 소스 대조](source-comparison.json), [명령 기록](commands.jsonl), [mount](broker-mounts.json) |

Docker Desktop은 착수 시 엔진 미기동 상태여서 기동했다. 최초 sandbox 실행 거부 후 허용된 실행 환경을 사용했다. 엔진 기동 뒤 기존 컨테이너 3개가 모두 중지 상태임을 기록하고 시험 전후 상태를 비교했다. Docker Desktop은 기동 상태로 남겼다.

## 3. 입력과 절차

```powershell
# Windows / PowerShell 7 / 기동된 Docker Linux Engine / 설치된 Mosquitto 이미지 필요
# 새 실행 ID만 허용하며 기존 evidence를 덮어쓰지 않는다.
pwsh -NoProfile -File tests/manual/COM03-C/Verify-KeepAlive.ps1 -RunId <새-실행ID>
```

1. 기존 컨테이너·이미지를 조회하고 콘솔 프로젝트를 --no-restore로 빌드한다. 적용 소스·시험 도구 해시를 저장한다.
2. 실행별 설정을 만들고 docker run --pull never로 별도 이름·동적 루프백 포트의 Broker를 기동한다.
3. 기존 수신 콘솔을 해당 포트로 접속해 구독 완료를 확인한다. 업무 발행자는 실행하지 않는다.
4. 설정의 KeepAlive를 읽고 구독 완료 이후 Stopwatch로 12초 이상 관찰한다. 도중 프로세스 종료는 실패다.
5. 콘솔 구성원이 시험 제품·제어 프로세스뿐인지 확인하고 CTRL_C_EVENT를 보낸다. 종료 0·해제·Dispose·빈 stderr를 검사한다.
6. 같은 Client ID의 Broker PING·정상 disconnect와 PUBLISH 0건을 대조한다. 로그를 저장한 뒤 이미 연결 해제된 시험 컨테이너만 중지·삭제하고 기존 컨테이너 상태를 대조한다.

## 4. 실제 결과와 판정

| 시험 ID·기대 | 실제 결과 | 판정·근거 |
| --- | --- | --- |
| C1-BUILD 빌드 성공 | 경고 0·오류 0 | 통과, [build.log](build.log) |
| C1-IDLE 최소 10초 무업무메시지 연결 유지 | 09:19:01.385~09:19:13.476 KST, Stopwatch 12.0899808초 | 통과, [session.json](session.json), [콘솔](console.stdout.log) |
| C1-PING 같은 Client ID의 2쌍 이상 | amr-demo-68db234913db461c91315bf20290b92e, 09:19:06.461 / 09:19:11.600 KST 각각 PINGREQ 수신·PINGRESP 송신, 총 2쌍 | 통과, [ping.log](ping.log), [전체 Broker 로그](broker.log) |
| C1-NOMSG 업무 PUBLISH·수신 0 | Broker PUBLISH 0건, 콘솔 업무 수신·검사·큐 접수 0건 | 통과, [verification.json](verification.json), Broker·콘솔 원본 |
| C1-EXIT Ctrl+C 후 5초 이내 종료 0 | 신호 09:19:13.499824 / 종료 확인 09:19:13.538863 KST. 정상 구독 해제·DISCONNECT·Dispose·종료 0, stderr 비어 있음 | 통과, [session.json](session.json), [console.stderr.log](console.stderr.log) |
| C1-CLEAN 시험 자원 정리·기존 상태 유지 | 시험 컨테이너 삭제, 기존 3개 모두 중지 상태 유지, 별도 데이터 volume 생성 없음 | 통과, [cleanup.json](cleanup.json), [전](containers-before.jsonl)·[후](containers-after.jsonl), [최종 Broker 로그](broker-final.log) |

최소 10초는 연결 유지 관찰 조건이다. 단절·무응답의 고정 감지 제한으로 적용하지 않는다. PINGRESP는 Broker 송신 로그이며 동일 세션의 두 교환·연결 유지·정상 해제로 C1을 판정했다. 클라이언트 패킷 캡처나 장시간 부하를 추가 수행한 결과는 아니다.

## 5. 문제와 후속 조치

- [실행 01](../20261020-c1-keepalive-01/README.md)은 콘솔 출력 파일이 아직 비어 있을 때 .Contains를 호출한 시험 도구 오류로 관찰 전에 실패했다. 제품 결함·C1 통과로 표시하지 않는다. null 확인만 보완한 뒤 별도 실행 02에서 실제 시험을 다시 수행했다. 원본 실패·정리 로그를 보존했다.
- 기존 제품 C#·프로젝트·SDK·패키지·공유 Broker 설정은 변경하지 않았다. 이 요청 때문에 기존 758개 자동시험을 반복하지 않았으며 그 개수를 이번 실행 실적으로 집계하지 않는다.
- 재현 도구는 기존 COM03-B의 검증된 격리 콘솔·CTRL_C_EVENT 방식을 재사용했다. 자체 MQTT Ping 구현을 추가하지 않고 MQTTnet KeepAlive와 Mosquitto 진단 로그를 사용한다.
- C2 단절 원인·실제 연결 중 Broker 종료, C3 승인 후 무응답, C4 중복·늦은 알림의 추가 검증은 남아 있다. 정리 단계의 Broker 중지는 클라이언트 해제 이후이며 C2 검증이 아니다.

## 6. 종합 결과

- 시험 판정: 통과 — C1 실제 MQTT 연결 유지 한 시나리오, PING 교환 2쌍. 제품 동작의 자동 단위시험 실행은 없음.
- 코드·환경·설정·Client ID·관찰 시간·종료 코드·로그를 연결했다. 최종 소스 대조가 일치하는 범위에서 위 기준 코드를 시험한 근거로 사용한다.
- #6은 부분 검증 완료·진행 중이며 COM-03 전체·과제1 전체 완료가 아니다. C2 이후는 후속 사용자 요청에 따라 진행한다.
- 시험 도구·증거·README는 로컬 미커밋이다. 커밋·푸시·PR·Issue 게시 변경·종료는 수행하지 않았다.
- 업무 기록 원본: C:/process-plan/일일업무/2026-10-20.md, COM03-C 담당 문서와 과제1 계획에 이 결과를 연결한다.
