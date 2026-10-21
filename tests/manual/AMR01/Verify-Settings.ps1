param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$')]
    [string]$RunId
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$evidenceRoot = Join-Path $repoRoot "evidence/AMR/$RunId"
if (Test-Path -LiteralPath $evidenceRoot) { throw '실행 ID가 이미 존재합니다. 새 ID를 사용하세요.' }
$dllPath = Join-Path $repoRoot 'src/Amr.Simulator/bin/Debug/net10.0/Amr.Simulator.dll'
if (-not (Test-Path -LiteralPath $dllPath)) { throw 'Simulator를 Debug로 먼저 빌드하세요.' }
$null = New-Item -ItemType Directory -Path $evidenceRoot
$started = [DateTimeOffset]::Now
$baseline = Get-Content -Raw -Encoding UTF8 (Join-Path $repoRoot 'config/simulator.single.json')
$changed = $baseline | ConvertFrom-Json -AsHashtable
$changed.initialPosition = @{ x = 12; y = -7 }
$changed.speedMps = 2.5
$changed.waypoints = @(@{ x = -8; y = 9 })
$missing = $baseline | ConvertFrom-Json -AsHashtable
$null = $missing.Remove('speedMps')
$invalidUtf8 = [Text.Encoding]::UTF8.GetBytes($baseline)
$idOffset = [Text.Encoding]::UTF8.GetByteCount($baseline.Substring(0, $baseline.IndexOf('AMR_001', [StringComparison]::Ordinal)))
# C3 4D는 잘못된 UTF-8이다. JSON 구조는 정상으로 유지한다.
$invalidUtf8[$idOffset] = 0xc3
$normalBom = [byte[]](@(0xef, 0xbb, 0xbf) + [Text.Encoding]::UTF8.GetBytes($baseline))
$invalidBom = [byte[]](@(0xef, 0xbb, 0xbf) + $invalidUtf8)

