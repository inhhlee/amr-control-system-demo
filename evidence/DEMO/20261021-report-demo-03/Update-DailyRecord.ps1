$ErrorActionPreference = 'Stop'
$path = 'C:\process-plan\일일업무\2026-10-21.md'
$expectedSha = '7505C4ABEEBE7B13232ACBD9732E4FA8369F3ACFCB977E95D5DCFB5063ED3DDE'
if ((Get-FileHash -LiteralPath $path).Hash -ne $expectedSha) { throw 'Daily record changed; reread before appending.' }
$before = [IO.File]::ReadAllBytes($path)
$addition = @'

<a id="report-demo-results"></a>

## 후속 실제 시연 — 현재 코드 재현 결과

- 사용자 요청: 책임님께 보여드릴 시연을 현재 코드로 준비·실행하고 기존 변경을 보존한다. 격리 Broker를 사용하며 커밋·푸시는 수행하지 않는다. 기존 기능 재현 범위이며 과제1 잔여 개발 보류·과제2 우선순위를 유지한다.
- 코드·환경: 71160f412624f829f8b5673c409da5a4accf202c / feature/33-simulator-movement, Windows 10.0.26200, SDK 10.0.401, MQTTnet 5.2.0.1603, Mosquitto 2.1.2. 제품 소스 변경 없이 시험 도구·실행 안내·증거를 보완했다.
- 최종 실행: 20261021-report-demo-03, 2026-10-21 19:58:27~19:59:47 KST. 실제 MQTT 수신·검사, 명령 발행, KeepAlive, Broker 종료·새 접속, 응답 차단 감지, Simulator 초기화의 7단계가 통과했다.
- 실제 결과: 두 세션 28입력에서 수신 20·거부 4·구독 외 4, MOVE/STOP/RESET 3종 수신·불량 입력 차단·잔존 명령 0, K=5초에서 12.101초 유지·PING 2쌍, Broker 종료 요청→감지 58.70ms·정리 후 종료 1·새 PID/Client ID 접속 정상, 무응답 마지막 송신→감지 7.626초·첫 응답 폐기→감지 2.507초·종료 1, Simulator 별도 프로세스 15건 통과.
- 자동시험: 첫 실행 20261021-report-demo-01에서 Communication 768건·Simulator 129건(기존 88·이동 계산 41)을 표준 VSTest로 직접 실행해 모두 통과했고 두 전용 프로젝트 빌드 경고·오류 0을 확인했다. 최종 시연에서는 제품·자동시험·설정·프로젝트 파일 해시와 전건 통과 TRX를 대조해 재사용했다. 다른 종류 시험과 합산하지 않았다.
- 보존·정리: 기존 변경 26파일의 원시 바이트와 Git 인덱스·HEAD·브랜치를 대조해 보존했다. 기존 컨테이너 3개 상태를 유지했다. 최종 격리 Broker 4개·익명 볼륨·프록시·콘솔을 정리했고, 앞선 두 시도의 소유권이 확인된 익명 볼륨 12개도 별도로 제거했다. 다른 기존 볼륨은 보존했다.
- 실패·보완: 첫 시연의 Docker 잘린 JSON 목록 파싱 오류를 --no-trunc로 수정하고 이전 실패 기록을 보존했다. 2차 기능 통과 후 소유 컨테이너 제거 시 익명 볼륨도 제거하도록 보완해 최종 시연을 다시 실행했다. 도구의 사전 소유권 점검 중단과 보완 기록도 남겼다.
- 근거: [실행 순서](C:/amr-control-system-demo/tests/manual/DEMO/README.md) · [최종 결과·원본 로그·남은 기능](C:/amr-control-system-demo/evidence/DEMO/20261021-report-demo-03/README.md) · [보고용 로그 보기](C:/amr-control-system-demo/evidence/DEMO/20261021-report-demo-03/report.html) · [실행 판정](C:/amr-control-system-demo/evidence/DEMO/20261021-report-demo-03/verification.json) · [변경 보존](C:/amr-control-system-demo/evidence/DEMO/20261021-report-demo-03/preservation.json).
- 남은 기능: COM-02 개별 AMR Heartbeat 중단·복구, COM05-B 요청·ACK 대조, COM-06 자동 재접속·재구독, #34 Simulator 실제 시간 실행 루프와 이후 Heartbeat/Telemetry 송신·수신 연결, GUI·Core·DB·전체 통합 검증. 이번 Simulator 콘솔은 초기화 후 종료하며 이동 계산은 자동시험으로 확인했다.
- 미실행·한계: Seq·MQTT Explorer GUI·물리 AMR·운영 부하·전수 동시 경합은 이번에 시험하지 않았다. 명령의 별도 구독자 수신과 실제 AMR 수행, 새 프로세스 접속과 자동 복구를 구분한다. 감지 시간은 이번 로컬 실측이며 운영 SLA가 아니다. 과제1·2 전체 완료나 책임님 검토 완료로 표시하지 않는다.
'@
$addition = $addition.Replace("`r`n","`n").Replace("`n","`r`n") + "`r`n"
$bytes = [Text.UTF8Encoding]::new($false).GetBytes($addition)
$stream = [IO.File]::Open($path,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try { $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
$after = [IO.File]::ReadAllBytes($path)
$prefix = [byte[]]::new($before.Length)
[Buffer]::BlockCopy($after,0,$prefix,0,$before.Length)
$prefixSha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($prefix))
if ($prefixSha -ne $expectedSha) { throw 'Original daily record prefix changed.' }
[ordered]@{path=$path;originalSha256=$expectedSha;originalPrefixPreserved=$true;afterSha256=(Get-FileHash -LiteralPath $path).Hash;bytesAppended=$bytes.Length;updatedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'daily-record-update.json') -Encoding utf8
Write-Output 'Daily record appended; all original bytes preserved.'
