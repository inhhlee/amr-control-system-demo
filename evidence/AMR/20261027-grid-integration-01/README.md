# Issue #46 격자 지도 선별 이관 검증 — 2026-10-27

## 1. 목표와 적용 기준

AMR-01/02/05·GUI-02의 단일 AMR 시연 범위다. 최신 develop 위에 기존 경유점·장애물 우회·MQTT 수신 지도를 복원하고 기존 통신·Core·Mock GUI를 보존한다. [Issue #46](https://github.com/inhhlee/amr-control-system-demo/issues/46)은 open이며 로컬 검증까지 수행했다. 커밋·푸시·PR·병합은 수행하지 않았다.

- 기준 HEAD/develop: `4e44fa880cb0d1fcccb000dbc53e634fd90381b5`, 작업 브랜치 `feature/46-grid-map-integration`.
- 선별 원본: `codex/preserve-grid-map-20261023`의 `4b9f950555aff6ad23e67495b5d0ec9cb9371b3b`.
- 코드 버전은 기준 HEAD + 미커밋 이관이다. [이관 전 기준](baseline.json), [최종 파일 해시·보존 검사](final-audit.json), [선별 원본 ZIP](selected-source.zip)으로 식별한다.
- 적용 문서: 과제2 과제계획 v0.16, 기능명세 v0.13 AMR-01/02/05·GUI-02 일부, 공통데이터명세 v0.12, 검토 및 결정사항 v0.16. 문서 원본은 `C:\process-plan`에 있다. 이번 사용자 요청과 10/23 보존 시연 동작을 직접 적용했다.
- 보존 시연은 4방향 다익스트라·동적 장애물·`X=열,Y=-행,1칸=1m`이다. 승인된 후속 다익스트라/A* 비교·셀 중심 좌표·고정 비교 스냅샷과 구분하며 PF01~10 전체 통과로 집계하지 않는다.
- spec_revision은 별도 코드 추적기 미사용으로 해당 없음. verification_index는 이 README다.

## 2. 실행 환경

Windows 10.0.26200 win-x64, .NET SDK 10.0.401·Runtime 10.0.12, MQTTnet 5.2.0.1603. [실제 SDK](environment-authorized.log). 실제 Mosquitto `2.1.2-alpine` 이미지 SHA256 `38c0da4f2ef84284d47b3b3eeea1cb3bdeabe81ee10caf0cd5c5ff61ee3ea408`를 사용했다. 각 시험은 Windows PowerShell 5.1이 아닌 설치된 PowerShell 7.6.5(`pwsh`)로 실행했다.

- 실제 MQTT/API 호스트: [session.json](../20261027-grid-host-01/session.json), HTTP 50695·Broker 50693, 11:12~11:20 KST. 제품 원시 이벤트는 해당 session의 ProductEvidence와 [로그](https://github.com/inhhlee/amr-control-system-demo/blob/ae71f452f27a65ec2fee68fee60496a7a5ca3d6c/evidence/AMR/mission-live-20261027-111239-042c5cc2856049e5805d9c156545013d/grid-navigation.jsonl)에 있다.
- 실제 브라우저 호스트: [session.json](../20261027-grid-ui-host-01/session.json), HTTP 52051·Broker 62328, 11:20~11:22 KST. [원시 이벤트](https://github.com/inhhlee/amr-control-system-demo/blob/ae71f452f27a65ec2fee68fee60496a7a5ca3d6c/evidence/AMR/mission-live-20261027-112011-79a794f9146c42118e04749357d01455/grid-navigation.jsonl).
- Topic `amr/AMR_001/telemetry`, `amr/AMR_001/heartbeat`, MQTT5·QoS0·Retain=false. 실제 Broker·송신 Client·관제 수신 Client·브라우저를 연결했다. 실제 로봇은 사용하지 않았다. 자동시험의 대역과 실제 연동은 아래에서 구분한다.

## 3. 선별 이관과 설계 판단

처음 추출한 32파일 중 과거 11회 우회 계약의 `Verify-GridNavigation.ps1`은 검토 후 제외하여 최종 31파일을 복원했다. 원본 ZIP·보존 브랜치에는 남아 있다. 상세 목록은 final-audit.json의 finalImportedFiles다.

- Simulator Navigation 4파일, Map 웹 호스트·Receiver/Projection·Control·화면, map-demo 설정, Simulator/Map 자동시험, 현재 MQTT·설정 시험 도구를 복원했다.
- 기존 Telemetry 통신 수정·Core·ControlGui는 덮어쓰지 않았다. 솔루션에는 Map와 Map 시험만 추가했다.
- Start/Stop 도구의 개인 PC Docker 경로를 `Get-Command docker`로 변경했다. Map README는 현재 실행·선별 범위·보존 근거 링크로 보완했다. 이외 제품/시험 코드는 원본과 Git 정규화 blob이 같다.
- 이전 MapSession/app.js·과거 설정 파일·무관한 시험/보고 도구·290개의 과거 증거를 일괄 복사하지 않았다. 기존 원시 증거는 [보존 커밋](https://github.com/inhhlee/amr-control-system-demo/tree/4b9f950555aff6ad23e67495b5d0ec9cb9371b3b/evidence/AMR)과 [복구 검증 백업](../../GIT/20261027-preservation-backup-01/README.md)에 유지한다.

전체 브랜치 병합보다 파일 선별이 최신 MQTT 수정과 Mock GUI를 유지하는 데 적합하다. 원본의 GridSimulation이 이동·발행·송신 수명을, GridMapSession이 관제 수신·화면 상태·최종 수신 확인을 소유한다. 잠금 안에서는 상태만 갱신하고 네트워크 대기·이벤트는 잠금 밖에서 처리한다. 취소 요청 뒤 실행 Task와 연결 정리를 기다린다. 이 구분은 프로세스 내부 상태의 일관성을 위한 것이며 QoS0의 무손실·장비 간 실행 순서를 보장하지 않는다.

대안은 Core 공급자에 바로 연결하거나 전체 보존 브랜치를 병합하는 것이다. 전자는 상태·취소·좌표 계약을 새로 정해야 하고 후자는 과거 시험·도구 및 예전 구성을 섞는다. 이번 방식은 독립 웹 호스트와 수신 모델을 유지하므로 작은 시연에서는 유지보수 범위가 명확하지만 별도 상태 사본의 메모리와 두 화면의 관리 비용이 남는다. 실제 통합이나 대규모 부하의 성능 우열은 측정하지 않았다.

## 4. 입력·예상·실제 결과

| 시험 | 입력·예상 | 실제·판정 | 근거 |
| --- | --- | --- | --- |
| 빌드/자동 회귀 | 최신 기준 + 이관, 기존 시험과 격자 시험 전부 통과 | 빌드 경고0·오류0, 통신768·Simulator200·ControlGui32·Map63 = **1,063 통과**, 실패/skip0 | [실행 명령](Run-Automated.ps1), [build.log](build.log), [TRX](tests/) |
| 설정 실제 연동 | 3×8/100ms·ID A/9회 → 파일 변경 후 재시작 전 유지 → 2×3/180ms·ID B/3회, 잘못된 주기 거부 | **6항목 통과** | [verification](../20261027-grid-settings-01/verification.json), [설정 시험 산출물](../20261027-grid-settings-01/) |
| 정상·우회·순서 | 기본5×6, S(0,0)→W1(2,3)→W2(4,3)→G(4,5). 정상9회, 1회 이동 후(2,2)차단 시15회 | W2 선행 통과는 완료로 계산하지 않고 W1→W2→G 방문. 예상 일치 | [실제 MQTT 15항목](../20261027-grid-mqtt-01/verification.json), [시나리오별 상태](../20261027-grid-mqtt-01/) |
| 대기/격리/종료 | W2·통로 차단/해제, 대기 Heartbeat, 불량·다른 ID, 취소·초기화, Broker 단절·최초 접속 실패 | 이동 정지/재개, 상태 격리, 정리 및 재출발 경계 확인. 위 **15항목 모두 통과** | 같은 MQTT 실행 근거 |
| 브라우저 실제 조작 | 출발 클릭, 1회 이동 확인 뒤(2,2)클릭, 15회 도착·3/3·수신(4,5) | **통과**. 방문11/13/15, Telemetry47·Heartbeat10, IDLE·속도0, X5/Y-4, 최종 발행/수신 ts=1793067657198 | [클릭 직전](../20261027-grid-ui-01/moving-before-obstacle-ax.txt), [장애물 화면](../20261027-grid-ui-01/moving-obstacle.png), [최종 화면](../20261027-grid-ui-01/final.png), [최종 AX](../20261027-grid-ui-01/final-ax.txt), [수신 API](../20261027-grid-ui-01/final-state.json) |
| 보존/정리 | 보호 파일·원래 미추적50·stash·보존 브랜치·백업 불변, 자신의 서버/Broker만 정리 | [재검사 결과](final-audit.json). 두 호스트 exit0·포트 닫힘·소유 Broker 삭제·기존 컨테이너 보존 | [API cleanup](../20261027-grid-host-01/cleanup.json), [UI cleanup](../20261027-grid-ui-host-01/cleanup.json), [검사 스크립트](Verify-Preservation.ps1) |

자동시험·실제 시나리오·수신 건수는 서로 합산하지 않는다. 기존 ControlGui32건은 이번에 실행했으며 실제 Windows Forms 화면은 이번에 다시 조작하지 않았다. 기존 폼 실행 근거는 원본 그대로 보존한다.

## 5. 실패·환경 차이와 후속

- 최초 `.NET --info`/restore는 제한 실행 환경의 Windows Service Control Manager 접근에서 실패했다. [최초 기록](environment-dotnet.log), [restore 기록](restore.log)을 유지하고 허용된 실행으로 다시 복원·빌드·자동시험했다. 제품 실패로 집계하지 않는다.
- Git archive는 core.autocrlf에 따라 CRLF를 출력한다. [최초 raw 비교](import-manifest.json)의 차이는 [정규화 비교](normalized-import-audit.json)와 최종 감사에서 구분했다. 코드 내용은 이관 과정에서 손실되지 않았다.
- 첫 브라우저 준비에서 장애 시험 Broker를 재시작하자 포트가 50693→61721로 바뀌어 이전 서버가 접속 실패했다. [실패 상태](../20261027-grid-ui-01/first-arrival.json)와 최초 initial/obstacle 이미지·AX를 유지했다. 이후 별도 `grid-ui-host-01`을 만들어 검증했다. 이 실패를 정상 도착 근거에 포함하지 않는다. BUILDING에 장애 시험 이후 새 호스트 사용을 명시했다.
- 전체 A* 비교/PF01~10, 실제 Core/MQTT/DB 연결, 명령/ACK, 실제 로봇·다중 AMR·장시간/부하 시험은 미실행이다. 과제 전체 완료가 아니다.
- 문서 저장소의 전체 HTML 생성은 기존 과제3 개요 항목이 빈 양식과 달라 쓰기 전에 중단됐다. 원본 렌더러를 수정하지 않고 같은 양식 검사를 빈 양식/과제2에 적용해 과제2 보기본만 생성했다. 다른 보기본 해시는 유지됐다. [문서 검사 결과](document-checks.json). 이번 과제2 원본·전체일정·일일업무만 갱신했으며 이 검사는 제품 시험과 별개다.

## 6. 리뷰 질문과 현재 단계

- **MQTT 단절 수정이 옛 코드로 되돌아가지 않았나?** 최신 Telemetry·통신·Core·GUI 보호 파일 해시, 이번 Simulator200/통신768/GUI32 회귀로 확인한다. 격자 송신 접속 중 단절 시험도 포함한다.
- **발행 성공을 도착으로 표시하지 않나?** 최종 시각·좌표·IDLE·속도0의 관제 수신 확인과 연결 정리 뒤 완료한다. 실제 UI/JSON/이벤트가 일치한다. QoS0 재전송·실제 로봇 제동 확인은 아니다.
- **왜 A*와 셀 중심 좌표를 추가하지 않았나?** 이번 요청은 기존 시연 보존·선별 이관이다. 향후 비교/실제 Core 통합에서 명세의 좌표·알고리즘 계약과 이력 호환을 별도 설계하고 검증해야 한다.

로컬 선별 이관·검증은 완료했다. 검토 후 사용자가 요청하는 단계에서만 커밋·푸시·PR·병합을 진행한다. Issue는 열어 두고 미게시 근거는 저장소 내 경로로만 참조한다.
