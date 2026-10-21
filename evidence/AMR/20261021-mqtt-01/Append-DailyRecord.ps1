$ErrorActionPreference='Stop'
$path='C:/process-plan/일일업무/2026-10-21.md'
$expected='E7C8CAFD7DC7DDA90B0555628C98973CB02B65366552349D4C3F6B0C7D75982C'
if((Get-FileHash -LiteralPath $path).Hash -ne $expected){throw 'Daily record changed; inspect before retrying.'}
$status=@(git -C C:/process-plan status --short)
if($LASTEXITCODE -ne 0){throw 'Cannot inspect document repository.'}
$before=[IO.File]::ReadAllBytes($path)
$addition=@'


<a id="task2-mqtt-local-results"></a>

## 과제2 실제 위치·상태 MQTT 송신과 관제 수신 — AMR-05 일부

- 사용자 요청: 남은 작업 1번인 실제 시뮬레이터 위치·상태 송신과 기존 관제 수신까지 구현·격리 Broker 실행 확인. 기존 변경 보존, 커밋·푸시 금지.
- 실제 수행: Simulator에 선택적 MQTT 모드와 명시적 송신 주기를 추가했다. 실제 이동의 불변 복사본으로 Telemetry·Heartbeat를 만들고 기존 Communication 계약으로 검사·발행한다. MQTT 없이 실행하는 기존 동작과 관제 수신 코드는 유지했다. 이번 수신 대상은 기존 콘솔의 검사·AMR별 큐·정상 모델 전달이며 Core·DB·지도 전체 연동은 후속이다.
- 최종 검증: 빌드 경고·오류 0, Simulator 자동시험 151건(기존135+신규16) 통과. 실제 별도 Simulator·관제 콘솔·격리 Mosquitto의 7개 시나리오 통과. 기본 경로 Telemetry21·Heartbeat4, 변경 경로9·2, 초기부터 도착2·1로 정상 수신39건을 대조했다. 위치32건은 실제 이동 로그와 일치했다. 기본 경로는 4.025초 뒤 (1,2)·IDLE·속도0·종료0이다.
- 추가 실제 확인: Retain 잔존 메시지 없음, Int64 최대 이동 갱신 대기 중 독립 송신·Ctrl+C 종료0, Broker 종료 시 단절 사유·Simulator/관제 종료1, 최초 접속 실패 시 이동 시작 없음·종료1. 출발·주기·도착 송신과 사용자 취소/단절을 구분했다. 완료 뒤 늦은 취소·해제 실패는 자동시험으로 확인했다.
- 조건·한계: MQTT5·QoS0·Retain=false. 시연 주기 Telemetry200ms·Heartbeat1000ms는 운영 확정이 아니며 위치 갱신 주기와 별도다. Published 반환 자체는 수신 보장이 아니므로 관제 Received와 대조했다. 자동 재접속·다중 AMR·명령/ACK·Alarm·MQTT 좌표 지도 표시·Core/DB는 남아 있다. 기존 통신 전체 자동시험·Seq·MQTT Explorer GUI는 이번에 반복하지 않았다.
- 코드·보존: HEAD 71160f412624f829f8b5673c409da5a4accf202c / feature/33-simulator-movement + 기존 미커밋 변경 + 이번 송신 구현. 기존 파일935개 동일·Git 인덱스/HEAD/브랜치 유지. 기존 지도 서버의 열린 로그2개는 시작 해시를 읽을 수 없어 전후 해시 판정에서 제외했고 이 작업이 쓰지 않았다. 기존 Docker 컨테이너3개 상태 유지, 시험용 Broker·프로세스 정리. 커밋·푸시·Issue/PR 변경 없음.
- 실패·보완: NuGet 잠금 파일 권한 오류는 최초 로그에 보존하고 이번 빌드 프로세스만 종료한 뒤 정상 사용자 권한으로 재실행했다. 1차149건·실제 실행01 후 종료 경계 보완을 반영해 최종151건·실제 실행02로 검증했다. 시험 수치를 합산하지 않는다.
- 근거: [쉬운 시연 순서·명령](C:/amr-control-system-demo/tests/manual/AMR01/MQTT-DEMO.md) · [최종 결과·소스·로그·TRX](C:/amr-control-system-demo/evidence/AMR/20261021-mqtt-01/README.md) · [실제7개 판정](C:/amr-control-system-demo/evidence/AMR/20261021-mqtt-live-02/verification.json).
- 다음 확인: 이번에 관제가 수신한 위치·상태를 2D 지도에 연결하여 같은 좌표를 표시하는 단계. 기존 과제계획 v0.12의 미착수/미검증 표기는 이전 단계의 기록이며 이번 로컬 실적은 이 절을 따른다. 정식 Issue/PR 반영·과제계획 전체 상태 정리는 별도 후속이고 과제2 전체 완료로 표시하지 않는다.
'@
[IO.File]::AppendAllText($path,$addition+"`r`n",[Text.UTF8Encoding]::new($false))
$after=[IO.File]::ReadAllBytes($path)
$preserved=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($after[0..($before.Length-1)])) -eq $expected
@{Path=$path;BeforeHash=$expected;AfterHash=(Get-FileHash $path).Hash;OriginalPrefixPreserved=$preserved;PreexistingStatus=$status} | ConvertTo-Json -Depth 4 | Set-Content 'C:/amr-control-system-demo/evidence/AMR/20261021-mqtt-01/daily-record-update.json'
if(!$preserved){throw 'Original content prefix mismatch'}
Write-Output 'Daily result appended; original content preserved.'