$cases = @(
    @{ Id = 'normal'; Json = $baseline; Code = 0; Output = @('InitialX=-1 InitialY=0 SpeedMps=1 UpdateIntervalMs=100 WaypointCount=2', 'Waypoint Index=1 X=1 Y=2', 'X=-1 Y=0 State=IDLE CurrentSpeedMps=0', 'InitializationCompleted') },
    @{ Id = 'normal-utf8-bom'; Bytes = $normalBom; Code = 0; Output = @('ConfigurationLoaded AmrId=AMR_001', 'AmrInitialized AmrId=AMR_001 X=-1 Y=0 State=IDLE CurrentSpeedMps=0', 'InitializationCompleted') },
    @{ Id = 'invalid-utf8'; Bytes = $invalidUtf8; Code = 1; Field = '$file'; Reason = '설정 파일의 문자 인코딩을 읽을 수 없습니다.' },
    @{ Id = 'invalid-utf8-bom'; Bytes = $invalidBom; Code = 1; Field = '$file'; Reason = '설정 파일의 문자 인코딩을 읽을 수 없습니다.' },
    @{ Id = 'changed'; Json = ($changed | ConvertTo-Json -Depth 16); Code = 0; Output = @('InitialX=12 InitialY=-7 SpeedMps=2.5', 'Waypoint Index=0 X=-8 Y=9', 'X=12 Y=-7 State=IDLE CurrentSpeedMps=0') },
    @{ Id = 'missing-field'; Json = ($missing | ConvertTo-Json -Depth 16); Code = 1; Field = '$.speedMps' },
    @{ Id = 'invalid-id'; Json = $baseline.Replace('AMR_001', 'AMR/001'); Code = 1; Field = '$.amrId' },
    @{ Id = 'zero-speed'; Json = $baseline.Replace('"speedMps": 1.0', '"speedMps": 0'); Code = 1; Field = '$.speedMps' },
    @{ Id = 'negative-speed'; Json = $baseline.Replace('"speedMps": 1.0', '"speedMps": -1'); Code = 1; Field = '$.speedMps' },
    @{ Id = 'overflow-speed'; Json = $baseline.Replace('"speedMps": 1.0', '"speedMps": 1e400'); Code = 1; Field = '$.speedMps' },
    @{ Id = 'wrong-type'; Json = $baseline.Replace('"speedMps": 1.0', '"speedMps": "1"'); Code = 1; Field = '$.speedMps' },
    @{ Id = 'fractional-interval'; Json = $baseline.Replace('"updateIntervalMs": 100', '"updateIntervalMs": 0.5'); Code = 1; Field = '$.updateIntervalMs' },
    @{ Id = 'invalid-coordinate'; Json = $baseline.Replace('"x": -1', '"x": null'); Code = 1; Field = '$.initialPosition.x' },
    @{ Id = 'malformed'; Json = '{'; Code = 1; Field = '$' },
    @{ Id = 'missing-file'; Json = $null; Code = 1; Field = '$file' }
)
$results = @()
foreach ($case in $cases) {
    $caseStarted = [DateTimeOffset]::Now
    $inputFile = Join-Path $evidenceRoot ($case.Id + '.json')
    if ($null -ne $case.Bytes) {
        [IO.File]::WriteAllBytes($inputFile, $case.Bytes)
    }
    elseif ($null -ne $case.Json) {
        [IO.File]::WriteAllText($inputFile, $case.Json, [Text.UTF8Encoding]::new($false))
    }
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = (Get-Command dotnet).Source
    $start.WorkingDirectory = $repoRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.Encoding]::UTF8
    $start.StandardErrorEncoding = [Text.Encoding]::UTF8
    $start.ArgumentList.Add($dllPath)
    $start.ArgumentList.Add('--config')
    $start.ArgumentList.Add($inputFile)
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        $null = $process.Start()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(10000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "소유한 Simulator 시험 프로세스 시간 초과: $($case.Id)"
        }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        $code = $process.ExitCode
    }
    finally {
        $process.Dispose()
    }
    [IO.File]::WriteAllText((Join-Path $evidenceRoot ($case.Id + '.stdout.log')), $stdout)
    [IO.File]::WriteAllText((Join-Path $evidenceRoot ($case.Id + '.stderr.log')), $stderr)
    $passed = $code -eq $case.Code
    if ($case.Code -eq 0) {
        $passed = $passed -and $stderr.Length -eq 0
        foreach ($expected in $case.Output) { $passed = $passed -and $stdout.Contains($expected) }
    }
    else {
        $passed = $passed -and $stdout.Length -eq 0 -and $stderr.Contains("ConfigurationRejected Field=$($case.Field) Reason=")
        if ($case.Reason) {
            $passed = $passed -and $stderr.Contains("Reason=$($case.Reason)")
        }
    }
    $results += [pscustomobject]@{
        Id = $case.Id; Input = [IO.Path]::GetFileName($inputFile)
        ExpectedExitCode = $case.Code; ActualExitCode = $code
        ExpectedOutput = $case.Output; ExpectedField = $case.Field; ExpectedReason = $case.Reason
        InputSHA256 = if (Test-Path -LiteralPath $inputFile) { (Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash } else { $null }
        Started = $caseStarted.ToString('o'); Finished = [DateTimeOffset]::Now.ToString('o')
        Passed = $passed
    }
}
$report = [ordered]@{
    RunId = $RunId
    Started = $started.ToString('o')
    Finished = [DateTimeOffset]::Now.ToString('o')
    BaseCommit = (git -C $repoRoot rev-parse HEAD)
    WorkingBranch = (git -C $repoRoot branch --show-current)
    UncommittedChanges = $true
    Runtime = (dotnet --version)
    OS = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    DllSHA256 = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
    SourceConfigSHA256 = (Get-FileHash -LiteralPath (Join-Path $repoRoot 'config/simulator.single.json') -Algorithm SHA256).Hash
    Cases = $results
    Passed = @($results | Where-Object { -not $_.Passed }).Count -eq 0
}
$report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $evidenceRoot 'verification.json') -Encoding utf8
"SimulatorProcessVerification Total=$($results.Count) Passed=$($report.Passed)"
if (-not $report.Passed) { exit 1 }
