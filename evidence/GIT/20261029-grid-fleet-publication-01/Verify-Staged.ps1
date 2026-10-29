$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../../..").Path
Set-Location $repo
$selection=Get-Content "$PSScriptRoot/selection.json" -Raw|ConvertFrom-Json
$paths=@(Get-Content "$PSScriptRoot/staging.paths.txt")
$staged=@(git -c core.quotepath=false diff --cached --name-only)
foreach($p in $staged){if($p -notin $paths){throw "Unreviewed staged path: $p"}}
foreach($f in $selection.selected){
    if((Get-FileHash -LiteralPath $f.path -Algorithm SHA256).Hash -ne $f.sha256){throw "Selected content changed: $($f.path)"}
}
$local=Get-Content "$PSScriptRoot/preserved-local-only.json" -Raw|ConvertFrom-Json
foreach($f in $local.files){
    if($f.path -in $staged -or (Get-FileHash -LiteralPath $f.path -Algorithm SHA256).Hash -ne $f.sha256){throw "Local backup not preserved: $($f.path)"}
}
$sources=Get-Content 'evidence/COM-02/20261028-two-amr-domain-01/final-source.json' -Raw|ConvertFrom-Json
foreach($f in $sources){
    if((Get-FileHash -LiteralPath $f.path -Algorithm SHA256).Hash -ne $f.sha256){throw "Verified source changed: $($f.path)"}
    $expected=git hash-object --path=$($f.path) $f.path
    $entry=git ls-files --stage -- $f.path
    if($entry -notmatch '^\d+ ([a-f0-9]{40}) 0\s' -or $Matches[1] -ne $expected){throw "Index content differs from verified source: $($f.path)"}
}
if(@(git ls-files --unmerged).Count){throw 'Unmerged index entries'}
$whitespace=@(git diff --cached --check -- . ':!evidence')
# The unchanged imported source already ended in this extra newline in the verified backup.
$known='tools/Amr.Simulator.Map/Control/GridMapSession.cs:299: new blank line at EOF.'
if(@($whitespace|Where-Object{$_ -ne $known}).Count){throw "New source whitespace error: $whitespace"}
$summary=Get-Content 'evidence/COM-02/20261028-two-amr-domain-01/test-summary.json' -Raw|ConvertFrom-Json
$verification=Get-Content 'evidence/COM-02/20261028-two-amr-domain-01/verification.json' -Raw|ConvertFrom-Json
if($verification.totalPassed -ne 1141 -or @($verification.projectResults|Where-Object{$_.failed -ne 0 -or $_.notExecuted -ne 0}).Count){throw 'Unexpected test results'}
Write-Output "Staged verification passed: $($staged.Count) changed files, $($sources.Count) exact verified worktree/index sources, $($local.count) preserved local-only files; legacy EOF newline noted."
