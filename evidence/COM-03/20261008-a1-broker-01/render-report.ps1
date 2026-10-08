# Render saved evidence only. This script never controls Docker or contacts MQTT.
$ErrorActionPreference = 'Stop'
$runDir = $PSScriptRoot
$evidenceFiles = @(Get-ChildItem -LiteralPath $runDir -File |
    Where-Object { $_.Extension -in '.json', '.jsonl', '.log' } | Sort-Object Name)
$body = (ConvertFrom-Markdown -LiteralPath (Join-Path $runDir 'README.md')).Html
foreach ($file in $evidenceFiles) {
    $body = $body.Replace('href="' + $file.Name + '"', 'href="#evidence-' + $file.Name + '"')
}
$body = $body.Replace('href="../../../README.md"', 'href="#reproduce"')
$rootReadme = (ConvertFrom-Markdown -LiteralPath (Join-Path $runDir '../../../README.md')).Html
$rootReadme = [regex]::Replace($rootReadme, 'href="(?!https?://|#|[A-Za-z]:|/)([^"\s]+)"', 'href="../../../$1"')
$evidenceHtml = foreach ($file in $evidenceFiles) {
    $raw = Get-Content -LiteralPath $file.FullName -Raw
    if ([string]::IsNullOrEmpty($raw)) { $raw = '(출력 없음 — 종료 코드는 commands.jsonl에서 확인)' }
    if ($file.Extension -eq '.json') {
        try { $raw = $raw | ConvertFrom-Json | ConvertTo-Json -Depth 30 } catch { }
    }
    $safeName = [System.Net.WebUtility]::HtmlEncode($file.Name)
    $safeText = [System.Net.WebUtility]::HtmlEncode($raw)
    '<details id="evidence-' + $safeName + '"><summary>' + $safeName + '</summary><pre>' + $safeText + '</pre></details>'
}
$html = @'
<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>COM-03 A1 · Broker 실행 검증</title>
<style>
:root{color-scheme:light;font-family:Segoe UI,Malgun Gothic,sans-serif;color:#192c38;background:#eef2f5}
body{margin:0;line-height:1.7}main{max-width:1180px;margin:32px auto;padding:40px;background:white;border:1px solid #dce3e8;border-radius:12px}
h1{font-size:30px;line-height:1.3;color:#123d48}h2{font-size:23px;margin-top:42px;border-top:1px solid #dce3e8;padding-top:24px}
a{color:#006c86;text-underline-offset:3px}code{background:#edf3f6;padding:2px 4px;border-radius:3px;overflow-wrap:anywhere}
table{border-collapse:collapse;width:100%;font-size:14px;margin:20px 0;display:block;overflow-x:auto}
th,td{border:1px solid #cedae0;padding:10px 12px;min-width:110px;text-align:left;vertical-align:top}th{background:#e7f1f3}
pre{font:13px/1.6 Consolas,monospace;background:#f3f6f8;padding:16px;white-space:pre-wrap;overflow-wrap:anywhere;border-radius:6px}
pre code{padding:0}details{border:1px solid #d5e0e6;border-radius:6px;margin:10px 0;padding:12px}summary{cursor:pointer;color:#075667;font-weight:600}
.notice{background:#fff5da;border-left:4px solid #bd8e29;padding:14px 18px}.bar{font-size:13px;letter-spacing:.05em;color:#496570}
@media(max-width:720px){main{margin:0;padding:20px;border:0;border-radius:0}h1{font-size:25px}}
@media print{body{background:white}main{margin:0;border:0;padding:0}details{break-inside:avoid}}
</style></head><body><main>
<p class="bar">AMR CONTROL SYSTEM DEMO · ISSUE #7 · 2026-10-08</p>
<p class="notice">시험 당시 설정의 로컬 실행 증거입니다. 이후 다른 작업의 설정 변경은 미검증입니다. MQTT 접속은 미실행이며 커밋·PR은 미생성입니다. 현재 컨테이너 상태를 실시간 조회하지 않습니다.</p>
'@
$html += $body
$html += '<h2 id="reproduce">재현 명령 — 루트 README 보기</h2><details><summary>README.md 펼치기</summary>' + $rootReadme + '</details>'
$html += '<h2>원본 증거</h2><p>파일명을 클릭해 저장된 기록을 펼치세요. 날짜는 명시된 시간대를 따릅니다.</p>'
$html += $evidenceHtml -join "`n"
$html += @'
<script>
function reveal(){const id=decodeURIComponent(location.hash.slice(1));const item=document.getElementById(id);if(item&&item.tagName==='DETAILS'){item.open=true;item.scrollIntoView();}}
addEventListener('hashchange',reveal);reveal();
</script></main></body></html>
'@
$html | Set-Content -LiteralPath (Join-Path $runDir 'report.html') -Encoding utf8
Write-Output ('Rendered report.html with ' + $evidenceFiles.Count + ' evidence files.')
