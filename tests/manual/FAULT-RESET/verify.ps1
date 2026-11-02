param(
    [string]$Docker = 'docker',
    [int]$Port = 18887,
    [string]$Evidence = '',
    [string]$Configuration = 'FaultReset'
)
$ErrorActionPreference = 'Stop'
if ($Port -eq 1884) { throw '공유 Broker 포트 1884는 시험에 사용하지 않습니다.' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
if (!$Evidence) { $Evidence = Join-Path $repo ('evidence/AMR/fault-reset-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$Evidence = [IO.Path]::GetFullPath($Evidence)
if (Test-Path -LiteralPath $Evidence) { throw '기존 시험 증거를 덮지 않도록 새 Evidence 경로를 지정하세요.' }
New-Item -ItemType Directory -Path $Evidence | Out-Null
$container = 'amr-fault-reset-' + [guid]::NewGuid().ToString('N')
$started = $false
Push-Location $repo
try {
    & dotnet build tests/manual/FAULT-RESET/FaultResetProbe.csproj -c $Configuration --nologo -v minimal 2>&1 | Tee-Object (Join-Path $Evidence 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $brokerConfig = Join-Path $PSScriptRoot 'mosquitto.conf'
    & $Docker run --rm -d --name $container -p "127.0.0.1:${Port}:1883" --mount "type=bind,source=$brokerConfig,target=/mosquitto/config/mosquitto.conf,readonly" eclipse-mosquitto:2.1.2-alpine
    if ($LASTEXITCODE -ne 0) { throw 'Isolated broker failed to start.' }
    $started = $true
    & $Docker inspect $container | Out-File (Join-Path $Evidence 'broker-inspect.json') -Encoding utf8
    Start-Sleep -Milliseconds 500
    & dotnet "tests/manual/FAULT-RESET/bin/$Configuration/net10.0-windows/FaultResetProbe.dll" $Port $Evidence 2>&1 | Tee-Object (Join-Path $Evidence 'probe.log')
    if ($LASTEXITCODE -ne 0) { throw 'E2E failed. See results.json and probe.log.' }
} finally {
    if ($started) {
        & $Docker logs $container 2>&1 | Out-File (Join-Path $Evidence 'broker.log') -Encoding utf8
        & $Docker stop -t 2 $container | Out-Null
    }
    Pop-Location
}
