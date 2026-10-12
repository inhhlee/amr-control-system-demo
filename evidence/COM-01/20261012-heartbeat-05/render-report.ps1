$ErrorActionPreference = 'Stop'
$result = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json') -Raw | ConvertFrom-Json
[xml]$trx = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../20261012-heartbeat-04/tests.trx') -Raw
$counters = $trx.TestRun.ResultSummary.Counters
$totalReceived = ($result.runs | Measure-Object diagnosticCount -Sum).Sum
$totalExcluded = ($result.runs | Measure-Object negativeTopicCount -Sum).Sum
function Escape($value) { [System.Net.WebUtility]::HtmlEncode([string]$value) }
function LogSection($title, $file, $open = '') {
    $contents = Get-Content -LiteralPath (Join-Path $PSScriptRoot $file) -Raw
    "<details $open><summary>$(Escape $title)</summary><p><a href='$(Escape $file)'>원본 파일 열기</a></p><pre>$(Escape $contents)</pre></details>"
}
$logs = (LogSection '첫 실행 · 원문 진단 / Topic 경계 / Ctrl+C' 'console-1.stdout.log' 'open') +
    (LogSection '독립 관찰자 · 첫 실행의 발행 5건' 'observer-1.stdout.log') +
    (LogSection '새 실행 · 새 Client ID / 기본 원문 숨김 / Ctrl+C' 'console-2.stdout.log') +
    (LogSection 'Broker · 실제 연결과 정상 해제' 'broker.log') +
    (LogSection '최종 판정 · 실제 프로세스 종료 코드' 'verification.json')
