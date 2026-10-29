$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../../..").Path
Set-Location $repo
$base = '4e44fa880cb0d1fcccb000dbc53e634fd90381b5'
if ((git rev-parse HEAD) -ne $base -or (git branch --show-current) -ne 'feature/46-grid-map-integration') {
    throw 'Publication base or branch changed; review before preparing again.'
}
$verified = Get-Content 'evidence/COM-02/20261028-two-amr-domain-01/final-source.json' -Raw | ConvertFrom-Json
foreach ($file in $verified) {
    if ((Get-FileHash -LiteralPath $file.path -Algorithm SHA256).Hash -ne $file.sha256) {
        throw "Previously verified source changed: $($file.path)"
    }
}
$selected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$all = @(git -c core.quotepath=false ls-files --cached --others --exclude-standard)
$untracked = @(git -c core.quotepath=false ls-files --others --exclude-standard)
foreach ($file in @(git -c core.quotepath=false diff HEAD --name-only)) { [void]$selected.Add($file) }
foreach ($file in $untracked) {
    if ($file -notlike 'evidence/*' -or
        $file -match '^evidence/(AMR/(20261027|20261028|mission-live-20261027|mission-live-20261028)|COM-02/|GUI/20261027-grid-dashboard-01/|DEV/20261027-vs-f5-01/)' -or
        $file -like 'evidence/GIT/20261029-grid-fleet-publication-01/*') {
        if ($file -notlike 'evidence/COM-02/20261028-two-amr-live-final/*') { [void]$selected.Add($file) }
    }
}
# Keep local recovery bundles and the unused intermediate run off the publication index.
# Preserve the originals in place; no ignore rule or deletion is introduced.
$localReferences = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$known = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($p in $all) { [void]$known.Add($p) }
$pending = $true
while ($pending) {
    $pending = $false
    foreach ($p in @($selected)) {
        if ($p -notlike '*.md') { continue }
        foreach ($m in [regex]::Matches([IO.File]::ReadAllText((Join-Path $repo $p)), '\[[^\]\r\n]*\]\((?:<(?<a>[^>\r\n]+)>|(?<b>[^)\r\n]+))\)')) {
            $link = if ($m.Groups['a'].Success) { $m.Groups['a'].Value } else { $m.Groups['b'].Value }
            if ($link -match '^(https?://|mailto:|codex:|#|[A-Za-z]:|/)') { continue }
            $target = [Uri]::UnescapeDataString(($link -split '#',2)[0])
            $full = [IO.Path]::GetFullPath((Join-Path (Split-Path (Join-Path $repo $p)) $target))
            if (!$full.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { continue }
            $relative = [IO.Path]::GetRelativePath($repo,$full).Replace('\','/')
            if (!$known.Contains($relative) -or $selected.Contains($relative)) { continue }
            if (($relative -like 'evidence/GIT/*' -and $relative -notlike 'evidence/GIT/20261029-grid-fleet-publication-01/*') -or
                $relative -like 'evidence/COM-02/20261028-two-amr-live-final/*') {
                [void]$localReferences.Add($relative); continue
            }
            [void]$selected.Add($relative); $pending = $true
        }
    }
}
$manifestNames = @('selection.json','preserved-local-only.json','staging.paths.txt')
foreach ($name in $manifestNames) { [void]$selected.Add('evidence/GIT/20261029-grid-fleet-publication-01/'+$name) }
$paths = @($selected | Sort-Object)
$rows = foreach ($p in $paths) {
    if ($p -match '^evidence/GIT/20261029-grid-fleet-publication-01/(selection.json|preserved-local-only.json|staging.paths.txt)$') { continue }
    $f = Get-Item -LiteralPath $p
    if ($f.Length -ge 100MB) { throw "File exceeds GitHub regular Git size limit: $p" }
    @{path=$p; bytes=$f.Length; sha256=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash}
}
$preserved = foreach ($p in $untracked) {
    if (!$selected.Contains($p)) {
        $f=Get-Item -LiteralPath $p
        @{path=$p; bytes=$f.Length; sha256=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash}
    }
}
@{base=$base; issue=46; verifiedSourceFiles=$verified.Count; sourceChangedAfterVerification=$false;
  selectedFiles=$paths.Count; selectedBytes=($rows.bytes|Measure-Object -Sum).Sum;
  selected=$rows; referencesToLocalArchives=@($localReferences);
  policy='Publish implementation and related run evidence. Keep recovery Git archives and unused intermediate run local; originals unchanged.'
} | ConvertTo-Json -Depth 6 | Set-Content "$PSScriptRoot/selection.json"
@{files=$preserved; count=@($preserved).Count; deleted=$false; ignored=$false} |
    ConvertTo-Json -Depth 5 | Set-Content "$PSScriptRoot/preserved-local-only.json"
[IO.File]::WriteAllLines("$PSScriptRoot/staging.paths.txt",$paths,[Text.UTF8Encoding]::new($false))
Write-Output "Prepared $($paths.Count) paths ($([math]::Round(($rows.bytes|Measure-Object -Sum).Sum/1MB,1)) MiB); preserved $(@($preserved).Count) local-only files. Verified source count: $($verified.Count)."
