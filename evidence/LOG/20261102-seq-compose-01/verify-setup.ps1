param([string]$RunId = '20261102-seq-compose-01')
$ErrorActionPreference = 'Stop'
if ($RunId -notmatch '^[a-z0-9-]{1,60}$') { throw '잘못된 실행 ID' }
$repo = (Resolve-Path "$PSScriptRoot/../../..").Path
Set-Location -LiteralPath $repo
$root = $PSScriptRoot
if (Test-Path "$root/verification.json") { throw '기존 결과를 덮어쓰지 않습니다. 새 실행 폴더를 사용하세요.' }
$project = "amr-seq-check-$RunId"
$composeFile = Join-Path $repo 'docker/compose.yaml'
$envFile = Join-Path $root 'test.env.example'
$dockerExe = (Get-Command docker -CommandType Application | Select-Object -First 1).Source
$checks = [Collections.Generic.List[object]]::new()
$started = [DateTimeOffset]::Now.ToString('O')
$owned = $false
$failure = $null
$cleanupFailure = $null
function Docker([string[]]$Arguments) {
    $lines = & $dockerExe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Docker 실패: $($lines -join [Environment]::NewLine)" }
    ($lines -join [Environment]::NewLine).Trim()
}
function Compose([string[]]$Arguments) {
    Docker (@('compose', '--env-file', $envFile, '-f', $composeFile, '-p', $project, '--profile', 'logging') + $Arguments)
}
function Check([string]$Name, [bool]$Passed, [string]$Detail) {
    $checks.Add([ordered]@{name=$Name;passed=$Passed;detail=$Detail})
    if (-not $Passed) { throw "FAIL $Name" }
    Write-Output "PASS $Name"
}
function Snapshot {
    $ids = Docker @('ps', '-aq')
    if (-not $ids) { return @() }
    @($ids -split '\r?\n' | ForEach-Object {
        Docker @('inspect', $_, '--format', '{{.Id}}|{{.State.Status}}|{{.State.StartedAt}}')
    } | Sort-Object)
}
function WaitApi {
    for ($attempt=0; $attempt -lt 45; $attempt++) {
        try { return Invoke-RestMethod "$script:seqUrl/api" -TimeoutSec 2 }
        catch { Start-Sleep -Seconds 1 }
    }
    throw '새 Seq API 준비 시간 초과'
}
$before = @(Snapshot)
$before | Set-Content "$root/containers-before.txt" -Encoding utf8
try {
    $existingContainers = Docker @('ps', '-aq', '--filter', "label=com.docker.compose.project=$project")
    $existingVolumes = Docker @('volume', 'ls', '-q', '--filter', "label=com.docker.compose.project=$project")
    $existingNetworks = Docker @('network', 'ls', '-q', '--filter', "label=com.docker.compose.project=$project")
    Check 'new-project-only' (-not $existingContainers -and -not $existingVolumes -and -not $existingNetworks) '시험 프로젝트의 기존 자원 없음.'
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    $script:seqUrl = "http://127.0.0.1:$port"
    $example = (Get-Content '.env.example' -Raw).Replace('SEQ_HTTP_PORT=5341',"SEQ_HTTP_PORT=$port").Replace('http://127.0.0.1:5341',$seqUrl)
    $example | Set-Content $envFile -Encoding utf8

    $defaultServices = Docker @('compose','--env-file',$envFile,'-f',$composeFile,'config','--services')
    Check 'seq-is-optional' ($defaultServices -eq 'mosquitto') '기본 서비스는 Mosquitto만 포함.'
    $original = & git show HEAD:docker/compose.yaml
    $original | Set-Content "$root/compose-before.yaml" -Encoding utf8
    $baseline = Docker @('compose','--project-directory',(Join-Path $repo 'docker'),'-f',"$root/compose-before.yaml",'config','--format','json') | ConvertFrom-Json -AsHashtable
    $resolved = Compose @('config','--format','json')
    $resolved | Set-Content "$root/resolved-compose.json" -Encoding utf8
    $configuration = $resolved | ConvertFrom-Json -AsHashtable
    $oldMqtt = $baseline.services.mosquitto | ConvertTo-Json -Depth 20 -Compress
    $newMqtt = $configuration.services.mosquitto | ConvertTo-Json -Depth 20 -Compress
    Check 'mosquitto-definition-preserved' ($oldMqtt -ceq $newMqtt) '기존 MQTT 서비스의 이미지·포트·마운트 정의 동일.'
    $binding = @($configuration.services.seq.ports)[0]
    Check 'env-port-and-loopback' ($binding.published -eq "$port" -and $binding.host_ip -eq '127.0.0.1' -and $binding.target -eq 80) '예시에서 바꾼 포트가 loopback에만 반영.'
    Check 'named-data-volume' (@($configuration.services.seq.volumes)[0].target -eq '/data' -and @($configuration.services.seq.volumes)[0].type -eq 'volume') '기존 PC의 bind 경로와 분리된 named volume.'
    $owned = $true
    Compose @('up','-d','--pull','never','seq') | Set-Content "$root/create.log" -Encoding utf8
    $api = WaitApi
    $container = Compose @('ps','-q','seq')
    Check 'actual-seq-created' ([bool]$api.Version -and [bool]$container) '이 저장소 Compose로 새 Seq API 실행.'
    [ordered]@{runId=$RunId;started=$started;commit=(& git rev-parse HEAD);project=$project;seqUrl=$seqUrl;seqVersion=$api.Version;dockerVersion=(Docker @('version','--format','{{.Server.Version}}'));sdk=(& dotnet --version);container=$container} | ConvertTo-Json | Set-Content "$root/environment.json" -Encoding utf8

    $trialRun = "$RunId-trial"
    & pwsh -NoProfile -File tests/manual/LOG/Verify-TrialSeq.ps1 -RunId $trialRun -SeqUrl $seqUrl *> "$root/trial.log"
    if ($LASTEXITCODE -ne 0) { throw '실제 Seq/MQTT trial 시험 실패. trial.log 확인' }
    $trialResult = Get-Content "evidence/LOG/$trialRun/verification.json" -Raw | ConvertFrom-Json
    Check 'actual-console-seq-mqtt' ($trialResult.result -eq 'passed' -and @($trialResult.checks | Where-Object {-not $_.passed}).Count -eq 0) '신규 Seq에서 기존 trial 실제 로그 수집·검색·송신 장애 격리 14항목.'
    $filter = [uri]::EscapeDataString("RunId = '$trialRun-online'")
    $eventBefore = @(Invoke-RestMethod "$seqUrl/api/events?filter=$filter&count=1&render=true" -TimeoutSec 5)
    Check 'persisted-event-before' ($eventBefore.Count -eq 1) '재생성 전 실제 콘솔 이벤트 존재.'
    Compose @('logs','--no-color','seq') | Set-Content "$root/seq-before-recreate.log" -Encoding utf8
    Compose @('stop','seq') | Set-Content "$root/recreate.log" -Encoding utf8
    Compose @('rm','-f','seq') | Add-Content "$root/recreate.log" -Encoding utf8
    Compose @('up','-d','--pull','never','seq') | Add-Content "$root/recreate.log" -Encoding utf8
    $null = WaitApi
    $newContainer = Compose @('ps','-q','seq')
    $eventAfter = @(Invoke-RestMethod "$seqUrl/api/events?filter=$filter&count=1&render=true" -TimeoutSec 5)
    Check 'data-survives-container-recreation' ($container -ne $newContainer -and $eventAfter.Count -eq 1 -and $eventBefore[0].Id -eq $eventAfter[0].Id) '컨테이너 ID 변경 뒤 동일 볼륨에서 같은 이벤트 ID 조회.'
    [ordered]@{before=$eventBefore[0].Id;after=$eventAfter[0].Id;oldContainer=$container;newContainer=$newContainer} | ConvertTo-Json | Set-Content "$root/persistence.json" -Encoding utf8
    Compose @('logs','--no-color','seq') | Set-Content "$root/seq-after-recreate.log" -Encoding utf8
} catch {
    $failure = $_.Exception.Message
    $failure | Set-Content "$root/failure.log" -Encoding utf8
} finally {
    if ($owned) {
        try { Compose @('down','--volumes') | Set-Content "$root/cleanup.log" -Encoding utf8 }
        catch { $cleanupFailure = $_.Exception.Message }
    }
    $after = @(Snapshot)
    $after | Set-Content "$root/containers-after.txt" -Encoding utf8
    $preserved = ($before -join "`n") -ceq ($after -join "`n")
    $remaining = Docker @('volume','ls','-q','--filter',"label=com.docker.compose.project=$project")
    $checks.Add([ordered]@{name='existing-containers-preserved';passed=$preserved;detail='기존 ID·실행 상태·시작 시각 비교.'})
    $checks.Add([ordered]@{name='owned-resources-cleaned';passed=(-not $cleanupFailure -and -not $remaining);detail='새 시험 프로젝트의 자원만 정리.'})
    [ordered]@{runId=$RunId;started=$started;ended=[DateTimeOffset]::Now.ToString('O');result=$(if ($failure -or $cleanupFailure -or -not $preserved -or $remaining) {'failed'} else {'passed'});checks=$checks;failure=$failure;cleanupFailure=$cleanupFailure} | ConvertTo-Json -Depth 10 | Set-Content "$root/verification.json" -Encoding utf8
}
if ($failure -or $cleanupFailure -or -not $preserved -or $remaining) { throw 'Seq Compose 검증 실패. verification.json을 확인하세요.' }
Write-Output "PASS Seq Compose setup: $root"
