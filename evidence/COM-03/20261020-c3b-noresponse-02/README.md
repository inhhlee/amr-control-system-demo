# 검증결과 COM03-C3b 무응답 감지 20261020-c3b-noresponse-02

- 실행자: Codex, 2026-10-20. 사용자 요청으로 같은 #6 브랜치에서 C3b를 수행했다.
- 제품 코드·KeepAlive·C3a 프록시는 그대로 사용했다. 기존 실제 실행 도구에 측정 모드를 추가했다. 새 변경은 미커밋이며 C1·C2·C3a 근거를 보존했다.

## 1. 목표와 적용 기준

- 과제1 / COM-03 / [Issue #6](https://github.com/inhhlee/amr-control-system-demo/issues/6). 착수: 기능명세 v0.7 COM-03·공통규칙 C-09/C-10·COM03-C v0.9 C3b·과제1 계획 v0.45. 문서 원본은 C:/process-plan이다.
- 무업무메시지의 승인·구독된 세션에서 Broker→Client만 차단하고 감지 시간·이유·정리·종료 1을 확인한다. Broker 종료(C2)·최초 접속 실패(B)와 조건을 구분한다.
- 적용 NuGet MQTTnet 5.2.0.1603의 로컬 nuspec에서 repository commit `14463d1ef9f1f119514185c1832905b91bff827d`를 확인했다. [공식 소스의 해당 버전](https://github.com/dotnet/MQTTnet/blob/14463d1ef9f1f119514185c1832905b91bff827d/Source/MQTTnet/MqttClient.cs#L935)을 대조했다. 송신 공백 K 이후 PING·응답 대기 K/2, 검사 간격 250ms를 기준으로 현재 K=5초의 명목 감지 7.5초를 산정했다.
- 이 수식은 현재 버전·무업무메시지·루프백 조건의 예상이다. 별도의 최초 접속 제한 3초·KeepAlive 2배 유지 관찰·운영 SLA와 구분한다. 재접속·AMR Heartbeat·C4 전수 경합은 범위 밖이다.
- 별도 추적기가 없어 실행 ID·기준 커밋·소스/바이너리 해시를 연결한다.

## 2. 사전 기준·허용 오차

두 실행 모두 **장애 전에** criteria.json을 저장했다. [실행 01 기준](../20261020-c3b-noresponse-01/criteria.json)·[실행 02 기준](criteria.json)은 같은 허용 범위를 적용한다. 결과를 본 뒤 허용 범위를 수정하지 않았다.

| 항목 | 사전 기준·측정 정의 |
| --- | --- |
| 마지막 송신→감지 | 7.4~8.75초. 명목 7.5초에서 조기 허용 0.1초, 검사 간격 0.25초와 스케줄링·콜백 여유 1초 반영 |
| 응답 폐기→감지 | 2.4~3.5초. 명목 2.5초에 -0.1/+1.0초 |
| 차단→감지 | 위 7.4~8.75초에서 직전 송신→block ACK 위상을 뺀 범위. 위상 3초 이하. 0초·2초 대기 두 세션을 사전에 선택 |
| 장애 시작·감지 | 프록시 ControlApplied(block) UTC → 제품 ConnectionLost UTC. 파일 생성·프로세스 실행 시각을 기준으로 사용하지 않음 |
| 보조 단조 시계 | 같은 프록시의 응답 폐기→Client EOF 2.4~3.5초. 하네스 요청→감지 로그 관찰의 Stopwatch 구간과 벽시계 경과 차이 100ms 이내 |
| 도구·종료 제한 | 응답 폐기 관찰 9초·장애 watchdog 15초·감지 후 프로세스 종료 관찰 5초. 응답 재개·Ctrl+C·감지 전 Broker 종료 없음 |

마지막 송신은 프록시의 Client→Broker Forwarded 로그다. 라이브러리 내부 Send와 같다고 가정하지 않으며 로컬 전달·관찰 차이를 조기/지연 여유에 포함한다. 이 여유는 실습 판정값이며 성능 요구를 새로 확정한 값이 아니다.

## 3. 환경과 입력

| 항목 | 실제 값 |
| --- | --- |
| 코드 | C:/amr-control-system-demo / feature/6-mqtt-keepalive / d777ab7d8ef034d66a862343231268cd1476af24 + 기존 C1·C2·C3a 및 C3b 도구·문서 미커밋 변경 |
| OS·실행 | Windows 10.0.26200 / PowerShell 7.6.5 / SDK10.0.401·net10.0 / MQTTnet5.2.0.1603 |
| Broker | Docker Client/Engine29.7.2 / Mosquitto2.1.2 / 설치 이미지 eclipse-mosquitto:2.1.2-alpine 재사용. 실제 ID·mount 설정·명령은 각 environment.json·broker.conf·commands.jsonl |
| 실행 01 | 10:50:04.496~10:50:17.593 KST / Broker127.0.0.1:52556 / proxy127.0.0.1:52558 / Client amr-demo-318d536fd57d41ea958d7c593251f44e |
| 실행 02 | 10:50:36.608~10:50:49.107 KST / Broker127.0.0.1:52572 / proxy127.0.0.1:52573 / Client amr-demo-d861789eecf04ba580774016db6389d3 |
| 계약·입력 | MQTT5·Clean Start·Session Expiry0·KeepAlive5초, 기존 네 Topic QoS0 구독. 업무 발행자/Payload/Retain 입력 없음 |
| 실제 구성 | 제품 콘솔·Mosquitto·C3a TCP 프록시. MQTT 대역·방화벽 변경 없음. 새 이름·루프백 동적 포트·읽기 전용 실행별 설정·persistence false |
| 로그·정리 | 자식 콘솔에서만 Seq 비활성화. 기존 중지 컨테이너3개는 시작/중단하지 않음. 생성한 자원만 정리 |

```powershell
pwsh -NoProfile -File tests/manual/COM03-C/Verify-ResponseGate.ps1 -RunId <새-ID-1> -MeasureTimeout -BlockDelaySeconds 0
pwsh -NoProfile -File tests/manual/COM03-C/Verify-ResponseGate.ps1 -RunId <새-ID-2> -MeasureTimeout -BlockDelaySeconds 2
```

0초·2초는 구독 확인 후 차단 요청까지의 도구 대기다. 실제 송신→차단 위상은 0.2447141초·2.2393366초였고 각각 아래 기준에 반영했다. 도구 제어·정리 절차는 [공통 안내](../../../tools/COM-03/README.md)를 따른다.

## 4. 실제 결과와 판정

| 항목 | 실행 01 (추가 대기 0초) | 실행 02 (추가 대기 2초) |
| --- | --- | --- |
| 차단 ACK (KST) | 10:50:08.891947 | 10:50:42.399939 |
| 실제 응답 폐기 (KST) | 10:50:13.718960 | 10:50:45.292921 |
| 감지 (KST) | 10:50:16.221729 | 10:50:47.797950 |
| 차단→감지 / 해당 허용 범위 | 7.3297821초 / 7.1552859~8.5052859초 | 5.3980114초 / 5.1606634~6.5106634초 |
| 마지막 송신→감지 / 7.4~8.75초 | 7.5744957초·통과 | 7.6373481초·통과 |
| 폐기→감지 / 2.4~3.5초 | 2.5027692초·통과 | 2.5050294초·통과 |
| 폐기→Client EOF 단조 시간 | 2.4998850초·통과 | 2.5019891초·통과 |
| 감지→제품 종료 관찰 / 최대5초 | 26.9204ms·통과 | 26.3119ms·통과 |
| 벽시계/단조 경과 차이 / 최대100ms | 0.7553ms·통과 | 1.1408ms·통과 |
| 감지·정리 | ConnectionLost1건·ClientWasConnected=True·MqttCommunicationTimedOutException·SocketError=None·정리·종료1 | 같은 결과 |
| 조건 보존 | 응답2바이트 폐기·차단 후 응답 전달0·반대 방향 전달·차단 중 TCP 유지. Client가 먼저 EOF, Broker 여전히 실행 중 | 같은 결과 |
| 종료 개입 | 응답 재개·Ctrl+C·감지 전 Broker 중지 없음 | 같은 결과 |
| 자원 정리 | 제품/프록시 종료·시험 컨테이너 삭제·기존3개 상태 보존 | 같은 결과 |

각 열의 직접 증거: [실행01 측정](../20261020-c3b-noresponse-01/measurement.json)·[기준](../20261020-c3b-noresponse-01/criteria.json)·[프록시](../20261020-c3b-noresponse-01/proxy.jsonl)·[콘솔](../20261020-c3b-noresponse-01/console.stdout.log)·[정리](../20261020-c3b-noresponse-01/cleanup.json), [실행02 측정](measurement.json)·[기준](criteria.json)·[프록시](proxy.jsonl)·[콘솔](console.stdout.log)·[Broker](broker.log)·[차단 중 TCP](sockets-blocked.json)·[정리](cleanup.json).

MQTTnet 프로토콜 Reason은 두 번 모두 NormalDisconnection이었다. 원인 예외·사용자 취소 여부·ConnectionLost와 함께 해석하며 Broker 정상 종료나 앱 정상 종료로 치환하지 않는다. 라이브러리가 타임아웃 뒤 소켓을 닫고 나서 콜백을 전달하므로 Client EOF가 ConnectionLost보다 약3ms 먼저 기록됐다. 프록시의 선행 연결 종료로 만든 결과가 아니다.

## 5. 회귀·설계 선택·한계

- 같은 최종 도구로 [C3a 기본 모드](../20261020-c3b-c3a-regression-01/README.md)를 실행해 실제 승인·구독·응답 폐기 중 TCP 유지·전달 재개 후 Ctrl+C 정상 종료0·정리를 확인했다. 측정 2건·C3a 회귀 1건 모두 실제 MQTT이며 세 빌드에서 경고/오류0이다. 이번 C3b 실행 실패는 없다. 과거 C3a 도구 실패는 별도 기록 그대로 보존한다.
- [최종 대조](source-comparison.json)에서 소스72개는 각 실행 스냅샷과 일치한다. 기존 C2 최종 자동시험 소스69개도 그대로다. 기존761건 자동시험을 새로 실행한 것으로 집계하지 않는다. 기존 제품 기준이 같고 변경한 도구의 두 모드를 실제 실행해 확인했다.
- 선택 이유: 기존 MQTTnet 감지 동작을 관찰하고 프록시/하네스에서 시각·위상만 연결한다. 제품에 감시 폴링·새 상태 머신·추가 Timeout 정책을 도입할 필요가 없었다. 두 세션은 위상 영향을 비교하기 위한 것이며 지연 분포·최악 시간 보장은 아니다.
- 대안: 제품에 단조 감지 시각을 추가하면 내부 관찰 정확도가 높아지지만 제품 진단 계약·회귀 범위가 늘어난다. 이번에는 동일 PC의 UTC와 프록시/하네스 단조 시간으로 교차 확인했다. MQTTnet 자체 KeepAlive 판정은 UTC 기반이며 이 시험이 시스템 시간 변경 내성을 검증하지는 않는다. 다중 장비 시계나 실제 지연 분포가 필요해지면 측정 설계를 재검토한다.
- PR 질문: 왜 차단→감지가 두 번 다르나? 차단 전 송신 공백의 위상 차이 때문이다. 왜 Reason만으로 판단하지 않나? 프로토콜 코드가 사용자 종료 원인을 표현하지 않기 때문이다. 7.4~8.75초를 운영 보장으로 써도 되나? 이 버전·설정·단일 PC 실습 범위의 사전 판정값이며 부하·원격 네트워크·시각 변경은 미검증이다.
- TCP read 경계는 MQTT 패킷 경계가 아니다. 같은 Client ID의 Broker PINGREQ/PINGRESP·폐기2바이트·타임아웃 예외·반대 방향 전달·실행 중 Broker·Client EOF를 함께 확인했다. 실제 Seq 수집·MQTT Explorer GUI·부하·자동 복구·C4 추가 경합은 미실행이다.

## 6. 종합 결과

- C3b 실제 무응답 감지 측정 통과: 두 사전 위상 조건·기준·허용 오차·측정·이유·종료·정리의 근거를 연결했다.
- #6은 C4 추가 중복/늦은 알림·A~C 최종 취합이 남은 부분 완료다. COM-03 전체·과제1 전체 완료로 표시하지 않는다.
- 커밋·푸시·PR·Issue 수정/종료·외부 전송은 수행하지 않았다. 업무 기록 원본은 C:/process-plan의 해당 날짜 일일업무·COM03-C 담당 문서·과제1 계획에 요약한다.
