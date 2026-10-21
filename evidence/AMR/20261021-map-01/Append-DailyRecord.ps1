$ErrorActionPreference = 'Stop'
$path = 'C:/process-plan/일일업무/2026-10-21.md'
$expected = 'C872A4FF0757D4F1A09C4A0FB024FE128C2E647650BC731E6B5B533B60A55829'
if ((Get-FileHash -LiteralPath $path).Hash -ne $expected) { throw 'Daily record changed; read current contents before retrying.' }
$status = @(git -C C:/process-plan status --short)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect document repository.' }
$branch = git -C C:/process-plan branch --show-current
$origin = git -C C:/process-plan remote get-url origin
$before = [IO.File]::ReadAllBytes($path)
$addition = @'


<a id="task2-live-map-results"></a>

## 과제2 실제 좌표 2D 지도 — 사용자 추가 시연 요청

- 요청·수행: 실제 Simulator 좌표로 출발→경유점→목적지 이동을 표시하는 로컬 웹 도구를 추가했다. 기존 SimulatorApplication.RunAsync를 같은 프로세스에서 실행하고 실제 출력 좌표를 화면에 표시한다. 설정·제품 소스는 보존했으며 과제3 전체 GUI 완료로 확대하지 않는다.
- 직접 검증: 지도 도구 빌드 경고·오류 0. 기본 경로와 변경 경로의 HTTP 상태 표본 총 141개를 실제 로그의 좌표·속도·상태와 대조했다. 정상 도착·중복 시작 거부·취소·재실행을 확인했다. 이동 중 서버 종료 시 실행 취소·호스트 종료 코드 0·포트 해제를 확인했다. 141은 자동시험 개수가 아니며 기존 자동시험 135건은 이번에 재실행하지 않았다.
- 브라우저 시연: 실제 버튼으로 실행하여 최종 (1,2)·IDLE·속도 0·종료 코드 0, 경과 4.029초를 확인했다. 사용자가 앞서 콘솔에서 실행한 4.064초 결과와 별도 근거다. 기본 경로 서버는 사용자의 즉시 시연용으로 유지하고 별도 검증 서버는 종료했다.
- 코드 기준·보존: HEAD 71160f412624f829f8b5673c409da5a4accf202c + 기존 #34 로컬 변경 + 새 지도 도구. feature/33-simulator-movement와 Git 인덱스를 유지했다. 기존 파일 805개가 동일했고 안내 2개를 보완했다. 확인 중 발견한 솔루션의 지도 등록 변경도 그대로 유지했다. 커밋·푸시·PR 변경은 하지 않았다.
- 근거: [시연 순서](C:/amr-control-system-demo/tools/Amr.Simulator.Map/README.md) · [결과·화면·로그·검증 명령](C:/amr-control-system-demo/evidence/AMR/20261021-map-01/README.md).
- 남은 범위: MQTT 송신·수신 연계, 다중 AMR·장애물·지도 편집·정식 관제 GUI·운영 부하·정식 PR 리뷰/반영. 이번 시연 도구에 대한 로컬 검증과 과제 전체 완료를 구분한다. 최초 빌드 권한 제한과 재실행 성공 기록도 위 결과에 보존했다.
'@
[IO.File]::AppendAllText($path, $addition + "`r`n", [Text.UTF8Encoding]::new($false))
$after = [IO.File]::ReadAllBytes($path)
$prefixSame = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($after[0..($before.Length-1)])) -eq $expected
if (-not $prefixSame) { throw 'Original daily content prefix mismatch.' }
@{Path=$path; BeforeHash=$expected; AfterHash=(Get-FileHash $path).Hash; OriginalPrefixPreserved=$prefixSame; Branch=$branch; Origin=$origin; PreexistingStatus=$status} | ConvertTo-Json -Depth 4 | Set-Content 'C:/amr-control-system-demo/evidence/AMR/20261021-map-01/daily-record-update.json' -Encoding utf8
Write-Output 'Daily record appended; existing bytes preserved.'
