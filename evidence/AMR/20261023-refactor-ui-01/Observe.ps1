$ErrorActionPreference = 'Stop'
$runDir = $PSScriptRoot
$endpoint = 'http://127.0.0.1:59344/api/grid/state'
$initial = Invoke-RestMethod -Uri $endpoint -TimeoutSec 5
$initial | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath "$runDir\initial.json" -Encoding utf8
$logPath = $initial.evidencePath
$lineCount = @(Get-Content -LiteralPath $logPath).Count
[ordered]@{ startedAt=(Get-Date -Format o); branch=(git branch --show-current); commit=(git rev-parse HEAD); sourceLog=$logPath; originalLogLines=$lineCount; sdk=(dotnet --version) } | ConvertTo-Json | Set-Content -LiteralPath "$runDir\environment.json" -Encoding utf8
$watch = [System.Diagnostics.Stopwatch]::StartNew()
$started = $false
$samples = 0
$writer = [System.IO.StreamWriter]::new("$runDir\api-samples.jsonl", $false, [System.Text.UTF8Encoding]::new($false))
try {
    Set-Content -LiteralPath "$runDir\observer-ready.txt" -Value 'ready' -Encoding utf8
    while ($watch.Elapsed.TotalSeconds -lt 120) {
        $state = Invoke-RestMethod -Uri $endpoint -TimeoutSec 3
        $sample = [ordered]@{ observedAt=(Get-Date -Format o); state=$state }
        $writer.WriteLine(($sample | ConvertTo-Json -Depth 15 -Compress))
        $writer.Flush()
        $samples++
        if ($state.navigation.phase -in @('Connecting','Running','Waiting','WaitingForTelemetry')) { $started = $true }
        if ($started -and $state.navigation.phase -in @('Completed','Failed','Canceled')) {
            $state | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath "$runDir\final.json" -Encoding utf8
            Get-Content -LiteralPath $logPath | Select-Object -Skip $lineCount | Set-Content -LiteralPath "$runDir\run-events.jsonl" -Encoding utf8
            [ordered]@{ finishedAt=(Get-Date -Format o); started=$started; samples=$samples; phase=$state.navigation.phase } | ConvertTo-Json | Set-Content -LiteralPath "$runDir\observer-result.json" -Encoding utf8
            exit 0
        }
        Start-Sleep -Milliseconds 40
    }
    throw 'Timed out waiting for the requested UI run to finish.'
} finally { $writer.Dispose() }
