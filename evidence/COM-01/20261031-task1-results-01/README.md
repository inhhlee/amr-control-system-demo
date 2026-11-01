# 과제 1 실제 실행 결과

- 목적: 현재 통신 콘솔을 직접 실행하고 과제 1 결과물의 확인 가능한 부분을 보여준다.
- 보기본: [항목별 결과 화면](index.html). 제품 GUI가 아니라 실제 콘솔 출력의 읽기용 보기본이다.
- 실행 시각: 2026-10-31T22:38:02.6653356+09:00 ~ 2026-10-31T22:38:31.8503501+09:00.
- 코드: develop `4e462691db9dcc312d1d6b882773c5ff382eadc6`, 제품 소스 변경 없음. 새 실행 근거만 생성했다.
- 환경: Microsoft Windows 10.0.26200, SDK 10.0.401, MQTTnet 5.2.0.1603, Mosquitto 2.1.2-alpine. 실제 Docker 엔진은 [버전 원문](docker-server.json)을 따른다.
- 콘솔 빌드: [build.log](build.log), 경고 0 / 오류 0. 기존 자동시험 전체 재실행 없음.
- 실제 시연: [verification.json](verification.json)의 17항목 모두 확인. 입력은 [inputs.json](inputs.json), 외부 프로세스 명령은 [commands.jsonl](commands.jsonl), 소스·실행 DLL 해시는 [source-files.json](source-files.json).

| 시험 | 입력과 예상 결과 | 실제 결과·근거 |
|---|---|---|
| 접속·수신 | 별도 Broker 접속, 4유형을 장비별 전달 | [receiver.stdout.log](receiver.stdout.log), 2대의 Heartbeat/Telemetry와 시험 Alarm/ACK 수신 |
| 형식 검사 | ts 없는 Heartbeat는 거부, 정상 데이터 전달은 유지 | MissingField / $.ts 1건, 정상 전달 6건 + 복구 Heartbeat 1건. [원본 대조](report-audit.json) |
| 명령 발행 | MOVE(10,20)/STOP/RESET의 대상·ID·입력값 수신 일치 | 3건 일치. [발행 요청](command-requests.json), [실제 Broker 수신](command-observer.stdout.log) |
| 생존 감시 | 5초 신호 중단 시 무응답, 재수신 시 복구 | 1회 무응답·1회 복구, 관측 5.389초. [monitor.stdout.log](monitor.stdout.log) |
| Broker 단절 | 서버 중단 시 감지·실패 종료 | 기존 receiver/monitor 모두 종료 코드 1 |
| 서버 재시작 후 새 실행 | 명시적으로 새 콘솔 실행, 수신 후 Ctrl+C 정상 종료 | [fresh.stdout.log](fresh.stdout.log), [정상 종료 신호/신원](normal-stop.json), 종료 코드 0 |
| 정리 | 소유 Broker/프로세스 제거, 기존 컨테이너 보존 | 기존 3컨테이너 ID·상태 동일, 추가 컨테이너 없음. before/after.jsonl |

## 한계·남은 작업

- 자동 재접속·재구독은 미구현이다. Broker 재시작 후 새 프로그램을 실행한 시험과 구분한다.
- Alarm/ACK는 mosquitto_pub으로 만든 통신 검사 입력이다. 실제 가상 AMR의 장애나 명령 수행 응답이 아니다.
- 보낸 요청과 ACK를 연결하는 기능은 미구현이다. 이번 명령 검증은 Broker 관찰 수신까지다.
- 장시간·대량 입력·과제1 전체 완료조건 검증은 이번에 수행하지 않았다.
- 별도 Broker는 127.0.0.1 임의 포트, persistence=false, --pull never, 실행 ID 라벨로 소유권을 확인했다. 기존 프로젝트 Broker·Seq를 시작하거나 중단하지 않았다.

## 재현

저장소 루트에서 .NET 콘솔을 빌드하고, 현재 폴더의 run-task1-demo.ps1과 같은 절차를 새 실행 ID로 수행한다. 이 파일은 이번 실행 절차의 고정본이며 동일 실행 폴더를 덮어쓰지 않는다. 재실행 전 새 폴더에서 콘솔 빌드 출력을 build.log로 남겨야 한다. Docker Linux 엔진과 캐시된 Mosquitto 이미지가 필요하다. 제품 소스·실행 DLL·입력값은 위 해시와 실행 명령으로 식별한다.

## 보기본 확인과 전달

- HTML 구조·내장 JSON·JavaScript 문법과 원본 로그를 오프라인으로 확인했다. [보기본 확인 기록](view-verification.json).
- 브라우저 자동화는 file 프로토콜 정책으로 로컬 파일 열기를 차단했다. 다른 경로로 우회하지 않았고, 브라우저 렌더링·메뉴 클릭 확인은 수행하지 않았다. 사용자가 index.html을 브라우저에서 직접 열어 확인한다.
- 첫 보기본 생성은 Python의 7자리 소수 초 ISO 시각 파싱에서 실패했다. 원본 시각을 유지하고 표시 부분만 추출하도록 고친 뒤 생성·원본 대조를 완료했다. 제품 실행과 17항목 판정에는 영향이 없다.
