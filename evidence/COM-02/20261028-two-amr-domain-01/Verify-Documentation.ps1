$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../../..").Path
$repoDocs = @('README.md', 'BUILDING.md', 'tools/Amr.Simulator.Map/README.md',
    'config/map-demo/README.md', 'evidence/COM-02/20261028-two-amr-domain-01/README.md')
$planDocs = @('관제시스템 과제 진행/과제1/과제계획.md', '관제시스템 과제 진행/과제3/과제계획.md',
    '관제시스템 과제 진행/과제4/과제계획.md', '관제시스템 과제 진행/명세/검토 및 결정사항.md',
    '관제시스템 과제 진행/명세/기능명세.md', '관제시스템 과제 진행/전체일정.md', '일일업무/2026-10-28.md')
$linkPattern = '!?(?:\[[^\]\r\n]*\])\((?:<(?<angle>[^>\r\n]+)>|(?<plain>[^)\r\n]+))\)'
$checks = @()
foreach ($item in @($repoDocs | ForEach-Object { @{path = (Join-Path $repo $_); scope = 'full'} }) +
        @($planDocs | ForEach-Object { @{path = (Join-Path 'C:/process-plan' $_); scope = 'currentTwoAmrReferences'} })) {
    $content = [IO.File]::ReadAllText($item.path)
    if ($item.scope -ne 'full') {
        # Historical local links are outside this update. Check this run's new cross references.
        $content = ($content -split '\r?\n' | Where-Object {
            $_ -match 'two-amr|CORE-FLEET|Core 책임/한계|GUI/F5 검증 구분|GUI 검증 구분'
        }) -join "`n"
    }
    foreach ($match in [regex]::Matches($content, $linkPattern)) {
        $target = if ($match.Groups['angle'].Success) { $match.Groups['angle'].Value } else { $match.Groups['plain'].Value.Trim() }
        if ($target -match '^(?:https?://|mailto:|codex:|plugin:|#)') { continue }
        $parts = $target -split '#', 2
        $file = [Uri]::UnescapeDataString($parts[0])
        $resolved = if ([IO.Path]::IsPathRooted($file)) { [IO.Path]::GetFullPath($file) }
                    else { [IO.Path]::GetFullPath((Join-Path (Split-Path $item.path) $file)) }
        $exists = Test-Path -LiteralPath $resolved
        $anchor = $null
        # Explicit anchors added by this change are verified; unrelated legacy heading slugs are not inferred.
        if ($exists -and $parts.Count -gt 1 -and $parts[1] -match '^two-amr') {
            $anchor = [IO.File]::ReadAllText($resolved).Contains('id="' + $parts[1] + '"')
        }
        $checks += @{source = $item.path; scope = $item.scope; target = $target; resolved = $resolved; exists = $exists; explicitAnchorPresent = $anchor}
    }
}
$bad = @($checks | Where-Object { !$_.exists -or $_.explicitAnchorPresent -eq $false })
@{passed = ($bad.Count -eq 0); checkedReferences = $checks.Count; missing = $bad; results = $checks;
  scope = 'All local file targets in 5 repository guides; current two-AMR cross-references in 7 originals; explicit new anchors. Historical original links and general heading slugs excluded.'
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$PSScriptRoot/link-check.json"
if ($bad.Count) { $bad | ConvertTo-Json -Depth 4; throw "$($bad.Count) invalid documentation references" }
$sources = Get-Content -LiteralPath "$PSScriptRoot/final-source.json" -Raw | ConvertFrom-Json
foreach ($file in $sources) {
    $path = Join-Path $repo $file.path
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) {
        throw "Verified source changed: $($file.path)"
    }
}
$documents = Get-Content -LiteralPath "$PSScriptRoot/documents-after-finalization.json" -Raw | ConvertFrom-Json
foreach ($file in $documents) {
    if ((Get-FileHash -LiteralPath $file.path -Algorithm SHA256).Hash -ne $file.sha256) {
        throw "Finalized original/view changed: $($file.path)"
    }
}
@{passed = $true; verifiedSourceFiles = $sources.Count; finalizedDocumentsAndViews = $documents.Count;
    references = $checks.Count; productTestsRepeated = $false} |
    ConvertTo-Json | Set-Content -LiteralPath "$PSScriptRoot/final-document-source-check.json"
Write-Output "Documentation passed: $($checks.Count) references, $($sources.Count) unchanged verified sources, $($documents.Count) original documents/views."
