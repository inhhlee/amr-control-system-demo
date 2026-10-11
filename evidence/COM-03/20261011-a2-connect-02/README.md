# COM-03 A2 접속·명시적 해제 — 20261011-a2-connect-02

## 1. 목표와 적용 기준

- 실행자: Codex. [Issue #8](https://github.com/inhhlee/amr-control-system-demo/issues/8)의 A2 중간 단계만 구현·실제 검증했다. A3 연결 유지·Ctrl+C·시험 대역은 아직 시작하지 않았다.
- 원본 위치: `C:\process-plan`. 기능명세 v0.7 COM-03, 요구사항정의 v0.5 R-04, 공통규칙 v0.4 C-04·C-09, 결정사항 v0.5 D-08·D-09, COM03-A v0.8, A2·A3 통합 초안 v0.5.
- 과제계획은 실제 v0.18을 읽었다. Issue의 v0.17 인용과 차이가 있으며, v0.18의 #8 통합·순서와 현재 Issue의 완료조건을 적용했다. 별도 기능 추적기는 이 실습 저장소에 없다.
- 선행: PR #12의 develop 병합 및 로컬 이력 포함 확인. #7 자체는 open이며 임의 종료하지 않았다. #9는 #8로 통합된 안내를 따르고 별도 구현하지 않는다.
- 현재 로컬·원격에 문서가 말한 기존 #8 브랜치가 없어, clean develop을 fetch·fast-forward 확인한 뒤 `git flow feature start 8-mqtt-connect`로 생성했다.
- 현재 PC는 Git Flow AVH 1.12.3이다. 문서의 Git Flow Next 2.0.0과 다르다. Git Bash에서 설치된 도구의 `init -d`로 로컬 main/develop·feature/ 설정 후 feature start를 사용했다. 전역 설정·공유 협업 문서는 수정하지 않았다. finish·커밋·푸시·PR·병합은 미수행.

## 2. 실행 환경과 코드

| 항목 | 실제 값·증거 |
| --- | --- |
| 실행 | 2026-10-11 14:29~14:31 KST. [commands.jsonl](commands.jsonl)의 UTC와 +9시간 관계 |
| 코드 | `855d76d28a8fd096f2328aefe306c3161c13eb26` + 미커밋 A2 소스, `feature/8-mqtt-connect` |
| 소스 보존 | [a2-source.zip](a2-source.zip)에 시험한 3개 C# 파일·프로젝트·SDK·Docker 설정을 상대 경로로 보존. [source-version.json](source-version.json)에 파일별·ZIP SHA-256 기록 |
| OS / .NET | Windows 10.0.26200 x64, SDK 10.0.401, 런타임 10.0.12. [dotnet-info.log](dotnet-info.log) |
| MQTTnet | 프로젝트 지정 5.2.0.1603, 변경 없음 |
| Docker | Desktop 4.91.0, Engine 29.8.0, Compose v5.5.1. [docker-version.log](docker-version.log), [compose-version.log](compose-version.log) |
| Broker | 기존 실행 중 `amr-demo-mosquitto-1`, Mosquitto 2.1.2, `127.0.0.1:1884` → 컨테이너 1883. [실제 마운트·포트](broker-inspect.json) |
| 개발용 설정 | MQTT 5, Clean Start, Session Expiry 0, KeepAlive 5초, 접속 제한 3초, 해제 제한 3초, 실행마다 `amr-demo-<GUID>` |
| 인자 | `--host 127.0.0.1 --port 1884 --connect-timeout-seconds 3` |

Broker는 시작·중단·재기동하지 않았다. 현재 설정·포트·마운트와 컨테이너 내 설정 파일을 조회했다. [컨테이너 설정 내용](broker-loaded-file.log)은 현재 로컬 설정과 동일하다. MQTT 메시지 구독·발행·Heartbeat·자동 재접속·GUI·Core·DB는 미실행이다.

## 3. 입력·실제 결과·판정

| 시험 | 입력·예상 | 실제 결과 | 판정·근거 |
| --- | --- | --- | --- |
| 복원 | 기존 솔루션 restore, 종료 0 | 세 프로젝트 복원, 종료 0 | 통과 — [restore.log](restore.log). 최초 권한 실패는 [이전 실행](../20261011-a2-connect-01/README.md)에 보존 |
| 빌드 | `dotnet build AmrControlSystem.slnx --no-restore --verbosity minimal` | 경고 0·오류 0·종료 0 | 통과 — [build.log](build.log) |
| 현재 Broker 구성 | Compose 검사·현재 상태·설정·마운트·포트 조회 | 모델 검사 0, running, 루프백 1884, 로컬 프로젝트 경로 | 조회·모델 검사 통과 — [검사](compose-config.log), [시작 전](broker-status-before.log), [종료 후](broker-status-after.log) |
| A2 첫 프로세스 | 접속 승인 → Connected → 명시적 해제 → 정리 → 종료 0 | 순서 일치, 종료 0 | 통과 — [console-01.log](console-01.log) |
| A2 새 프로세스 | 다른 Client ID, 같은 정상 순서·종료 0 | 첫 실행과 다른 GUID, 순서 일치, 종료 0 | 통과 — [console-02.log](console-02.log) |
| 양쪽 로그 대응 | 각 Client ID의 MQTT 5 접속·정상 해제 | 두 ID 모두 Broker 접속 및 disconnected 로그 확인 | 통과 — [broker-logs.log](broker-logs.log) |
| 최소 접속 실패 | TCP listener가 없는 로컬 포트 1, Connected 없음·정리·종료 1 | MqttCommunicationException, Disposed, 종료 1, Connected 없음 | 통과 — [console-refused.log](console-refused.log). listener 부재 확인은 [최초 실행 스크립트](verify-initial.ps1)에 포함 |
| xUnit 수명주기 시험 | A3에서 대역으로 검증 예정 | 이번 단계에서 작성·실행하지 않음 | 미검증. 빌드한 빈 시험 프로젝트를 기능 시험 통과로 집계하지 않음 |

정상 실행의 ID는 `amr-demo-28815b42212044f688868e5409371125`, `amr-demo-061352095bae49948bf945692748196b`다. 종료 코드와 stdout/stderr, 설정·시각을 함께 보존했다.

## 4. 확인 방법과 검사 도구 수정

```powershell
Set-Location C:\amr-control-system-demo
dotnet restore AmrControlSystem.slnx
dotnet build AmrControlSystem.slnx --no-restore
dotnet run --project src/Amr.Communication.Console --no-build -- --host 127.0.0.1 --port 1884 --connect-timeout-seconds 3
$LASTEXITCODE
```

위 run을 새 프로세스로 다시 실행하여 새 ID와 정상 종료를 대조한다. 이후 A3로 코드가 바뀌면 위 명령의 동작도 달라지므로 A2를 재현할 때는 ZIP을 별도 시험 폴더에 풀어 사용한다. 현재 작업 폴더를 덮어쓰지 않는다. 새 실제 시험에는 새 실행 폴더를 사용한다.

- [verify-initial.ps1](verify-initial.ps1)은 실제 실행한 초기 도구다. 콘솔 세 번을 모두 기록한 후, Broker 해제 로그에서 `[주소:포트]`를 허용하지 않은 정규식 때문에 자체 판정이 실패했다. [오류 설명](verification-initial-error.txt)에 원인·영향을 보존한다.
- [check-logs.ps1](check-logs.ps1)은 같은 원본 로그·기록된 종료 코드만 다시 검사한다. 정상 접속·해제 ID·순서 조건을 유지하며 주소 표기를 허용했다. 실제 MQTT 시험을 재실행하지 않았다.
- [verification.json](verification.json)은 원본 로그 재대조 통과다. [verify.ps1](verify.ps1)은 재현 도구의 수정본이며, 이 파일 전체를 다시 실행한 결과로 표시하지 않는다.

## 5. 완료 범위와 다음 단계

- A2: 실제 접속 승인·명시적 해제·정리·종료 0·새 프로세스·고유 ID·Broker 로그 대응을 확인했다. 승인 전 성공 표시를 막는 코드가 있으며, 최소 실제 접속 실패에서 성공 표시가 없었다.
- 실제 MQTT 거부 승인(CONNACK 실패) 대역, 승인 완료 전 상태, 반복 정리의 자동 시험은 A3에서 검증한다. 포트 거부 결과를 모든 승인 실패 검증으로 확대하지 않는다.
- A3: Ctrl+C까지 연결 유지, 취소 요청과 해제 완료 대기, 새 실행, xUnit 대역 시험, 최종 코드에 대한 A1·A2 근거 유효성 대조가 남았다. 사용자가 A2 코드 설명을 요청하여 학습 확인을 이어간다.
- 총괄 #4의 기준·환경·접속·ID·일부 증거 조건에는 위 A2 결과를 연결할 수 있다. Ctrl+C·최종 재실행·자동 시험·최종 README/PR 연결 조건은 아직 미충족이다. #8·#4·COM-03 전체 완료로 표시하지 않는다.
- A1 이후 `855d76d`에서 Docker 설정이 바뀌었으므로 과거 A1 결과를 현재 설정 전체의 통과로 확대하지 않았다. 현재 A2에서는 설정 파일·실제 접속을 확인했으며 새 중단·재기동 시험은 하지 않았다.
- 커밋·푸시·PR·Issue 종료·업무 문서 갱신은 아직 미수행이다. 상세 실패·취소는 #5, 비정상 끊김·경합은 #6의 후속 범위다.
