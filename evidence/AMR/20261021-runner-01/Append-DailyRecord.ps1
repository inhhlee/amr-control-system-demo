$ErrorActionPreference='Stop'
$path='C:/process-plan/일일업무/2026-10-21.md'
$expected='1C15C75E97481CD397B033CA06899E8B48E1E10115F3AA5A6CB6B135651A8816'
if ((Get-FileHash -LiteralPath $path).Hash -ne $expected) { throw 'Daily record changed; read the latest content before appending.' }
$before=[IO.File]::ReadAllBytes($path)
$addition=@'


<a id="task2-runner-local-results"></a>

## 과제2 출발·이동·도착 시연 — #34 로컬 구현·실제 검증 추가

- 사용자 요청: #34 실행 루프를 구현해 출발→경유점→도착·정지를 한 시나리오로 시연하고 실제 실행으로 확인. 기존 변경 보존, 커밋·푸시 금지.
- 실제 수행: Codex가 현재 #33 코드에 순차 실행 루프·단조 시각에 따른 이동·경유점/완료/취소 로그를 연결했다. 기본 (-1,0)→(1,0)→(1,2), 속도 1 시나리오는 실제 실행에서 4.020초 뒤 IDLE·속도 0·종료 코드 0으로 끝났다. 설정만 바꾼 (5,3)→(5,4)→(7,4), 속도 2도 1.565초 뒤 정상 도착했다.
- 검증: 전용 빌드 경고·오류 0, 표준 자동시험 135건 통과(기존 129+루프 6). 실제 콘솔 시나리오 6건과 설정 회귀 15건 통과. Windows CTRL_C_EVENT를 보낸 이동 중 취소·Int64 최대 갱신 간격의 취소에서 실제 프로세스 종료 코드 0 확인. 키보드를 사용자가 직접 누른 결과와 구분한다. 통신 자동시험·전체 솔루션 빌드는 이번에 반복하지 않았다.
- 실패 기록: 빌드 폴더 권한 제한, 시험 도구의 빈 로그 읽기와 Ctrl+C 무시 플래그 상속을 각각 기록하고 수정·재실행했다. 최종 제품 코드·설정·DLL 해시와 원본 로그를 아래 근거에 연결했다.
- 코드 기준: `71160f412624f829f8b5673c409da5a4accf202c` + 미커밋 변경, `feature/33-simulator-movement`. #33 PR #36은 조회 시 open·미병합. 이번 요청의 로컬 시연을 먼저 구현한 것으로, #34 문서의 선행 PR 반영·정식 브랜치 절차 완료와 구분한다. Git 인덱스·HEAD·브랜치를 유지했고 커밋·푸시·PR 수정·병합·Issue 종료는 하지 않았다.
- 기존 변경 보존: 이번 작업이 수정하지 않은 기존 파일 663개는 시작 해시와 동일했다. 작업 중 별도 수정이 확인된 DEMO 화면·문서·생성기 4개와 루트 README의 기존 안내 문구는 최신 내용을 그대로 유지했다. README에는 이번 과제2 시연 절만 추가했다.
- [시연 명령·설명 순서](<C:/amr-control-system-demo/tests/manual/AMR01/README.md>) · [상세 결과·완료조건 대조·남은 일](<C:/amr-control-system-demo/evidence/AMR/20261021-runner-01/README.md>) · [실제 6건 결과](<C:/amr-control-system-demo/evidence/AMR/20261021-runner-console-03/verification.json>) · [자동시험 TRX](<C:/amr-control-system-demo/evidence/AMR/20261021-runner-01/runner.trx>)
- 상태: 단일 AMR 첫 실행본의 로컬 구현·검증 통과. 정식 PR 반영과 과제계획의 상태 정리는 후속이며, MQTT Heartbeat/Telemetry·명령/ACK·다중 AMR·장애·GUI는 아직 후속 범위다. 과제2 전체 완료로 표시하지 않는다. 앞선 일일업무의 미착수·미검증 표기는 당시 기록이다.
'@
[IO.File]::AppendAllText($path,$addition,[Text.UTF8Encoding]::new($false))
$after=[IO.File]::ReadAllBytes($path)
$preserved=[Linq.Enumerable]::SequenceEqual([byte[]]$before,[byte[]]$after[0..($before.Length-1)])
@{Path=$path;BeforeHash=$expected;AfterHash=(Get-FileHash -LiteralPath $path).Hash;OriginalBytesPreserved=$preserved;AddedBytes=$after.Length-$before.Length} | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'daily-record-update.json')
if (!$preserved) { throw 'Original daily bytes changed.' }
Write-Output 'Daily result appended; original content preserved.'
