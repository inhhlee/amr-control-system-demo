# COM-03 A1 Broker 기동·관리 검증결과

- 실행 ID: `20261008-a1-broker-01`
- 실행자: Codex, 사용자 요청에 따른 로컬 실행
- 관련 Issue: [#7 · 전용 Broker 기동·관리 확인](https://github.com/inhhlee/amr-control-system-demo/issues/7). 총괄 [#4](https://github.com/inhhlee/amr-control-system-demo/issues/4).
- 판정: **시험 당시 설정의 A1 로컬 실행 검증 통과. 이후 별도 설정 변경은 미검증이며 커밋·푸시·PR 게시 연결도 미진행.** MQTT 접속 성공이나 총괄 완료를 뜻하지 않는다.
- 브라우저 보기: 같은 폴더의 [report.html](report.html)을 로컬 브라우저로 연다. 아래 결과와 원본 로그를 함께 담은 정적 보기본이며 실시간 상태 화면이 아니다.

## 1. 목표와 적용 기준

준비된 Compose 정의로 프로젝트 Broker만 기동·중단·재기동하고, 상태·설정 로딩·포트·로그를 대조한다. 기존 파일은 루트 `README.md`, `docker/compose.yaml`, `docker/mosquitto/config/mosquitto.conf`다. 이번 생성 경로는 이 실행 증거 폴더다. Docker 설정은 기존 동작이 조건을 충족하여 변경하지 않았다.

선행 #2·PR #3의 main 병합 커밋 `963a1fa8d48f432aaa87f07f15a61db57e22db88`이 현재 HEAD의 조상임을 확인했다([33-base-ancestor.log](33-base-ancestor.log), 종료 0은 [commands.jsonl](commands.jsonl)). GitHub 조회에서도 PR #3의 `merged=true`와 같은 병합 SHA를 확인했다.

| 기준 | 실제 읽은 버전·항목 | 적용·차이 |
| --- | --- | --- |
| 기능명세 | v0.7 초안 COM-03 | Broker 연결 상태 감시 중 이번 A1 실행 환경 범위 |
| 요구사항정의 | v0.5 초안 R-04 | 통신 계층의 접속 관리 |
| 공통규칙 | v0.4 초안 C-04·C-09 | 운행·Broker·AMR Heartbeat 상태를 구분 |
| 공통데이터명세 / 검토 및 결정사항 | v0.4 / v0.5, D-08·D-09 | 이번 A1에는 메시지 계약·재접속 정책 적용 없음 |
| 과제1 과제계획 | 현재 v0.16 초안, 새 저장소 실습·A1 #7 | Issue 참조 v0.15와 차이: A1~A3 실제 게시·총괄 연결 반영 |
| COM03-A / COM03-A1 원본 | 현재 v0.7 / v0.4 | Issue 참조 COM03-A v0.6과 차이: #4 총괄, #7~9 별도 작업으로 연결 |
| 개발 진행 절차 / 개발환경 | v0.6 / v0.7 | 실행값은 과거 환경표 대신 현재 PC에서 조회 |
| 코드 저장소 규칙 | AGENTS.md, CONTRIBUTING.md v0.3, PR 양식 | 기존 feature 브랜치 유지, PR base develop, 이번에는 커밋 금지 |

원본 경로는 `C:\process-plan\관제시스템 과제 진행\명세\{기능명세,요구사항정의,공통규칙,공통데이터명세,검토 및 결정사항}.md`, 같은 프로젝트의 `과제1\과제계획.md`, `과제1\이슈\02_COM-03_접속과_정상종료.md`, `05_COM-03_Broker_기동확인.md`, `개발환경.md` 및 `C:\process-plan\기준\개발 진행 절차.md`다. 정확한 파일별 절대 경로·버전 행·SHA-256은 [31-document-sources.json](31-document-sources.json)에 있다. 공유 URL은 확인되지 않아 로컬 경로를 웹 링크로 사용하지 않는다.

과제계획 v0.15·COM03-A v0.6의 과거 본문을 별도로 읽은 것으로 표시하지 않는다. 이번 적용 범위는 사용자가 전달한 본문과 실제 #7이며, 현재 원본의 분리·연결 변경은 범위를 넓히지 않는다. 문서 원본 읽기는 가능했지만 `C:\process-plan`은 이번 코드 프로젝트의 등록된 쓰기 루트가 아니다. 기존 문서 저장소 변경은 별도로 보존한다.

학습 답변에서 호스트→컨테이너 연결 이해를 확인하고 프로젝트·서비스 구분 및 Mosquitto의 Broker 역할을 설명한 뒤 실행했다. 학습 이해는 제품 검증 근거에 포함하지 않는다. 별도 제품 추적기 spec_revision·verification_index는 이 저장소에 없어 해당 없음이다.

## 2. 실행 환경과 코드

| 항목 | 실제 값·근거 |
| --- | --- |
| 실행 구간 | 2026-10-08 16:11:17~16:13:40 KST, 뒤에 문서·증거 대조 수행. 명령별 시각은 commands.jsonl |
| 코드 위치 / 브랜치 | `C:\amr-control-system-demo` / `feature/7-broker-check` |
| 기준 코드 | `ebaf54641040b97f81cdda0142ab5c7e498fa454` |
| 기준 브랜치 | 조회한 원격 develop도 같은 SHA. 기존 작업 브랜치 재사용, 새 브랜치 생성 없음 ([32-git-baseline.log](32-git-baseline.log)) |
| 기존 변경 / 원격 PR | 시작 시 Git 상태 빈 목록. 같은 head의 PR 검색 결과 없음. 원격 작업 브랜치도 조회되지 않음 |
| 이번 미커밋 변경 | README와 이 실행 증거. 이 작업에서 C#·프로젝트·Docker 설정은 수정하지 않음. 시험 뒤 생긴 다른 작업의 mosquitto.conf 변경은 보존·미검증 |
| OS / PowerShell | Windows 10.0.26200 x64 / 7.6.5 |
| Docker | Desktop 4.90.0 (238679), Client·Engine 29.7.2, Compose v5.5.1, context desktop-linux |
| Docker CLI | `C:\Users\Admin\AppData\Local\Programs\DockerDesktop\resources\bin\docker.exe` (현재 PATH에서도 조회됨) |
| 엔진 주소 | `npipe:////./pipe/dockerDesktopLinuxEngine` |
| 이미지 | 기존 `eclipse-mosquitto:2.1.2-alpine`, Linux amd64. 다운로드·설치 없음. digest는 [08-image.json](08-image.json) |
| 프로젝트 / 서비스 / 컨테이너 | `amr-demo` / `mosquitto` / `amr-demo-mosquitto-1`, ID `039102e4775ccc99cb74d4e08e9c50d689289a6aca5ae39788048b69d7d88a1c` |
| 관리 정의 | `C:\amr-control-system-demo\docker\compose.yaml`, Compose working_dir는 같은 `docker` 폴더 |
| 후속 클라이언트 접속 주소 | 이 Windows PC의 `127.0.0.1:1884` → 컨테이너 `1883/tcp` |
| 마운트 | config 파일 read-only, data·log 폴더 read-write. 모두 이 저장소의 docker/mosquitto 아래 |
| Broker 정책 | 로컬 개발용 익명 접속, persistence true, stdout 및 파일 로그, restart no |
| 기존 다른 Broker | `amr-mosquitto`, 프로젝트 docker, 관리 정의 `C:\Monit\docker\docker-compose.yml`. 작업 전후 exited(0) 유지 |
| SDK / MQTTnet / Topic·QoS·Retain | .NET 실행·MQTT 클라이언트 시험에 해당하지 않아 이번에 측정·사용하지 않음 |

환경·최초 Git 상태·설정 SHA-256은 [environment.json](environment.json), 엔진·이미지·해석된 모델은 [01-engine-before.log](01-engine-before.log)·[04-compose-resolved.json](04-compose-resolved.json)·[08-image.json](08-image.json), 실제 적용값은 [15-inspect-started.json](15-inspect-started.json)과 대조한다. 초기 모델의 예상값만으로 실제 적용을 판정하지 않았다.

## 3. 입력·예상·실제 결과

모든 Compose 명령은 코드 루트에서 같은 `compose -f docker/compose.yaml` 정의로 실행했다. 정확한 실행 파일·인자 배열·시작/종료 시각·종료 코드를 [commands.jsonl](commands.jsonl)에 기록했다.

| 시험 | 입력·예상 | 실제 결과·판정 | 근거 |
| --- | --- | --- | --- |
| A1-01 모델 검사 | `config --quiet`, 종료 0 | 종료 0, 출력 없음. **통과(모델 검사만)** | [03-compose-config.log](03-compose-config.log), commands.jsonl |
| A1-02 기동·관리 확인 | 기존 이미지·빈 1884 포트에서 `up -d mosquitto` → ps·logs·inspect, running·설정 로딩·루프백 포트·정상 마운트 예상 | 기동 종료 0. 16:12:44 KST 기동, 2.1.2 설정 로딩·running. Windows 127.0.0.1:1884 listener 확인. **통과** | [12-up.log](12-up.log), [13-ps-started.jsonl](13-ps-started.jsonl), [14-logs-started.log](14-logs-started.log), [15-inspect-started.json](15-inspect-started.json), [16-listeners-started.json](16-listeners-started.json) |
| A1-03 중단·재기동 | 같은 서비스 `stop mosquitto`, exited(0)·정상 종료 예상. 다시 `up -d mosquitto`, running·재로딩·포트 공개 예상 | stop 종료 0, 컨테이너도 exited(0). 16:13:12 terminating·DB 저장. 16:13:37 같은 ID 재기동, 설정 재로딩·running·1884 공개. **통과** | [17-stop.log](17-stop.log), [18-ps-stopped.jsonl](18-ps-stopped.jsonl), [19-inspect-stopped.json](19-inspect-stopped.json), [20-logs-stopped.log](20-logs-stopped.log), [22-up-again.log](22-up-again.log), [24-logs-restarted.log](24-logs-restarted.log), [25-inspect-restarted.json](25-inspect-restarted.json), [27-listeners-restarted.json](27-listeners-restarted.json) |
| A1-04 문서·증거 대조 | README의 경로·주소·명령이 실제 기록과 같고 필요한 파일 추적 가능 | 재현 명령과 이번 증거 연결. 실행 구간의 전후 Docker 설정 SHA-256 동일. 이후 별도 변경은 미검증. 로컬 문서 검사 결과는 repository-checks.json. **문서 검증, MQTT 시험 아님** | [루트 README](../../../README.md), [29-comparison.json](29-comparison.json), [repository-checks.json](repository-checks.json) |
| 보존 확인 | 기존 다른 컨테이너를 시작·중단·삭제·초기화하지 않음 | 기존 4개 ID·State·이미지·재시작 횟수·마운트 내용 동일. **통과** | [11-others-before.jsonl](11-others-before.jsonl), [26-others-after.jsonl](26-others-after.jsonl), [30-preservation-check.json](30-preservation-check.json) |

추가 기록: [28-mosquitto-file.log](28-mosquitto-file.log)는 실제 마운트된 `docker/mosquitto/log/mosquitto.log`의 복사본이다. 중단 시 `docker/mosquitto/data/mosquitto.db`가 생성됐으며 데이터를 삭제·초기화하지 않았다. Git 제외 대상인 실행 데이터·로그와 검토용 evidence 로그를 구분한다.

## 4. 완료조건과 브라우저 확인

로컬 `report.html`을 열고 아래 완료조건 표를 확인한다. 브라우저에서 Ctrl+O로 이 파일을 선택할 수 있다. 하단 원본 증거에서 해당 파일명을 펼치면 외부 서비스 없이 확인할 수 있다. GitHub에는 아직 미게시이므로 현재 저장소 웹페이지에 이번 변경이 표시되지는 않는다. 보기본 재생성은 PowerShell 7에서 같은 폴더의 `render-report.ps1`을 실행한다.

| #7 완료조건 | 상태 | 브라우저에서 확인할 내용 |
| --- | --- | --- |
| 1. 기준 브랜치·문서·기존 변경·관리 위치·구성 기록 | 충족 | 1~2절, environment.json, 31-document-sources.json, 04-compose-resolved.json, 15-inspect-started.json |
| 2. Compose 검사 종료 0 | 충족 | commands.jsonl의 03-compose-config.log 항목 exitCode=0. 빈 로그만으로 판정하지 않음 |
| 3. 실제 기동·설정 로딩·포트 공개 | 시험본 충족 / 이후 설정 변경 미검증 | 13의 running·1884, 14의 Config loaded 및 running, 15의 읽기 전용 설정 마운트, 16의 호스트 listener |
| 4. 해당 프로젝트만 중단·재기동 | 시험본 충족 / 이후 설정 변경 미검증 | 19의 exited·ExitCode 0, 24의 종료→재로딩, 25의 같은 ID·새 StartedAt·Running true, 30의 다른 컨테이너 전 항목 true |
| 5. README·실행 ID·코드·로그·판정을 Issue·PR에 연결 | **일부 충족 / 게시 단계 남음** | README와 증거의 상호 연결·#7 참조는 완료. 사용자 지시로 커밋·푸시·PR·Issue 게시 갱신 미수행. PR base develop, 관련 Issue #7로 연결할 예정 |
| 6. MQTT 미실행·환경 제한·남은 항목 기록 | 충족 | 아래 5~6절. A2·A3 및 총괄 #4 완료 표시 없음 |

## 5. 환경 문제·미실행·해석 제한

- 실행 구간이 끝나고 기록을 정리하는 동안 Git diff 통계에 `docker/mosquitto/config/mosquitto.conf` 5줄 추가가 나타났다. 내용 읽기 명령이 거절됐고, 사용자는 “다른 작업의 변경이므로 그대로 두세요”라고 답했다. 변경을 되돌리거나 수정·재시험하지 않았고 현재 내용·동작 영향은 미확인이다. 2~3절과 모델·실행 통과 기록은 environment.json 및 29-comparison.json의 시험 당시 설정 해시에 한정한다. 다른 작업이 끝난 뒤 최종 설정과 증거의 유효성을 대조해야 하며 이번 Issue 전체를 완료 처리하지 않는다.
- 기록 시작 전 읽기 전용 조회에서 Docker Linux 엔진 파이프가 없어 version의 Server가 null이고 ps·image 조회가 실패했다. `desktop status`도 실행 여부를 확인하지 못했다. 실행 증거 수집 시에는 엔진이 응답했으며 [05-desktop-start.log](05-desktop-start.log)는 이미 실행 중과 종료 0을 반환했다. 누가·어떤 동작으로 엔진을 시작했는지는 확인하지 않았다. 이 이전 조회에는 별도 종료 코드를 저장하지 않았으므로 이번 명령 기록으로 꾸미지 않는다.
- 명령 실행 도구의 샌드박스 초기화 오류로 필요한 명령은 승인된 실행 경로에서 수행했다. Docker 설정이나 Windows 보안 정책을 변경하지 않았다.
- HTML 생성과 로컬 링크 검사는 수행했다. 브라우저 자동 열기 도구는 두 번 모두 프로세스가 종료되어 화면 검증은 미실행이다. 파일 생성 성공을 브라우저 표시 검증으로 집계하지 않는다. 사용자는 브라우저 Ctrl+O로 `report.html`을 열어 확인할 수 있다.
- [29-comparison.json](29-comparison.json)의 `otherContainerSnapshotsIdentical=false`는 원문 문자열 대조 결과다. 기존 amr-mosquitto의 Mounts 배열 순서만 달랐고, Destination으로 정렬하여 필드별 비교한 [30-preservation-check.json](30-preservation-check.json)은 모두 true다. 원문 결과를 덮어쓰지 않았다.
- 호스트 listener·컨테이너 상태·로그는 실행 준비 근거다. MQTT CONNECT/CONNACK, 메시지 구독·발행, C# 클라이언트·Ctrl+C 수명주기·자동 재접속·GUI 시험은 **미실행 / 이번 범위 제외**다. TCP 접속 프로브도 수행하지 않았다.
- .NET 소스·패키지를 수정하지 않아 복원·빌드·기능 자동 시험을 실행하지 않았다. 과거 시험이나 시험 0건을 이번 제품 통과 근거로 사용하지 않았다.
- `restart: no`이므로 Docker/PC 재시작 뒤 자동 복구는 이번 보장 범위가 아니다. 후속 A2 전에 `version`, `ps -a`, `logs`를 조회하고, 필요하면 같은 서비스의 `up -d mosquitto`를 실행한다. 엔진·이미지·포트·마운트 오류가 있으면 해당 환경 원인을 해결한 뒤 새 실행 ID로 기록한다.

## 6. 종합 결과와 남은 단계

시험 당시 설정의 Broker 실행 준비 검증은 통과했고 16:13 KST 실행 조회에서 `amr-demo-mosquitto-1`은 running이었다. 이번 작업에서 설정 수정은 필요하지 않았다. 이후 다른 작업의 설정 변경은 보존하며 미검증으로 구분한다. 실행한 단계는 기준 확인 → 모델 검사 → 기동 → 상태·로그·포트·마운트 확인 → 중단 → 재기동 → 증거·README 정리다.

커밋·푸시·PR 생성·리뷰·병합·Issue 종료는 미수행이다. #7 완료조건 5의 게시 연결과 develop 반영은 후속 요청 단계에 남는다. #4의 원래 완료조건은 유지하며 자동 종료하지 않는다. #7 PR 반영·환경 증거 확인 뒤 A2 #8에 착수하고 이번에 A2·A3 코드는 만들지 않았다.

업무 기록 위치는 `C:\process-plan\일일업무\2026-10-08.md`다. 같은 날짜 기존 파일이 없어 새 날짜 기록에 이번 결과와 이 경로를 연결한다. 상세 로그는 코드 저장소에서 관리하며 별도 문서 저장소의 기존 변경과 커밋을 섞지 않는다.
