# 최신 GUI의 실제 MQTT 연결 검증 (#58)

## 목적·완료 범위

최신 관제 GUI에서 실제 가상 AMR 위치·상태와 서버 단절·자동 재접속·수신 복구를 확인한다. 기본 Mock·기존 격자 실행·통신 콘솔을 유지하고, trial의 `ReconnectingCommunicationSession`을 실제 DataProvider에서도 재사용했다.

- 관련: [Issue #58](https://github.com/inhhlee/amr-control-system-demo/issues/58), GUI-01/02/05, COM-03/04/06, D-08/09/11.
- 적용 원본: 공통규칙 v0.12·공통데이터명세 v0.12·기능명세 v0.13·결정사항 v0.16, 2026-11-01 사용자 연결 요청. 원본 계획 헤더의 기존 충돌은 확정 버전으로 취급하지 않는다.
- 코드: `feature/58-gui-mqtt-live`, 기준 `755e1405dd4bb74b54a80a97abdc47d244fcfd99` + **미커밋 변경**. [소스·입력 해시/환경/결과](verification.json). 원격 develop에 반영된 결과가 아니다.
- 실행: Windows Forms·.NET SDK 10.0.401·MQTTnet 5.2.0.1603·Mosquitto 2.1.2-alpine. 실제 서버는 격리 `127.0.0.1:18885`, 기존 1884 서버 유지. 세부 실행 시각/OS/컨테이너는 [실제 결과](live-final/live-results.json).

## 변경 내용과 이유

1. `Amr.Infrastructure/MqttAmrDataProvider`: 기존 공통 통신 모듈의 관측을 Core의 조회/초기 스냅샷·변경 스트림으로 변환한다. MQTT 형식을 폼에서 직접 해석하지 않는다. GUI는 Core 공개 계약을 사용하고, 시작 지점에서 실제 공급자를 조립한다.
2. `--mqtt`/VS `MQTT Live`: 기존 최신 화면 배치를 재사용하며 실제 수신 출처·위치·속도·운행·생존 신호·서버 접속 상태를 표시한다. 대상 한 대를 지정한다. 기본 AMR_001·1884·5초/추가5회는 trial의 시험 기본값이다.
3. 위치 수신 전에는 가상 좌표/IDLE을 만들지 않는다. 단절·새 세션의 첫 Telemetry 전까지 기존 좌표와 원래 상태를 유지하되 **마지막 수신값/현재 운행 미확인**을 표시한다. Broker 접속과 AMR 생존을 구분한다.
4. 공통 재접속 세션의 `SessionObservation` 이벤트를 통해 정상 Heartbeat마다 세션·순서·수신 시각·생존 관측을 전달한다. 큐에서 뒤늦게 전달된 Heartbeat로 최근 생존 관측을 덮어쓰지 않는다. 다른 trial 출력은 유지한다.
5. 폼은 자기 Watch 구독을 취소하고 Program은 실제 공급자의 세션·재접속 작업 종료까지 기다린다. 통지는 기본 64개 유한 버퍼이며 초과 시 예외→새 구독의 초기 스냅샷으로 재동기화한다.

## 확인 방법과 실제 결과

| 구분 | 입력·예상 | 실제·판정 | 근거 |
| --- | --- | --- | --- |
| 최종 전체 빌드 | 솔루션과 새 Infrastructure 포함 빌드 | 경고0·오류0·종료0 | [빌드 요약 발췌](build-final.log) |
| 통신 자동 시험 | 새 세션 관측 전달·기존 접속/송수신/재접속/콘솔 회귀 | 827/827 통과 | [TRX](communication-final.trx) |
| GUI/공급자 자동 시험 | 수신 전 빈 위치, 실제 모드 표시, 재접속 전후 마지막값/새값, 불량 거부, 생존 중단/복구, 초기 실패·재시도 종료, 버퍼 초과 재동기화, 구독별 취소·즉시 종료, 기존 Mock/격자/두AMR 화면 회귀 | 81/81 통과(신규16 포함) | [TRX](gui-final.trx) |
| Core 회귀 | 기존 업무 도메인·계약 시험 | 9/9 통과 | [TRX](core-final.trx) |
| 실제 MQTT·네이티브 GUI | 독립 Simulator 프로세스→실제 Mosquitto→공통 모듈→실제 공급자→Windows Forms | LIVE-01~10 모두 통과 | [판정](live-final/live-results.json) · [통신 관측](live-final/mqtt-events.jsonl) |
| Simulator 실제 실행 | 기존 config/simulator.single.json, Telemetry 200ms·Heartbeat 1000ms. 두 차례 모두 최종(1,2)·속도0·IDLE·종료0 | GUI 표시와 일치 | [처음 실행](live-final/simulator-before.log) · [재접속 후 새 실행](live-final/simulator-after.log) |

자동 시험 917건은 **통신827+GUI81+Core9**다. 실제 MQTT·GUI 10항목은 별도다. 자동 대역 시험 결과를 실제 MQTT 시험으로 합산하지 않는다. Simulator/Map 시험 전체는 이번에 재실행하지 않았으며 기존 무변경 근거를 유지한다.

실제 시험 순서: 서버 연결·위치 없음 → Simulator 이동/도착 → Simulator 정상 종료 뒤 AMR 응답 없음(Broker 연결 유지) → 불량 Telemetry 거부 → 서버 중단/마지막값 → 서버 시작/자동 재접속·새 Telemetry 대기 → Simulator 재실행/수신 복구 → 재접속 대기 중 GUI 종료. 실제 재접속 시험은 1초·추가20회 설정이며 운영 기본 정책이 아니다.

## 대표 화면과 재현

| 확인할 장면 | 실제 GUI 렌더링 |
| --- | --- |
| 실제 이동 데이터 수신 | [이동](live-final/01-moving.png) |
| 서버 단절·마지막 수신값 유지 | [단절](live-final/02-disconnected.png) |
| 재접속 완료·새 위치 대기 | [재접속](live-final/03-reconnected-awaiting-data.png) |
| 같은 GUI에서 새 실행 데이터 수신 | [수신 복구](live-final/04-received-after-reconnect.png) |

PNG는 실제 폼을 표시한 뒤 `DrawToBitmap`으로 저장한 렌더링이며 이미지 목업이 아니다. 지도·목록·상세·연결 상태를 실제 컨트롤 값과 함께 검사했다. [직접 실행/격리 자동 시험 안내](../../../tests/manual/GUI-MQTT/README.md)를 따른다.

## 설계 선택과 검토 질문

- 선택: 이미 예정된 Infrastructure에서 Communication·Core를 연결하고 GUI는 기존 IAmrDataProvider를 사용한다. 기존 지도 서버를 반드시 실행하지 않아도 최신 GUI에서 통신을 확인할 수 있고, 기존 격자 모드도 유지된다.
- 대안: 기존 HTTP 지도 서버 경로에 재접속 기능을 연결할 수도 있다. 두 AMR 미션·도메인 화면을 재사용할 장점이 있지만 지도 시연 서버의 실행과 수명에도 의존한다. 이번에 이 대안을 구현·비교 시험한 것은 아니다.
- 규모: 한 대·작은 전체 스냅샷·구독별 64개 버퍼로 시작한다. 높은 빈도/여러 장비에서는 전체 사본 비용과 UI 처리량을 측정한 뒤 묶음 갱신/대상별 통지를 검토한다. 이 스트림을 손실 없는 명령/알람 이력 저장소로 사용하지 않는다.
- 질문: 왜 새 통신 구현을 추가하지 않았나? → trial의 세션을 재사용하며 GUI 공급자는 데이터 변환·구독만 담당한다. 연결/재시도 소유자는 기존 세션 하나다.
- 질문: 재접속되면 AMR 위치도 최신인가? → 새 Telemetry를 받기 전까지 마지막값이다. Broker와 생존 상태도 각각 표시한다(LIVE-06~09).
- 질문: 두 AMR·실제 명령까지 가능한가? → 새 직접 MQTT 모드는 한 대다. 기존 두 AMR 격자 모드는 별도 유지한다. 여러 장비/미션 정책이 필요하면 해당 Core 연결과 세션 대상 확장을 별도 검토한다. 실제 명령 수행·최종 ACK는 #51 범위다.

## 남은 작업·이전 실행

- 실제 MOVE/STOP/RESET 수행·결과 추적(#51), 장애 시나리오(#50), 알람 업무·DB 이력, 과제5 전체 검증은 후속이다. 단일 Simulator는 도착 후 종료하므로 복구 후 새 실행으로 데이터 재개를 확인했다. Simulator 자체의 자동 재접속 완료를 주장하지 않는다.
- 신규 실제 모드는 한 대만 지원한다. 장시간·대량·다중 장비의 새 모드 시험은 미실행이다.
- `gui-first.trx`(신규16)·`gui-regression.trx`(81)와 이 폴더 루트의 실제10항목·PNG/로그는 세션 관측 보완 전 실행이다. **최종 판정은 gui-final/communication-final/core-final과 live-final**을 따른다. 이전 성공 횟수를 추가 합산하지 않는다.
- 초기 샌드박스 빌드는 종료1/진단0으로 실패했고, 권한 환경을 바꾼 첫 빌드에서 로컬 변수 이름 중복 CS0136을 확인·수정했다. 이후 최종 빌드/시험은 위 근거로 통과했다. 실패를 통과로 바꾸어 기록하지 않는다.
- 이번 단계는 로컬 구현·시험이다. 커밋·푸시·PR·병합·Issue 종료는 진행하지 않았다. 기존 사용자 미추적 COM-01 자료35개는 유지했다.
- 원본 과제3 계획·기존 11/01 일일업무에 결과를 연결했다. HTML 보기본 생성은 공식 render-plans 도구가 기존 **과제1의 절·하위 제목 양식 불일치**에서 중단해 갱신하지 못했다. 과제3 헤더의 기존 병합 충돌도 별도 정리 항목으로 유지한다. 제품 시험 결과와 문서 생성 실패를 구분한다.
