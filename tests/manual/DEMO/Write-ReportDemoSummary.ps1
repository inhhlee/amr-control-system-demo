param([Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,36}$')][string]$RunId)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repo
$root = Join-Path $repo "evidence/DEMO/$RunId"
$nl = [Environment]::NewLine
function Read-Json([string]$Path) { Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -DateKind String }
function Format-DemoNumber([double]$Value, [int]$Digits=3) { $Value.ToString("F$Digits",[Globalization.CultureInfo]::InvariantCulture) }
function ConvertTo-DemoHtmlText([string]$Text) { [Net.WebUtility]::HtmlEncode($Text) }
$verification = Read-Json "$root/verification.json"
$cleanup = Read-Json "$root/cleanup.json"
if ($verification.result -ne 'passed' -or $cleanup.result -ne 'passed' -or @($verification.steps).Count -ne 7) { throw 'A complete passed demo is required.' }
$environment = Read-Json "$root/environment.json"
$automaticRun = if (Test-Path "$root/automatic-reuse.json") { (Read-Json "$root/automatic-reuse.json").sourceRunId } else { $RunId }
$automatic = @()
foreach ($project in @('Communication','Simulator')) {
    [xml]$trx = Get-Content "evidence/DEMO/$automaticRun/automatic/$project/$project.trx" -Raw
    $counter = $trx.TestRun.ResultSummary.Counters
    if ([int]$counter.total -ne [int]$counter.passed) { throw 'Unexpected automated result.' }
    $automatic += [ordered]@{project=$project;passed=[int]$counter.passed;failed=[int]$counter.failed}
}
$messages = Read-Json "evidence/COM-01/$RunId-msg/verification.json"
$commands = Read-Json "evidence/COM-05/$RunId-cmd/verification.json"
$c1 = Read-Json "evidence/COM-03/$RunId-c1/verification.json"
$c2 = Read-Json "evidence/COM-03/$RunId-c2/verification.json"
$c3 = Read-Json "evidence/COM-03/$RunId-c3/measurement.json"
$simulator = Read-Json "evidence/AMR/$RunId-sim/verification.json"
$accepted = ($messages.sessions | Measure-Object delivered -Sum).Sum
$rejected = ($messages.sessions | Measure-Object rejected -Sum).Sum
$ignored = ($messages.sessions | Measure-Object ignored -Sum).Sum
$start = [DateTimeOffset]::Parse($verification.startedAtUtc).ToOffset([TimeSpan]::FromHours(9)).ToString('yyyy-MM-dd HH:mm:ss')
$end = [DateTimeOffset]::Parse($verification.completedAtUtc).ToOffset([TimeSpan]::FromHours(9)).ToString('HH:mm:ss')
$speech = '책임님, AMR의 상태 정보를 받고 이동·정지 같은 명령을 보내는 기본 통신 기능을 구현했습니다. 시험용 데이터를 보내 정상 수신과 잘못된 입력 처리를 확인했고, 통신 서버를 껐을 때 연결 이상을 알아채는지도 확인했습니다. 명령은 전달되는 단계까지 확인했으며, 이를 받아 가상 AMR이 실제로 움직이게 연결하는 부분은 다음 작업입니다.'
$rows = @(
    @{name='1. 정상 정보 받기';actual="시험용 AMR 2대의 정보를 구분해 $($accepted)건 받았습니다.";say='여러 AMR의 정보를 구분해서 받습니다.';href="../../COM-01/$RunId-msg/verification.json"},
    @{name='2. 잘못된 정보 거절하기';actual="잘못된 정보 $($rejected)건은 이유를 표시하고 거절했습니다. 그 뒤 정상 정보도 계속 받았습니다.";say='잘못된 정보가 와도 이후 정상 정보는 계속 받습니다.';href="../../COM-01/$RunId-msg/console-0.stdout.log"},
    @{name='3. 이동·정지 명령 보내기';actual='이동·정지·초기화 명령을 보내고, 시험용 수신기에서 받은 내용을 확인했습니다.';say='명령이 전달되는 단계까지 확인했습니다.';href="../../COM-05/$RunId-cmd/subscriber.stdout.log"},
    @{name='4. 연결 유지 확인하기';actual='정보를 보내지 않고 약 12초 기다려도 연결이 유지됐습니다.';say='정보를 보내지 않는 동안에도 연결이 유지됩니다.';href="../../COM-03/$RunId-c1/verification.json"},
    @{name='5. 서버 종료 알아채기';actual='연결이 끊긴 것을 기록하고, 사용 중이던 자원을 정리한 뒤 프로그램이 종료됐습니다.';say='연결 이상을 감지하고 기록을 남깁니다.';href="../../COM-03/$RunId-c2/lost.stdout.log"},
    @{name='6. 다시 실행해 연결하기';actual='서버를 다시 켜고 프로그램을 새로 실행하니 정상 연결됐습니다.';say='지금은 프로그램을 새로 실행해 연결합니다. 자동 연결은 다음 작업입니다.';href="../../COM-03/$RunId-c2/console.stdout.log"}
)
$remaining = @(
    'AMR 한 대가 상태 정보를 보내지 않을 때 그 AMR을 찾아내기.',
    '보낸 명령에 맞는 응답이 돌아왔는지 확인하기.',
    '연결이 끊기면 프로그램이 자동으로 다시 연결하게 만들기.',
    '가상 AMR의 이동 프로그램과 통신을 연결하고 명령대로 동작하게 만들기.'
)
# Preserve the earlier detailed report and its original failure history.
if (!(Test-Path "$root/technical-details.md")) {
    @('# 시험의 자세한 근거','',
      "- 실행 ID: $RunId / $start ~ $end KST",
      "- 코드: $($environment.head) / $($environment.branch)",
      "- 환경: $($environment.os), SDK $($environment.sdk), PowerShell $($environment.powershell), Docker $($environment.docker), MQTTnet 5.2.0.1603.",
      "- 자동시험: Communication $($automatic[0].passed)건·Simulator $($automatic[1].passed)건. 직접 실행 TRX는 ../$automaticRun/automatic/ 에 있다. 다른 종류의 시험과 합산하지 않는다.",
      "- 연결 유지: $(Format-DemoNumber $c1.session.observedSeconds)초, PING $($c1.pingRequests)쌍.",
      "- Broker 종료 요청→감지: $(Format-DemoNumber $c2.shutdown.detectionMilliseconds 2)ms, 단절 종료 1 / 새 실행 종료 0.",
      "- 무응답 마지막 송신→감지: $(Format-DemoNumber $c3.lastOutgoingToDetectSeconds)초, 첫 응답 폐기→감지: $(Format-DemoNumber $c3.firstDiscardToDetectSeconds)초.",
      "- Simulator 실제 콘솔: $(@($simulator.Cases).Count)개 별도 프로세스.",
      '- 기존 자동시험 재사용 여부는 automatic-reuse.json을 확인한다. 원본 로그·JSON을 수정하지 않는다.',
      '- 실제 AMR·Seq 화면·MQTT Explorer 화면·장시간 부하·모든 동시 경합은 이번 시험 범위 밖이다.',
      '- 자원 정리는 cleanup.json, 실행 코드는 source-version.json, 명령·시각은 commands.jsonl을 확인한다.',
      '- GUI·Core·DB 및 전체 통합 검증은 후속 과제에 남아 있다.'
    ) | Set-Content "$root/technical-details.md" -Encoding utf8
}
$userMovement = if (Test-Path "$root/user-movement.json") { Read-Json "$root/user-movement.json" } else { $null }
$task2Html = ''
$task2Markdown = ''
if ($null -ne $userMovement) {
    if ($userMovement.source -ne 'user-pasted-console-output' -or !(Test-Path "$root/user-movement.md")) { throw 'User movement provenance is missing.' }
    $arrival = Format-DemoNumber $userMovement.completedElapsedSeconds
    $waypointTime = Format-DemoNumber $userMovement.waypointObservedSeconds
    $initial = ConvertTo-DemoHtmlText $userMovement.initial
    $waypoint = ConvertTo-DemoHtmlText $userMovement.waypoint
    $destination = ConvertTo-DemoHtmlText $userMovement.destination
    $amrId = ConvertTo-DemoHtmlText $userMovement.amrId
    $exitNote = if ($null -eq $userMovement.exitCode) { '프로그램 종료 코드는 아직 확인하지 않았습니다.' } else { "사용자가 전달한 종료 코드: $(ConvertTo-DemoHtmlText ([string]$userMovement.exitCode))." }
    $task2Html = @"
<section class="movement" aria-labelledby="movement-title">
  <div class="section-title"><span aria-hidden="true">02</span><h2 id="movement-title">과제2 · 출발부터 도착·정지까지 확인했습니다</h2></div>
  <p class="source direct">사용자 직접 실행 · 대화로 전달한 PowerShell 로그 기준</p>
  <div class="flow" role="img" aria-label="$initial 출발, $waypoint 경유, $destination 도착 후 정지">
    <div class="node"><small>출발</small><strong>$initial</strong><small>$amrId · 이동 속도 $($userMovement.speedMps)m/s</small></div>
    <div class="connector">이동<span class="arrow" aria-hidden="true"></span></div>
    <div class="node primary"><small>경유점 통과</small><strong>$waypoint</strong><small>$waypointTime 초에 통과 확인</small></div>
    <div class="connector">이동<span class="arrow" aria-hidden="true"></span></div>
    <div class="node"><small>도착 · 정지</small><strong>$destination</strong><small>$arrival 초 · IDLE · 속도 0</small></div>
  </div>
  <aside class="speech" aria-label="과제2 설명 문안">
    <h3>이렇게 설명하세요 · 경로 그림을 가리키며</h3>
    <p>“다음은 가상 AMR이 설정한 경로대로 움직이는 기능입니다. 이 부분은 제가 직접 프로그램을 실행해서 확인했습니다.</p>
    <p>AMR 한 대가 $initial 에서 출발해 $waypoint 을 지나 $destination 에 도착하도록 설정했습니다.</p>
    <p>실행 중 좌표가 바뀌었고, 약 $arrival 초 뒤 목적지에 도착했습니다. 도착 후에는 대기 상태로 바뀌고 속도가 0이 되는 것을 로그로 확인했습니다.”</p>
  </aside>
  <p class="scope">설정 경로를 따라 움직이는 독립 시연입니다. 통신으로 받은 명령에 따라 움직이는 연결은 다음 단계입니다.</p>
  <details><summary>직접 실행한 로그와 확인 범위</summary>
    <pre>WaypointReached AmrId=$amrId Index=0 X=1 Y=0 ObservedElapsedSeconds=$waypointTime
SimulationCompleted AmrId=$amrId X=1 Y=2 State=IDLE CurrentSpeedMps=0 ElapsedSeconds=$arrival</pre>
    <p class="scope">$exitNote 실제 실행 시각은 전달받지 않았습니다. 이 화면은 실시간 화면이 아니라 전달받은 결과를 정리한 보고 화면입니다.</p>
    <p class="scope"><a href="user-movement.md">사용자 제공 로그 발췌 · 코드 · 테스트 근거</a> · <a href="../../../tests/manual/AMR01/README.md">시연 명령과 설명 순서</a></p>
  </details>
</section>
"@
    $task2Markdown = @"
## 과제2 — 사용자가 직접 실행한 이동 시연

근거는 이 대화에서 사용자가 직접 실행한 뒤 보내준 PowerShell 로그다. 위 과제1의 저장된 시험 및 Codex의 별도 자동시험과 구분한다.

| 단계 | 실제 확인한 결과 |
| --- | --- |
| 출발 | $($userMovement.amrId), $($userMovement.initial), IDLE·현재 속도 0 |
| 이동 | BUSY·현재 속도 $($userMovement.speedMps), 좌표 변화 |
| 경유점 통과 | $($userMovement.waypoint), $waypointTime 초에 통과 관찰 |
| 도착·정지 | $($userMovement.destination), $arrival 초, IDLE·현재 속도 0, SimulationCompleted |

$exitNote 실행한 정확한 시각도 미확인이다. 설정 경로의 독립 이동을 확인했으며 통신 명령에 따른 이동 연동은 후속이다.

[사용자 제공 로그 발췌·코드·테스트](user-movement.md) · [시연 순서](../../../tests/manual/AMR01/README.md)

> 설정한 경로대로 이동했고, 목적지에 도착한 뒤 대기 상태와 속도 0으로 바뀌었습니다.
"@
}
$md = [Collections.Generic.List[string]]::new()
foreach ($line in @('# AMR 통신 기능 — 책임님 보고용 결과','',"시험한 시간: $start ~ $end (한국 시간)",'','## 이렇게 보고하면 됩니다','',$speech,'','실제 AMR 대신 시험용 데이터를 사용했습니다. 아래 내용은 그때 저장한 시험 결과입니다.','','## 직접 확인한 동작','','| 확인할 내용 | 실제 결과 | 기록 |','| --- | --- | --- |')) { $md.Add($line) }
foreach ($row in $rows) { $md.Add("| $($row.name) | $($row.actual) | [확인]($($row.href)) |") }
foreach ($line in @('','서버가 켜져 있어도 응답을 보내지 않는 상황을 추가로 만들었고, 이때도 프로그램이 이상을 알아채고 종료했습니다.','','## 가상 AMR은 어디까지 됐나요?','','설정 파일을 읽어 AMR의 이름과 처음 위치를 표시하는 기능은 확인했습니다. 시간이 지났을 때 어디로 이동해야 하는지 계산하는 부분도 자동시험으로 확인했습니다. 이 시험 당시 실행 프로그램은 초기 정보를 표시한 뒤 종료했습니다. 계속 움직이면서 통신 프로그램에 정보를 보내는 연결은 다음 작업입니다.','','## 아직 남은 일','')) { $md.Add($line) }
foreach ($item in $remaining) { $md.Add('- ' + $item) }
foreach ($line in @('','명령을 받았다는 사실과 실제 AMR이 움직였다는 사실은 따로 확인해야 합니다. 현재는 시험용 수신기까지 명령이 전달되는 것을 확인했습니다.','','## 보고할 때 여는 화면','','[한 장 보고 화면](report.html)을 열어 **통신 흐름 그림 → 핵심 시험 결과 → 남은 일** 순서로 설명합니다. 원본 기록은 질문을 받았을 때 확인합니다. 새 시험을 준비할 때만 [시험 실행 안내](../../../tests/manual/DEMO/README.md)를 사용합니다.','','이미 확인한 이 결과를 바로 보여주려면 PowerShell 7에서 아래 명령을 입력하세요. 저장된 화면을 여는 명령이며 새 시험은 실행하지 않습니다.','','~~~powershell','Set-Location C:\amr-control-system-demo',"Invoke-Item '.\evidence\DEMO\$RunId\report.html'",'~~~','','## 자세한 기록이 필요할 때','','- [쉬운 보고 화면](report.html) · [기술 수치·원본 근거·이전 실패 기록](technical-details.md)',"- 코드 자동시험: 통신 $($automatic[0].passed)건, 가상 AMR $($automatic[1].passed)건. 실제 프로그램 실행 시험과 나누어 기록했습니다.",'- [전체 시험 결과](verification.json) · [시험용 서버 정리](cleanup.json) · [원본 실행 명령](commands.jsonl)')) { $md.Add($line) }
if (Test-Path "$root/preservation.json") { $md.Add('- [기존 변경을 보존한 확인 기록](preservation.json)') }
$md.ToArray() | Set-Content "$root/README.md" -Encoding utf8
if ($task2Markdown) {
    $reportText = [IO.File]::ReadAllText("$root/README.md")
    $reportText = $reportText.Replace('# AMR 통신 기능 — 책임님 보고용 결과', '# 과제1 통신 → 과제2 이동 시연 → 남은 일')
    $reportText = $reportText.Replace('시험한 시간:', '과제1 저장된 시험 시간:')
    $reportText = $reportText.Replace('## 직접 확인한 동작', '## 과제1 — 저장된 통신 시험 결과')
    $begin = $reportText.IndexOf('## 가상 AMR은 어디까지 됐나요?')
    $finish = $reportText.IndexOf('## 아직 남은 일', $begin)
    if ($begin -lt 0 -or $finish -lt 0) { throw 'Expected report section is missing.' }
    $reportText = $reportText.Remove($begin, $finish-$begin).Insert($begin, $task2Markdown + $nl + $nl)
    $reportText = $reportText.Replace('통신 흐름 그림 → 핵심 시험 결과 → 남은 일', '과제1 저장된 통신 시험 → 과제2 직접 실행한 이동 시연 → 남은 일')
    $reportText = $reportText.Replace('아래 내용은 그때 저장한 시험 결과입니다.', '과제1은 그때 저장한 시험 결과이며, 과제2는 사용자가 이후 직접 실행해 전달한 결과입니다.')
    $reportText = $reportText.Replace('## 아직 남은 일', '## 남은 일 — 이동 프로그램과 통신 연결')
    [IO.File]::WriteAllText("$root/README.md", $reportText, [Text.UTF8Encoding]::new($false))
}
$commandCount = @($commands.requests).Count
$html = @"
<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="description" content="과제1 저장된 통신 시험, 과제2 사용자 직접 이동 시연, 남은 일을 순서대로 정리한 보고 화면입니다.">
<title>과제1 통신 · 과제2 이동 시연 — 개발 결과 보고</title>
<style>
:root{color-scheme:light;--ink:#182d43;--muted:#53677b;--blue:#255ec6;--line:#dce4ee;--green:#176344}
*{box-sizing:border-box}
body{margin:0;background:#f4f6fa;color:var(--ink);font:16px/1.5 'Segoe UI','Malgun Gothic',sans-serif;word-break:keep-all;overflow-wrap:anywhere}
main{max-width:1240px;margin:auto;padding:24px 32px 16px}
p,h1,h2,h3,figure{margin:0}
header{margin-bottom:20px}
.eyebrow{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;color:var(--muted);font-size:13px;margin-bottom:8px}
.saved{color:var(--green);font-weight:700}
h1{font-size:clamp(26px,2.8vw,36px);font-weight:750;line-height:1.35;letter-spacing:-.04em}
.lead{margin-top:6px;color:var(--muted)}
section+section{margin-top:20px}
.section-title{display:flex;align-items:baseline;gap:10px;margin-bottom:10px}
.section-title span{font-size:12px;font-weight:800;letter-spacing:.08em;color:var(--blue)}
h2{font-size:18px;font-weight:700}
.flow{display:grid;grid-template-columns:minmax(0,1fr) 82px minmax(0,1.1fr) 82px minmax(0,1fr);align-items:center;gap:10px}
.node{background:#fff;border:1px solid var(--line);border-radius:10px;text-align:center;padding:12px 10px}
.node.primary{border-color:#b4c9ef;background:#edf3ff}
.node strong{display:block;font-size:21px;letter-spacing:-.03em}
.node small{display:block;margin-top:3px;color:var(--muted);font-size:13px}
.connector{text-align:center;color:var(--blue);font-size:13px;font-weight:700}
.arrow{display:block;position:relative;margin-top:8px;width:100%;height:2px;background:#8ca9dc}
.arrow:after{content:'';position:absolute;right:0;top:-4px;width:9px;height:9px;border-top:2px solid #8ca9dc;border-right:2px solid #8ca9dc;transform:rotate(45deg)}
.results{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:14px}
article{background:#fff;border:1px solid var(--line);border-top:3px solid var(--blue);border-radius:10px;padding:15px 18px}
h3{font-size:16px;font-weight:700}
.metric{display:flex;align-items:baseline;gap:8px;margin:8px 0 5px;color:var(--blue);font-size:18px;font-weight:700}
.metric strong{font-size:36px;line-height:1.15;font-variant-numeric:tabular-nums;letter-spacing:-.04em}
.result-note{color:var(--muted);font-size:14px;line-height:1.6}
.scope{margin-top:10px;font-size:14px;color:var(--muted)}
.scope strong{color:var(--ink)}
.source{font-size:13px;color:var(--muted);margin:0 0 12px}
.source.direct{color:#176344;font-weight:700}
.movement{border-top:2px solid #bdd8ce;padding-top:18px}
.movement .node.primary{background:#edf8f2;border-color:#bdd8ce}
.speech{margin-top:14px;padding:16px 20px;background:#edf3ff;border-left:3px solid var(--blue);border-radius:6px;font-size:16px;line-height:1.8}
.speech h3{margin-bottom:8px;font-size:13px;color:var(--blue)}
.speech p+p{margin-top:8px}
.movement .speech{background:#edf8f2;border-left-color:#176344}
.movement .speech h3{color:#176344}
.next .speech{background:#fffdf7;border-left-color:#876321}
.next .speech h3{color:#876321}
details{margin-top:10px;font-size:13px;color:var(--muted)}
summary{cursor:pointer;text-decoration:underline;text-underline-offset:3px}
pre{overflow:auto;white-space:pre-wrap;overflow-wrap:anywhere;padding:12px;background:#fff;border:1px solid var(--line);font-size:12px}
a{color:var(--blue)}
.next{background:#fbf7ed;border-left:3px solid #b68a37;padding:14px 18px}
.next .section-title{margin-bottom:8px}
.next .section-title span{color:#876321}
.remaining{list-style:none;padding:0;margin:0;display:grid;grid-template-columns:repeat(2,minmax(0,1fr));column-gap:24px;row-gap:10px}
.remaining li{font-size:14px;color:var(--muted)}
.remaining strong{color:var(--ink);margin-right:8px;font-size:15px}
footer{margin-top:14px;padding-top:10px;border-top:1px solid var(--line);display:flex;justify-content:space-between;gap:8px 18px;flex-wrap:wrap;font-size:12px;color:var(--muted)}
footer a{color:var(--muted);text-underline-offset:3px}
footer nav{display:flex;gap:16px;flex-wrap:wrap}
@media(max-width:800px){main{padding:20px}.flow{grid-template-columns:minmax(0,1fr) 56px minmax(0,1fr) 56px minmax(0,1fr);gap:6px}.node strong{font-size:18px}.node small{font-size:12px}article{padding:13px}.results{gap:10px}.remaining strong{display:block;margin-right:0}}
@media(max-width:600px){main{padding:18px 16px}.lead{font-size:14px}.flow{grid-template-columns:1fr;gap:0}.connector{display:flex;align-items:center;justify-content:center;gap:16px;min-height:38px}.arrow{width:20px;margin:0;transform:rotate(90deg)}.results,.remaining{grid-template-columns:1fr}.metric strong{font-size:32px}.remaining strong{display:inline;margin-right:8px}.scope{line-height:1.7}section+section{margin-top:18px}}
@media print{@page{size:A4 landscape;margin:10mm}body{background:#fff;font-size:14px}main{max-width:none;padding:0}header{margin-bottom:14px}h1{font-size:28px}.lead{font-size:14px}section+section{margin-top:14px}h2{font-size:16px}.node strong{font-size:18px}article{padding:12px 14px}.metric strong{font-size:30px}.result-note,.scope,.remaining li{font-size:12px}.remaining strong{font-size:13px}section{break-inside:avoid}footer nav{display:none}}
</style>
</head>
<body>
<main>
<header>
  <div class="eyebrow"><p>가상 AMR 관제시스템 · 개발 결과 보고</p><p class="saved">과제1 → 과제2 → 남은 일</p></div>
  <h1>통신 결과와 가상 AMR 이동 시연을 보고드립니다.</h1>
  <p class="lead">과제별 확인 결과와 근거를 구분했습니다. 다음 단계는 이동 프로그램과 통신을 연결하는 일입니다.</p>
</header>

<section aria-labelledby="flow-title">
  <div class="section-title"><span aria-hidden="true">01</span><h2 id="flow-title">과제1 · 정보를 받고 명령을 전달했습니다</h2></div>
  <p class="source">저장된 통신 시험 결과 · 시험용 데이터 사용 · $start ~ $end (한국 시간)</p>
  <div class="flow" role="img" aria-label="시험용 AMR 두 대의 정보를 통신 프로그램이 받아 검사하고, 통신 프로그램에서 보낸 이동·정지·초기화 명령을 시험용 수신기가 받는 흐름">
    <div class="node"><strong>시험용 AMR 정보</strong><small>AMR_001 · AMR_002</small></div>
    <div class="connector">정보 수신<span class="arrow" aria-hidden="true"></span></div>
    <div class="node primary"><strong>통신 프로그램</strong><small>정보 검사 · 수신 / 명령 발행</small></div>
    <div class="connector">명령 전달<span class="arrow" aria-hidden="true"></span></div>
    <div class="node"><strong>시험용 수신기</strong><small>이동 · 정지 · 초기화</small></div>
  </div>
  <div class="section-title" style="margin-top:14px"><h3>통신 시험에서 확인한 결과</h3></div>
  <div class="results">
    <article>
      <h3>정상 정보 수신</h3>
      <p class="metric"><strong>$accepted</strong><span>건 수신</span></p>
      <p class="result-note">시험용 AMR 두 대의 정보를<br>각각 구분해서 받았습니다.</p>
    </article>
    <article>
      <h3>잘못된 정보 처리</h3>
      <p class="metric"><strong>$rejected</strong><span>건 거절</span></p>
      <p class="result-note">잘못된 값은 이유를 남기고 거절하고,<br>이후 정상 정보는 계속 받았습니다.</p>
    </article>
    <article>
      <h3>명령 전달 · 연결 감시</h3>
      <p class="metric"><strong>$commandCount</strong><span>종 명령 전달</span></p>
      <p class="result-note">이동 · 정지 · 초기화 수신 확인<br>약 $(Format-DemoNumber $c1.session.observedSeconds 0)초 연결 유지 · 서버 종료 감지</p>
    </article>
  </div>
  <p class="scope"><strong>확인 범위</strong> &nbsp; 명령은 시험용 수신기까지 전달됐습니다. 서버를 다시 켠 뒤에는 프로그램을 새로 실행해 연결했습니다.</p>
  <aside class="speech" aria-label="과제1 설명 문안">
    <h3>이렇게 설명하세요 · 통신 시험 결과를 보며</h3>
    <p>“책임님, 먼저 AMR의 정보를 받고 명령을 전달하는 통신 기능을 확인했습니다.</p>
    <p>시험용 AMR 두 대의 정상 정보 $($accepted)건을 구분해서 받았고, 잘못된 정보 $($rejected)건은 이유를 남기고 거절했습니다.</p>
    <p>이동·정지·초기화 명령도 시험용 수신기까지 전달되는 것을 확인했습니다. 연결 유지와 서버가 꺼졌을 때 연결 끊김을 감지하는 것도 확인했습니다.</p>
    <p>이 부분은 앞서 실행해서 저장해 둔 시험 결과입니다.”</p>
  </aside>
</section>

$task2Html

<section class="next" aria-labelledby="remaining-title">
  <div class="section-title"><span aria-hidden="true">03</span><h2 id="remaining-title">남은 일</h2></div>
  <ul class="remaining">
    <li><strong>가상 AMR 연동</strong>이동 중인 위치 보내기 · 받은 명령대로 동작</li>
    <li><strong>명령 응답 확인</strong>보낸 명령과 돌아온 응답 맞추기</li>
    <li><strong>자동 재연결</strong>연결이 끊긴 뒤 접속 · 구독 자동 복구</li>
    <li><strong>AMR별 정보 중단 감지</strong>한 대가 상태를 보내지 않을 때 알아내기</li>
  </ul>
  <aside class="speech" aria-label="남은 일 설명 문안">
    <h3>이렇게 설명하세요 · 다음 작업을 말씀드리며</h3>
    <p>“현재는 통신 기능과 가상 AMR의 경로 이동을 각각 확인한 상태입니다.</p>
    <p>다음에는 이동 중인 AMR의 위치를 통신으로 보내고, 받은 명령에 따라 움직이도록 두 기능을 연결할 예정입니다.</p>
    <p>이후에는 보낸 명령에 맞는 응답이 왔는지 확인하는 기능, 연결이 끊겼을 때 자동으로 다시 연결하는 기능, AMR 한 대의 정보가 중단됐을 때 알아내는 기능이 남아 있습니다.”</p>
  </aside>
</section>

<footer>
  <p>과제1 실행 ID: $(ConvertTo-DemoHtmlText $RunId) · 과제2 출처는 해당 절에 표시</p>
  <nav aria-label="시험 근거"><a href="technical-details.md">과제1 상세 시험 근거</a><a href="README.md">전체 결과 문서</a></nav>
</footer>
</main>
</body>
</html>
"@
$html | Set-Content "$root/report.html" -Encoding utf8
[ordered]@{runId=$RunId;result='passed';received=$accepted;rejected=$rejected;ignored=$ignored;commandCount=@($commands.requests).Count;keepAliveSeconds=$c1.session.observedSeconds;pingPairs=$c1.pingRequests;brokerStopDetectionMs=$c2.shutdown.detectionMilliseconds;brokerExitMs=$c2.shutdown.productExitMilliseconds;lastOutgoingToDetectSeconds=$c3.lastOutgoingToDetectSeconds;firstDiscardToDetectSeconds=$c3.firstDiscardToDetectSeconds;simulatorProcesses=@($simulator.Cases).Count;automatic=$automatic} | ConvertTo-Json -Depth 5 | Set-Content "$root/summary.json" -Encoding utf8
Write-Output "보고용 화면: $root/report.html"
Write-Output "쉬운 결과 문서: $root/README.md"
