param(
    [string]$ConsolePath = 'src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.exe',
    [int]$Port = 18887,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [string]$SeqUrl,
    [string]$RunId
)
$ErrorActionPreference = 'Stop'
$probeRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $probeRoot
$consoleFile = (Resolve-Path -LiteralPath $ConsolePath).Path
$evidencePath = [IO.Path]::GetFullPath($EvidenceDirectory, $probeRoot)
New-Item -ItemType Directory -Path $evidencePath -Force | Out-Null
if (Test-Path -LiteralPath "$evidencePath/native-environment.json") { throw '새 실행 경로를 지정하세요.' }
@{ started=[DateTimeOffset]::Now.ToString('O'); inputRedirected=[Console]::IsInputRedirected;
   outputRedirected=[Console]::IsOutputRedirected; columns=[Console]::WindowWidth;
   console=$consoleFile; sha256=(Get-FileHash -LiteralPath $consoleFile -Algorithm SHA256).Hash;
   managedSha256=(Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($consoleFile,'.dll')) -Algorithm SHA256).Hash; port=$Port;
   seqUrl=$SeqUrl;runId=$RunId } |
    ConvertTo-Json | Set-Content -LiteralPath "$evidencePath/native-environment.json"
# 이 시험 프로세스에만 적용한다. 기존 사용자 Seq 설정/서비스는 변경하지 않는다.
$env:AMR_SEQ_URL=$SeqUrl
$env:AMR_SEQ_API_KEY=$null
$env:AMR_LOG_RUN_ID=$RunId
try {
    & $consoleFile --trial --host 127.0.0.1 --port $Port --amr-id AMR_001 --show-payload --no-color
}
finally {
    @{ ended=[DateTimeOffset]::Now.ToString('O'); exitCode=$LASTEXITCODE } |
        ConvertTo-Json | Set-Content -LiteralPath "$evidencePath/native-result.json"
}
