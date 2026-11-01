param(
    [int]$Port = 18885,
    [string]$EvidenceDirectory = ('evidence/GUI/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-mqtt-live')
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repoRoot
$containerName = 'amr-gui-live-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
$configPath = (Resolve-Path 'docker/mosquitto/config/mosquitto.conf').Path
$created = $false
try {
    & docker create --name $containerName --publish "127.0.0.1:${Port}:1883" --mount "type=bind,source=$configPath,target=/mosquitto/config/mosquitto.conf,readonly" eclipse-mosquitto:2.1.2-alpine
    if ($LASTEXITCODE -ne 0) { throw '격리 MQTT 서버 생성 실패' }
    $created = $true
    & docker start $containerName
    if ($LASTEXITCODE -ne 0) { throw '격리 MQTT 서버 시작 실패' }
    & dotnet run --project tests/manual/GUI-MQTT/LiveProbe.csproj -- $Port $containerName $EvidenceDirectory
    if ($LASTEXITCODE -ne 0) { throw '실제 MQTT/GUI 연결 시험 실패: live-results.json 확인' }
}
finally {
    if ($created) { & docker rm --force $containerName | Out-Null }
}
