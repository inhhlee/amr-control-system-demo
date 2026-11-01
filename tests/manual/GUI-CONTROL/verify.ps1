param([int]$Port = 18886, [string]$EvidenceDirectory = ('evidence/GUI/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-gui-control'))
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repoRoot
$evidencePath = [System.IO.Path]::GetFullPath($EvidenceDirectory, $repoRoot)
if (Test-Path -LiteralPath $evidencePath) { throw '기존 시험 결과를 덮어쓰지 않습니다. 새 EvidenceDirectory를 지정하세요.' }
$containerName = 'amr-gui-control-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
$configPath = (Resolve-Path 'docker/mosquitto/config/mosquitto.conf').Path
$created = $false
try {
    & docker create --name $containerName --publish "127.0.0.1:${Port}:1883" --mount "type=bind,source=$configPath,target=/mosquitto/config/mosquitto.conf,readonly" eclipse-mosquitto:2.1.2-alpine
    if ($LASTEXITCODE -ne 0) { throw '격리 MQTT 서버 생성 실패' }
    $created = $true
    & docker start $containerName
    if ($LASTEXITCODE -ne 0) { throw '격리 MQTT 서버 시작 실패' }
    & dotnet run --project tests/manual/GUI-CONTROL/LiveControlProbe.csproj -- $Port $containerName $evidencePath
    if ($LASTEXITCODE -ne 0) { throw '실제 이동·정지/GUI 시험 실패: control-results.json 확인' }
}
finally { if ($created) { & docker rm --force $containerName | Out-Null } }
