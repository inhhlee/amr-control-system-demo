# One-time cleanup of anonymous volumes linked to this task's first two attempts.
# No prune, force removal, date-only selection, or modification of existing services.
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repository
$audit = Join-Path $PSScriptRoot 'earlier-volume-cleanup'
if (Test-Path "$audit/result.json") { throw 'This audit already ran; preserve its result.' }
New-Item -ItemType Directory -Path $audit -Force | Out-Null
$docker = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'
function Invoke-Docker([string[]]$Arguments) {
    $at = [DateTimeOffset]::UtcNow
    $output = @(& $docker @Arguments 2>&1)
    $code = $LASTEXITCODE
    [ordered]@{arguments=$Arguments;atUtc=$at.ToString('O');exitCode=$code} | ConvertTo-Json -Compress | Add-Content "$audit/commands.jsonl" -Encoding utf8
    if ($code -ne 0) { throw "Docker failed: $($Arguments -join ' ')" }
    return $output
}
$before = @(Invoke-Docker @('volume','ls','--format','{{.Name}}'))
$before | Set-Content "$audit/before.txt" -Encoding utf8
$refs = [Collections.Generic.List[object]]::new()
foreach ($attempt in @('20261021-report-demo-01','20261021-report-demo-02')) {
    $parent = "evidence/DEMO/$attempt"
    $start = [DateTimeOffset]::Parse((Get-Content "$parent/execution.json" -Raw | ConvertFrom-Json -DateKind String).startedAtUtc).AddSeconds(-1)
    $end = [DateTimeOffset]::Parse((Get-Content "$parent/verification.json" -Raw | ConvertFrom-Json -DateKind String).completedAtUtc).AddSeconds(1)
    foreach ($stage in @('c1','c2','c3')) {
        $path = "evidence/COM-03/$attempt-$stage/broker-mounts.json"
        if (!(Test-Path $path)) {
            if ($attempt -eq '20261021-report-demo-02' -and $stage -eq 'c3') {
                $owner = (Get-Content "evidence/COM-03/$attempt-$stage/environment.json" -Raw | ConvertFrom-Json).containerId
                $eventPath = "$audit/c3-volume-events.jsonl"
                $events = @(Get-Content $eventPath | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.Action -eq 'mount' -and $_.Actor.Attributes.container -eq $owner })
                if ($events.Count -ne 2) { throw 'Missing exact container-to-volume mount events.' }
                foreach ($event in $events) { $refs.Add(@{reference=$event.Actor.ID;owner=$owner;evidence=$eventPath;start=$start;end=$end}) }
            }
            continue
        }
        $envPath = "evidence/COM-03/$attempt-$stage/environment.json"
        $owner = (Get-Content $envPath -Raw | ConvertFrom-Json).containerId
        if ($owner -notmatch '^[a-f0-9]{64}$') { throw 'Missing owned container identity.' }
        foreach ($mount in (Get-Content $path -Raw | ConvertFrom-Json)) {
            if ($mount.Type -eq 'volume') { $refs.Add(@{reference=$mount.Name;owner=$owner;evidence=$path;start=$start;end=$end}) }
        }
    }
    $broker = Get-Content "$parent/broker.json" -Raw | ConvertFrom-Json
    if ($broker.containerId -notmatch '^[a-f0-9]{64}$' -or $broker.name -ne "amr-demo-$attempt") { throw 'Missing parent Broker identity.' }
    $inventory = "evidence/COM-03/$attempt-c1/containers-before.jsonl"
    $line = @(Get-Content $inventory | Where-Object { $_.Contains('"Names":"' + $broker.name + '"') })
    if ($line.Count -ne 1) { throw 'Expected exactly one parent Broker inventory record.' }
    # The first attempt used Docker's truncated JSON. Only ASCII volume-name
    # prefixes from that owned record are used, requiring an unambiguous full match.
    $mountNames = [regex]::Match($line[0],'"Mounts":"([^"]*)"').Groups[1].Value
    $ids = [regex]::Matches($mountNames,'\b[a-f0-9]{12,64}\b')
    if ($ids.Count -ne 2) { throw 'Expected two anonymous mount references for the parent Broker.' }
    foreach ($id in $ids) { $refs.Add(@{reference=$id.Value;owner=$broker.containerId;evidence=$inventory;start=$start;end=$end}) }
}
$validated = [Collections.Generic.List[object]]::new()
foreach ($ref in $refs) {
    $candidateVolumes = @($before | Where-Object { $_.StartsWith($ref.reference,[StringComparison]::Ordinal) })
    if ($candidateVolumes.Count -ne 1 -or $candidateVolumes[0] -notmatch '^[a-f0-9]{64}$') { throw 'Volume reference is absent or ambiguous; nothing removed.' }
    $volume = (Invoke-Docker @('volume','inspect',$candidateVolumes[0]) | Out-String | ConvertFrom-Json -DateKind String)[0]
    $created = [DateTimeOffset]::Parse($volume.CreatedAt)
    if ($created -lt $ref.start -or $created -gt $ref.end) { throw 'Volume creation time does not match owned attempt.' }
    if (@(Invoke-Docker @('ps','-a','--filter',"volume=$($volume.Name)",'--format','{{.ID}}')).Count) { throw 'Volume remains attached; nothing removed.' }
    $validated.Add([ordered]@{name=$volume.Name;owner=$ref.owner;observedReference=$ref.reference;ownerEvidence=$ref.evidence;createdAt=$volume.CreatedAt;notAttached=$true})
}
$validated.ToArray() | ConvertTo-Json -Depth 5 | Set-Content "$audit/ownership.json" -Encoding utf8
if (@($validated.name | Select-Object -Unique).Count -ne $validated.Count -or $validated.Count -ne 12) { throw "Expected 12 distinct task-owned volumes; validated $($validated.Count); nothing removed." }
$failures = [Collections.Generic.List[string]]::new()
foreach ($volume in $validated) {
    try { $null = Invoke-Docker @('volume','rm',$volume.name) }
    catch { $failures.Add($_.Exception.Message) }
}
$after = @(Invoke-Docker @('volume','ls','--format','{{.Name}}'))
$after | Set-Content "$audit/after.txt" -Encoding utf8
$expected = @($before | Where-Object { $_ -notin $validated.name } | Sort-Object)
if (@(Compare-Object $expected @($after | Sort-Object)).Count) { $failures.Add('Unrelated volume inventory changed or an owned volume remains.') }
[ordered]@{result=if($failures.Count){'failed'}else{'passed'};removedCount=$before.Count-$after.Count;expectedRemoved=12;existingOtherVolumesPreserved=($failures.Count -eq 0);errors=$failures.ToArray();completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json -Depth 5 | Set-Content "$audit/result.json" -Encoding utf8
if ($failures.Count) { throw ($failures -join '; ') }
Write-Output 'PASS: removed 12 independently identified anonymous volumes from the earlier attempts; other volumes preserved.'
