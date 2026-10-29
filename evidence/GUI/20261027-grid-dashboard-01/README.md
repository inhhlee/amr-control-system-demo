# Figma 목업 · Windows Forms 격자 MQTT 연결 검증

## 1. 목표와 기준

2026-10-27 사용자 후속 요청으로 [Figma](https://www.figma.com/design/ZyTPFPyj2CGcYygMxBGajo?node-id=3-2)의 다섯 영역 중 기존 대시보드·지도·선택 상세에 검증된 격자 상태를 연결했다. GUI-01/02/05 일부이며 알람/DB 이력·명령 ACK·실제 Core 업무 서비스 전체를 완료 처리하지 않는다. 브랜치는 `feature/46-grid-map-integration`, HEAD는 `4e44fa880cb0d1fcccb000dbc53e634fd90381b5`이고 이관 및 GUI 연결은 미커밋 변경이다.

기준 문서는 기능명세 v0.13, 공통데이터명세 v0.12, 결정사항 v0.16, 과제3 계획 v0.11의 기존 계약이며 이번 사용자 요청을 연결 범위로 추가했다. Figma MCP는 Starter 호출 한도로 설계 코드/스크린샷을 반환하지 못했다. 브라우저에서 `03 결과 · 지도·알람 상세`(3:2)를 확인하고 기존 네 카드·지도/선택 상세·알람/명령·하단 영역을 재사용했다. [관찰 화면](figma-map-reference.png). Figma 원본 수정·새 자산 다운로드·픽셀 단위 일치 검증은 수행하지 않았다.

## 2. 코드·환경·소유권

- 새 제품 파일: GUI 루트의 GridMapDataProvider.cs·MainForm.Grid.cs·MapCanvas.Grid.cs. 기존 MainForm/Designer·MapCanvas·Program에 연결했다. 같은 시험 프로젝트에 GridDashboardTests.cs를 추가했다. 새 프로젝트·폴더·패키지·Core 계약은 없다.
- 기존 호스트 → HTTP 상태/시연 제어 → GUI 공급자 → 같은 프레임의 지도/목록/요약/상세 순서다. 호스트가 기존 MQTT 구독·검사·큐와 Simulator 실행을 소유한다. GUI는 이동/MQTT·경로 알고리즘을 다시 구현하지 않는다.
- 상태 조회는 200ms 순차 실행, 자체 HttpClient의 제한2초, localhost HTTP만 허용하고 자동 redirect를 사용하지 않는다. 한 공급자에 구독 하나를 허용하며 화면 종료는 취소한 구독·진행 요청의 정리를 기다린다. 앱 시작 지점이 자신이 만든 공급자/HttpClient를 정리한다. HTTP 조회 재개는 MQTT 재접속/재전송을 추가한 것이 아니다.
- X=열/Y=-행·1칸=1m, 위치는 receivedTelemetry/position으로만 만든다. 계산 경로와 수신 궤적을 구분하고 Heartbeat 시각을 생존 확정으로 확대하지 않는다. API 장애는 마지막 수신값·연결 미확인을 표시한다. 수신 종료 Closed와 Broker 장애, 취소 Task 종료와 실제 로봇 정지는 다르다.
- Windows 10.0.26200, SDK10.0.401, PowerShell7.6.5, MQTTnet5.2.0.1603, Mosquitto2.1.2-alpine. 이전 격자 검증과 같은 설치 환경이다. 실제 폼용 복사 [설정](ui-settings.json)은 이동3000ms, 기본 map-demo 설정600ms는 변경하지 않았다.
- 검증 세션: [check-session.json](check-session.json), HTTP57029·Broker57027, Release 실행본. 실제 GUI 출발/초기화/중단과 수신 상태를 확인했다. [호스트 기록](../../AMR/20261027-gui-grid-check-01/session.json).
- 먼저 띄운 Debug 창과 HTTP60292/Broker60291의 `20261027-gui-grid-host-01`은 수동 입력이 관찰되어 유지했다. [관리 정보](gui-session.json), [호스트 정보](../../AMR/20261027-gui-grid-host-01/session.json). Stop-MqttMap.ps1의 해당 RunId로 소유 호스트/Broker를 정리한다. GUI 닫기는 호스트 임무 중단 명령이 아니다.

## 3. 입력·예상·실제·판정

| ID | 입력·예상 | 실제·판정 | 근거 |
| --- | --- | --- | --- |
| GUI3-G01 수신 계약 | 계산 위치와 받은 위치가 다를 때 수신값만 표시, 수신 전/Heartbeat만 있을 때 위치 없음 | 통과. 잘못된 ID/좌표/비MQTT 응답 거부·기존 조회 입력/취소/종료 계약도 확인 | [최종48건 TRX](tests/gui-final.trx) |
| GUI3-G02 표시/선택 | 같은 프레임의 좌표·속도·상태와 경로/수신 궤적·Marker/목록·상세/요약 일치 | 통과. 실제 도착 X5/Y-4·대기1·속도0·15회·3/3과 수신API가 일치 | [실제 최종 화면](detour-final.png), [AX](detour-final.txt), [API](detour-final.json) |
| GUI3-G03 제어/우회 | 실제 출발→정상9회, 초기화→위치 비움, 우회 입력→15회, W1/W2/G 방문11/13/15회 | 통과. 출발/초기화는 실제 GUI 버튼. 우회 장애물은 검증용 API로 주입한 뒤 GUI에서 출발. 표준 입력(2,2) 차단으로15회 도착 | [정상 화면](normal-arrival.png), [우회 입력](detour-input.json), [실제 표본](detour-samples.jsonl) |
| GUI3-G04 NoPath | 경로 차단 시 remainingDistance=null은 유효한 대기 상태 | 최초 연결에서 JsonException 발생, nullable DTO와 “경로 없음” 표시로 수정·재시험 통과 | [최초API](initial-api-error.json), [오류 화면](initial-format-error.png), [수정 후 NoPath](no-path.png), 최종 TRX |
| GUI3-G05 중단/장애 | 재출발 후 GUI 중단→이동 정지·정리, 서버 종료→마지막 값 유지·제어 불가, 다시 유효 응답이면 복구 | 통과. 실제 중단 후2회로 고정. 실제 서버 종료의 마지막값/미확인 및 버튼 비활성 확인. 복구는 HTTP 대역 자동시험 | [중단API](cancel-state.json), [800ms 뒤](cancel-still.json), [서버 종료 화면](server-stopped.png), [AX](server-stopped.txt) |
| GUI3-G06 수명/배치/회귀 | 폼 닫힘이 진행 HTTP 정리를 기다리고 최소1000×720에서 내용/버튼 표시, 기존 Mock 회귀 | 통과. 최종 GUI48(기존32+신규16), 실패/skip0. 최종 전체 솔루션 Release 빌드 경고0·오류0 | [빌드](build-final.log), [시험](gui-final.log), [검증 호스트 cleanup](../../AMR/20261027-gui-grid-check-01/cleanup.json) |

최종 실제 우회의 수신은 Telemetry224·Heartbeat45, 마지막 X5/Y-4·IDLE·속도0이다. 최종 발행/수신 시각은 detour-final.json에서 대조했고 11/13/15 방문 순서도 확인했다. 이번에는 다른 모듈 자동시험을 반복하지 않았다. 동일 소스의 [앞선 통신768/Simulator200/Map63](../../AMR/20261027-grid-integration-01/README.md)은 기존 근거로 재사용하며 이번 GUI48과 “이번 전체 재실행”으로 합산하지 않는다.

실제 버튼 조작은 Release07의3000ms 설정에서 수행했다. 이후 조회 입력/CTS 정리와 미연결 알람의 “미확인” 표시를 보완한 최종 빌드의 GUI48건을 실행했다. 최종 소스에서는 별도100ms 설정으로 API 장애물/출발 입력을 주고 실제15회·방문11/13/15·최종 송수신 시각·IDLE/속도0과 GUI 표시를 재확인했다. [최종 소스 화면](latest-visible.png) · [AX](latest-final.txt) · [API](latest-final.json) · [설정](final-ui-settings.json) · [실행 관리](final-ui-session.json). 최종 실행은 위치17건/Heartbeat2건이며 앞선3000ms 실행의224/45와 구분한다.

최종 HTTP49201·Broker49200의 `20261027-gui-grid-final-01`과 GUI는 사용자 확인용으로 열어 두었다. GUI를 닫은 후 `pwsh -NoProfile -File tests/manual/AMR01/Stop-MqttMap.ps1 -RunId 20261027-gui-grid-final-01`로 해당 소유 호스트/Broker를 정리한다. 앞선 사용 중 세션60292는 별도 RunId `20261027-gui-grid-host-01`로 관리한다.

## 4. 실패와 확인 제한

- 최초 빌드의 WinForms 디자이너 직렬화 속성 지정 누락, 시험 코드의 HistoryResult/xUnit 비동기 단언 형식 오류는 수정했고 실패 로그를 보존했다. gui-04는 시험 HTTP 대역 누락으로44건 중1건 실패했다. 대역을 구성한 gui-05는44건, NoPath 보완 gui-06은45건, 수명/최소 크기 gui-release-07은47건, 최종 조회 계약 보완은48건이다.
- 제한 실행의 gui-03 시험 호스트는 결과 없이 대기하여 중단한 뒤 허용된 실행에서 검증했다. 초기 숨김 GUI가 보이지 않아 해당 소유 프로세스만 정리하고 명시적 검증 창으로 전환했다.
- 처음 창에서 수동 입력이 감지돼 별도 세션을 사용했다. Native sky의 이동된 창/오래된 상태 오류는 새 CUA 창 바인딩으로 전환했다. 검증용 Map 좌표 클릭은 자동 도구에서 입력 위치를 확정하지 못해 성공으로 집계하지 않았다. 장애물 셀 hit test·표시·금지 셀·HTTP 요청은 자동시험, 실제15회 표시는 API 입력+GUI 출발로 확인했다. 앞선 브라우저의 이동 중 장애물 클릭은 [이전 검증](../../AMR/20261027-grid-integration-01/README.md)의 별도 근거다.
- 전체 Figma 자산/정밀 치수는 MCP 제한으로 미확인이다. 새 정적 자산을 추가하지 않았고 기존 스타일/컨트롤을 유지했다. 알람/DB 업무 이력·실제 명령/ACK·Core 업무 서비스·A* 비교·다중 AMR은 이번 미실행이다.

## 5. 설계 이유·대안·리뷰 근거

기존 호스트를 재사용하면 최신 MQTT 단절 수정과 단일 세션의 경유 순서·최종 수신 확인을 유지할 수 있다. GUI에는 화면 공급자와 그리기만 추가했다. 대안은 GUI 프로세스에 세션을 직접 넣거나 Core에 새 격자 계약을 만드는 것이다. 전자는 웹 호스트와 앱 시작/종료 책임의 결합이 커지고, 후자는 이번 시연보다 큰 업무 계약 설계가 필요하다. 현재 방식은 JSON과 지도 사본·주기 조회 비용을 추가한다. 실제 운영 부하의 성능 우열은 측정하지 않았다.

- **수신 전 Marker가 없는 이유:** AMR 위치는 receivedTelemetry/position만 사용하며 계산 current를 대신 표시하지 않는다. 단위시험과 실제 초기 화면에서 확인했다.
- **중단 뒤 BUSY가 남을 수 있는 이유:** 마지막 수신값과 Task 종료를 구분한다. CommandId/최종 STOP ACK가 없으므로 물리적 정지로 바꾸지 않는다. 실제 중단 API/화면에 그 차이가 남는다.
- **언제 다른 구조로 바꾸나:** 실제 Core/업무 Command·ACK·다중 AMR 통합이나 주기 JSON 비용 문제가 확인되면 공급자/전송 계약을 재설계한다. 현재 GUI 인터페이스와 Mock 모드는 유지한다.

## 6. 현재 단계

로컬 구현·검증 완료, 커밋·스테이징·푸시·PR·병합 미수행. [최종 감사](final-audit.json)에서 기존 stash·백업·원래50파일·격자 이관31파일과 Core/MQTT 소스를 대조했다. 기존 GUI4파일은 사용자 후속 요청으로 변경했고 이전 소스는 HEAD와 이관 전 해시로 복구 가능하다. 이전 증거/검증 스크립트는 해당 실행 당시 기준으로 그대로 보존한다.
