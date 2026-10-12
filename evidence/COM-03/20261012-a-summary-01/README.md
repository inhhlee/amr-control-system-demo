# COM-03 총괄 #4 — Broker 접속과 정상 종료 통합 검증

- 기록 ID: `20261012-a-summary-01`, 확인일: 2026-10-12, 확인자: Codex.
- 관련 Issue: [#4](https://github.com/inhhlee/amr-control-system-demo/issues/4), 구현 [#8](https://github.com/inhhlee/amr-control-system-demo/issues/8).
- 판정: **원래 완료조건 7개에 충족 근거 있음. 정상 접속·종료 범위이며 COM-03 전체·과제1 전체 완료가 아니다.**
- 구분: 기존 실제 시험의 통합 대조와 병합 코드 유효성 검사. **2026-10-12 제품 재시험 결과가 아니다.**

## 1. 목표와 적용 기준

A1 Broker 실행 준비, A2 접속·해제 중간 검증, A3 유지·Ctrl+C 정상 종료·재실행·자동시험을 #4의 원래 7개 조건에 연결한다. 총괄용 구현 코드를 중복 생성하지 않고 결과·코드 버전·환경·입력·예상·실제·미검증을 한 PR에서 검토할 수 있게 한다.

문서 원본은 C:\process-plan이다. 기능명세 v0.7 COM-03, 요구사항정의 v0.5 R-04, 공통규칙 v0.4 C-04·C-09, 공통데이터명세 v0.4, 결정사항 v0.5 D-08·D-09를 적용한다. 시험 당시 과제계획 v0.18·COM03-A v0.8·통합 #8 v0.5와 현재 기록용 과제계획 v0.20·COM03-A v0.9·통합 #8 v0.6을 구분한다. 기록 갱신은 시험 기준 변경이나 재시험이 아니다. 원본 공유 URL은 확인하지 않았으므로 문서명·버전·ID로 식별한다.

## 2. 코드·환경·선행 반영

| 항목 | 실제 기준과 확인 |
| --- | --- |
| 작업 위치 | C:\amr-control-system-demo |
| 검증할 병합 코드 | `c34f380728d914aed9d0fe5f3427d698493d2ab6`, PR #13 develop 병합 |
| 구현 커밋 | `538c2372bc4c32bbe2de789affea328af192251f` |
| A1 반영 | PR #12, develop 병합 `b91fc8e22ef56b208b3c2edeca99f5e2807474ef` |
| 직접 원격 조회 | [remote-snapshot.json](remote-snapshot.json): #12·#13 merged·base·SHA. #4 open |
| 시험 당시 환경 | Windows 10.0.26200 x64, SDK 10.0.401 / 런타임 10.0.12, MQTTnet 5.2.0.1603, xUnit 2.9.3 |
| 실제 MQTT 환경 | A2·A3 당시 Mosquitto 2.1.2, amr-demo-mosquitto-1, 127.0.0.1:1884 → 컨테이너 1883 |
| 설정 | MQTT 5, Clean Start, Session Expiry 0, KeepAlive 5초, 접속·해제 제한 3초, 실행별 GUID ID |
| 기존 변경 | CONTRIBUTING.md 미커밋 변경을 검사/PR 커밋에서 제외하고 이번 검사 전후 SHA-256 대조 |

A1의 시험 뒤 Docker 설정이 바뀐 이력을 보존한다. A1은 당시 기동·중단·재기동 근거다. A2가 실제 마운트·설정·포트·MQTT를 확인했고 A3가 같은 설정·Broker의 최종 수명주기를 검증했다. 이번에는 서비스를 시작·중단하거나 현재 Broker를 조회하지 않았다.

[최종 소스 버전](../20261011-a3-shutdown-03/source-version.json)의 ZIP·내부 12개 파일 해시 및 병합 코드 내용을 검사한다. LF/CRLF 변환은 바이트 해시와 내용 일치를 구분한다. A2 중간 Program.cs는 [A2 소스 ZIP](../20261011-a2-connect-02/a2-source.zip)에 보존하며, 최종 콘솔과의 차이는 [A2→A3 비교](../20261011-a3-shutdown-03/a2-comparison.json)에 있다.

## 3. 원래 완료조건 7개와 판정

- [x] 준비 결과가 반영된 기준 브랜치·문서 버전·기존 변경과 실제 대상 경로를 확인한다.
- [x] 프로젝트 전용 Broker가 실제로 기동되고 호스트에서 MQTT 접속 승인을 받은 뒤에만 Connected가 표시된다.
- [x] 적용 주소·설정·고유 Client ID가 기록되어 같은 실행의 콘솔·Broker 접속 로그를 대조할 수 있다.
- [x] 연결된 콘솔에서 Ctrl+C 후 연결·자원이 정리되고 미처리 예외 없이 종료 코드 0으로 끝난다.
- [x] 콘솔을 다시 실행하면 새 Client ID로 정상 연결하며 이전 실행의 연결·자원이 남지 않는다.
- [x] 정상 수명주기 자동 시험과 실제 Broker 시험의 범위·결과를 구분하고 의존성 복원·빌드·관련 시험이 성공한다.
- [x] README의 실제 명령·설정과 실행 ID·코드 버전·콘솔/Broker 로그·판정을 Issue·PR에 연결한다.

| 원래 완료조건 | 판정·직접 대조한 근거 |
| --- | --- |
| 1. 기준 브랜치·문서·기존 변경·실제 경로 | 충족. 위 현재 Git·원본 버전 확인, [A1 기준](https://github.com/inhhlee/amr-control-system-demo/blob/c34f380728d914aed9d0fe5f3427d698493d2ab6/evidence/COM-03/20261008-a1-broker-01/README.md), [A2 기준](https://github.com/inhhlee/amr-control-system-demo/blob/c34f380728d914aed9d0fe5f3427d698493d2ab6/evidence/COM-03/20261011-a2-connect-02/README.md), 최종 소스 버전. |
| 2. 전용 Broker 실제 기동·MQTT 승인 뒤 Connected | 충족. A1 당시 기동 근거와 A2·A3 당시 현재 구성·실제 접속을 구분. [승인 확인 코드](https://github.com/inhhlee/amr-control-system-demo/blob/c34f380728d914aed9d0fe5f3427d698493d2ab6/src/Amr.Communication/Mqtt/MqttConnection.cs), [최종 콘솔](https://github.com/inhhlee/amr-control-system-demo/blob/c34f380728d914aed9d0fe5f3427d698493d2ab6/src/Amr.Communication.Console/Program.cs), [정상 출력](https://github.com/inhhlee/amr-control-system-demo/blob/c34f380728d914aed9d0fe5f3427d698493d2ab6/evidence/COM-03/20261011-a3-shutdown-03/native-1.stdout.log). |
| 3. 적용 설정·고유 ID·같은 실행 양쪽 로그 | 충족. [두 실행 결과](https://github.com/inhhlee/amr-control-system-demo/blob/c34f380728d914aed9d0fe5f3427d698493d2ab6/evidence/COM-03/20261011-a3-shutdown-03/native-console-results.json), [Broker 로그](https://github.com/inhhlee/amr-control-system-demo/blob/c34f380728d914aed9d0fe5f3427d698493d2ab6/evidence/COM-03/20261011-a3-shutdown-03/broker.log), 각 stdout의 127.0.0.1:1884·MQTT 5·Clean Start·Session Expiry 0·KeepAlive 5초·접속/해제 제한 3초와 ID 대조. |
| 4. Ctrl+C 해제·자원 정리·미처리 예외 없음·종료 0 | 충족. 두 독립 프로세스에 Windows CTRL_C_EVENT 전달, 각 3초 유지 뒤 Disconnected→Disposed→실제 Process.ExitCode 0. 위 두 실행 결과와 [검증 요약](https://github.com/inhhlee/amr-control-system-demo/blob/c34f380728d914aed9d0fe5f3427d698493d2ab6/evidence/COM-03/20261011-a3-shutdown-03/verification.json). |
| 5. 새 ID 재실행·이전 실행 연결/자원 종료 | 충족(관찰한 시험 범위). 서로 다른 ID의 접속·정상 disconnected 및 시험 프로세스 종료 대응. Session Expiry 0 적용. 내부 세션 DB 직접 검사로 확대하지 않음. |
| 6. 자동시험·실제 Broker 구분, 복원·빌드·관련 시험 | 충족. [복원 명령](https://github.com/inhhlee/amr-control-system-demo/blob/c34f380728d914aed9d0fe5f3427d698493d2ab6/evidence/COM-03/20261011-a3-shutdown-01/commands.jsonl) 종료 0, [수정 후 빌드·시험 명령](https://github.com/inhhlee/amr-control-system-demo/blob/c34f380728d914aed9d0fe5f3427d698493d2ab6/evidence/COM-03/20261011-a3-shutdown-02/commands.jsonl) 종료 0, 빌드 경고/오류 0, [TRX](https://github.com/inhhlee/amr-control-system-demo/blob/c34f380728d914aed9d0fe5f3427d698493d2ab6/evidence/COM-03/20261011-a3-shutdown-02/tests.trx) 실행 6·통과 6·실패/미실행 0. Fake 자동시험과 실제 Broker 두 실행은 별도 근거. |
| 7. README·실행 ID·코드 버전·로그·판정을 Issue/PR에 연결 | 충족. [실행 안내](https://github.com/inhhlee/amr-control-system-demo/blob/c34f380728d914aed9d0fe5f3427d698493d2ab6/README.md), [A3 종합 증거](https://github.com/inhhlee/amr-control-system-demo/blob/c34f380728d914aed9d0fe5f3427d698493d2ab6/evidence/COM-03/20261011-a3-shutdown-03/README.md), 이 대응표와 PR #13의 조건별 근거 연결. A2의 깨진 Compose 링크는 아래의 실제 명령 기록으로 직접 연결한다. |


## 4. 이번 근거 검사와 재현

[verify-evidence.ps1](verify-evidence.ps1)은 저장된 근거·Git blob을 검사하고 [audit-results.json](audit-results.json)에 항목별 판정·시각·코드·환경을 기록한다. dotnet·Docker·제품 EXE를 실행하지 않는다. 이번 검사 59개는 모두 통과했으며 제품 자동시험 6개와는 별도의 근거 검사다.

검사 범위는 PR #12/#13 반영 이력, 제품 소스/설정 변경 없음, A2/A3 ZIP·파일 해시, A1 명령·설정 로딩, A2 접속/해제·ID, A3 상태 순서·설정·실제 종료 코드·ID/Broker 로그 대응·이전 프로세스 종료 기록, TRX·복원/빌드 명령, 기존 CONTRIBUTING.md 보존이다. 코드의 승인 후 출력 확인은 정적 대조이며 자동시험·실제 MQTT 시험을 대체하지 않는다.

이번 실행 환경은 PowerShell 7.6.5이며 날짜 문자열을 원본 형식으로 유지한다. 출력 경로를 지정하면 이번 검사 결과도 보존할 수 있다.

```powershell
Set-Location C:\amr-control-system-demo
pwsh -NoProfile -File evidence/COM-03/20261012-a-summary-01/verify-evidence.ps1 -OutputPath "$env:TEMP\com03-summary-audit.json"
```

제품 실행 명령·실제 Ctrl+C·재실행 절차는 [루트 README](../../../README.md#복원빌드실행)를 따른다. 해당 명령은 이번 통합 대조에서 재실행하지 않았다.

## 5. 미검증·실패 이력·남은 단계

- 사용자 Git Bash의 `echo $?`: 미수집. 앱의 종료 0은 독립 프로세스의 Process.ExitCode로 확인했으므로 사용자 터미널 종료 코드까지 통과로 표시하지 않는다.
- Broker 내부 세션 DB 직접 덤프: 미실행. 각 연결의 정상 해제·프로세스 종료·Session Expiry 0을 원래 정상 수명주기 범위의 근거로 사용한다.
- [기존 리뷰 P3](https://github.com/inhhlee/amr-control-system-demo/pull/13#issuecomment-6107018261): A2 README의 `compose-config.log` 링크 대상은 없다. 실제 Compose 검사 명령·시각·종료 0은 [commands.jsonl 6행](https://github.com/inhhlee/amr-control-system-demo/blob/c34f380728d914aed9d0fe5f3427d698493d2ab6/evidence/COM-03/20261011-a2-connect-02/commands.jsonl#L6)에 있다. 이 직접 링크로 근거 접근을 확보했으며 이번 PR은 해당 링크를 실제 commands.jsonl로 보완한다. 실제 시험 실패가 아니며 이 사유만으로 Broker 재시험을 요구하지 않는다.
- 코드 README·A1/A2/A3 당시 증거·문서 원본의 “미커밋·PR 대기·병합 미수행”은 작성 시점의 안내다. 현재 원격 상태는 위 병합 결과를 따른다. 과거 시험 기록의 실패·당시 코드 상태는 보존한다.
- 상세 입력/접속 실패·시간 초과·접속 중 취소·늦은 승인은 #5, 비정상 단절·무응답·KeepAlive 감시·종료 경합은 #6이다. 메시지 송수신·자동 재접속·GUI·Core·DB도 이번 범위 밖이다. #4의 정상 접속·종료 조건 충족을 COM-03 전체 또는 과제1 전체 완료로 집계하지 않는다.

- 초기 빌드·시험 도우미 실패는 [A3 01](../20261011-a3-shutdown-01/README.md)·[02](../20261011-a3-shutdown-02/README.md)에 보존했다. 수정 후 03의 두 번 성공을 최종 근거로 사용한다.
- 이번 총괄 검증 PR의 리뷰·develop 병합은 아직 수행하지 않았다. 사용자 요청은 PR 생성까지다.
- main 반영은 미결정이며 이번 PR base는 develop이다. #4·#8은 open으로 유지하고 별도 종료 요청 단계에 남긴다.