$rows = ($result.runs | ForEach-Object {
    "<tr><td>실행 $($_.run)</td><td><code>$(Escape $_.clientId)</code></td><td>$($_.diagnosticCount)</td><td>$($_.exitCode)</td></tr>"
}) -join ''
$html = @"
<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>COM-01 · Heartbeat 수신 검증</title><style>
:root{font-family:'Malgun Gothic',system-ui,sans-serif;color:#203047;background:#f3f6fa}body{margin:0}main{max-width:1120px;margin:0 auto;padding:38px 24px 60px}header{background:#142a43;color:#fff;padding:30px;border-radius:16px}h1{font-size:29px;margin:8px 0 12px}h2{font-size:21px;margin-top:32px}p,li{line-height:1.75}a{color:#125ba2}header a{color:#b9e2ff}.eyebrow{letter-spacing:.1em;color:#acc7e2;font-size:13px}.cards{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;margin:22px 0}.card{background:white;border:1px solid #d8e2ed;border-radius:12px;padding:19px}.value{font-size:29px;font-weight:700;color:#176743}.label{font-size:13px;margin-top:7px}.pending{color:#8f5708}table{border-collapse:collapse;width:100%;background:white;margin:18px 0}th,td{text-align:left;padding:14px;border-bottom:1px solid #dce4ed;vertical-align:top;line-height:1.6}th{background:#e7eef6;font-size:14px}.pass{color:#176743;font-weight:700}.note{padding:16px 20px;background:#fff5df;border-left:4px solid #d89a2d}code{font-family:Consolas,monospace;overflow-wrap:anywhere}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#142338;color:#dce9f8;padding:18px;border-radius:8px;font-size:12px;line-height:1.7}details{background:white;border:1px solid #d8e2ed;border-radius:10px;padding:17px;margin:12px 0}summary{cursor:pointer;font-weight:700}nav{display:flex;gap:16px;flex-wrap:wrap;margin-top:18px}small{color:#55667b}.table-wrap{overflow-x:auto}@media(max-width:680px){main{padding:16px}.cards{grid-template-columns:repeat(2,1fr)}header{padding:22px}h1{font-size:24px}th,td{padding:10px}}
</style></head><body><main>
<header><div class="eyebrow">AMR CONTROL SYSTEM DEMO · ISSUE #15</div><h1>Heartbeat 한 종류 구독·수신</h1><p>2026-10-12 12:36 KST · 실제 Broker 검증 기록<br>실행 ID: <code>20261012-heartbeat-05</code> · 로컬 변경 / 미커밋</p><a href="https://github.com/inhhlee/amr-control-system-demo/issues/15">Issue #15 열기</a></header>
<div class="cards"><div class="card"><div class="value">$($counters.passed) / $($counters.total)</div><div class="label">자동시험 통과 · Fake</div></div><div class="card"><div class="value">$totalReceived 건</div><div class="label">실제 Heartbeat 수신</div></div><div class="card"><div class="value">$totalExcluded 건</div><div class="label">비구독 Topic 제외</div></div><div class="card"><div class="value">0 / 0</div><div class="label">두 콘솔의 실제 종료 코드</div></div></div>
<p class="note">저장된 실행 결과입니다. 실시간 관제 화면이 아닙니다. 수신 원문은 검사 전 진단이며 생존·정상 모델 판정을 하지 않습니다. MQTT 주소 <code>127.0.0.1:1884</code>는 웹페이지 주소가 아닙니다.</p>
<nav><a href="#criteria">완료조건</a><a href="#sessions">두 실행 비교</a><a href="#logs">원본 로그</a><a href="#remaining">남은 범위</a><a href="README.md">상세 검증결과</a></nav>
<h2 id="criteria">완료조건별 확인</h2><div class="table-wrap"><table><thead><tr><th>조건</th><th>판정</th><th>브라우저에서 확인할 내용</th></tr></thead><tbody>
<tr id="c1"><td>1. 구독 성공 뒤 수신</td><td class="pass">통과</td><td>첫 실행 로그에서 <code>Subscribed</code> 다음 <code>HeartbeatDiagnostic</code>를 확인합니다. Topic·UTC 시각·49바이트 원문을 <a href="publications-1.json">발행 입력</a>과 대조합니다.</td></tr>
<tr id="c2"><td>2. 비구독 Topic 제외</td><td class="pass">통과</td><td>독립 관찰자는 5건을 받았습니다. C# 콘솔에는 Heartbeat 2건만 있습니다. telemetry, heartbeat/extra, 추가 중간 level의 3건은 없습니다.</td></tr>
<tr id="c3"><td>3. Ctrl+C 정리·재실행</td><td class="pass">통과</td><td>두 로그의 <code>SubscriptionStopped → Disconnected → Disposed → ExitCode=0</code>과 서로 다른 Client ID를 확인합니다. 새 실행에서도 Heartbeat 1건이 수신됩니다.</td></tr>
<tr id="c4"><td>4. 결과·미검증 연결</td><td class="pending">부분 완료</td><td>README·로컬 증거·Issue 참조를 연결했습니다. 커밋·푸시·원격 Issue 결과 갱신·PR 연결은 사용자 요청에 따라 남아 있습니다.</td></tr>
</tbody></table></div>
<h2 id="sessions">두 실행 비교</h2><div class="table-wrap"><table><thead><tr><th>실행</th><th>Client ID</th><th>수신</th><th>종료 코드</th></tr></thead><tbody>$rows</tbody></table></div>
<p>첫 실행은 <code>--show-payload</code>로 통제된 시험 원문을 표시했습니다. 새 실행은 기본값 <code>Payload=&lt;hidden&gt;</code>을 확인했습니다. 실제 Windows CTRL_C_EVENT를 숨김 시험 콘솔에 전달했고, 제품 프로세스의 종료 코드를 직접 기록했습니다.</p>
<h2 id="logs">원본 로그 펼쳐 보기</h2>$logs
<h2 id="remaining">시험 범위와 남은 작업</h2><ul><li>빌드 경고·오류 0, 자동시험 $($counters.passed)개 통과. <a href="../20261012-heartbeat-04/build.log">빌드</a> · <a href="../20261012-heartbeat-04/test.log">자동시험</a> · <a href="../20261012-heartbeat-04/tests.trx">TRX</a></li><li>구독 거부·Topic 경계·이벤트 해제·원문 기본 숨김·256바이트 제한은 자동시험으로 확인했습니다.</li><li>Telemetry·Alarm·ACK 처리, JSON 검사·정상 모델 전달, 생존 갱신·Timeout, 자동 재접속은 후속 범위입니다.</li><li>사람이 키보드로 누른 Ctrl+C 및 Broker 내부 세션 DB 직접 검사는 미실행입니다.</li><li>첫 빌드와 시험 도구의 실패 기록은 <a href="../20261012-heartbeat-01/README.md">01</a>, <a href="../20261012-heartbeat-02/harness-attempt.md">02</a>, <a href="../20261012-heartbeat-03/README.md">03</a>, <a href="../20261012-heartbeat-04/README.md">04</a>에 보존했습니다.</li></ul>
<p><a href="../../../README.md#heartbeat-run">실행·발행·종료 재현 방법</a> · <a href="source-version.json">코드 버전·SHA-256</a> · <a href="environment.json">실행 환경</a></p><small>기준 HEAD 746c173 + 미커밋 변경. 이 보기본은 render-report.ps1로 저장된 JSON·로그·TRX에서 생성했습니다.</small>
</main></body></html>
"@
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'report.html'), $html, [Text.UTF8Encoding]::new($false))
