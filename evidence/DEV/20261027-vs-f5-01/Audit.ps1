$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../../..").Path
Set-Location $repo
function Hash-Shared([string]$path){
 $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
 try{[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
}
function Require([bool]$ok,[string]$message){if(!$ok){throw $message}}
$before=Get-Content "$PSScriptRoot/before-files.json" -Raw|ConvertFrom-Json
$git=Get-Content "$PSScriptRoot/before-git.json" -Raw|ConvertFrom-Json
$allowed=@('README.md','BUILDING.md')
$live=Get-Content 'evidence/AMR/20261027-heartbeat-live-01/session.json' -Raw|ConvertFrom-Json
$liveLog=[IO.Path]::GetRelativePath($repo,(Join-Path $live.ProductEvidence 'grid-navigation.jsonl')).Replace('\','/')
$changed=@();$same=0;$liveChanges=@()
foreach($file in $before){
 Require (Test-Path -LiteralPath $file.path) "Missing existing file: $($file.path)"
 $actual=Hash-Shared (Join-Path $repo $file.path)
 if($actual -eq $file.sha256){$same++;continue}
 if($file.path -in $allowed){$changed+=$file.path;continue}
 if($file.path -eq $liveLog){
  $now=Get-Item -LiteralPath $file.path
  Require ($now.Length -ge $file.bytes) 'Existing live log shrank.'
  $liveChanges+=@{path=$file.path;beforeBytes=$file.bytes;afterBytes=$now.Length;reason='Previously running AMR session continues writing; not edited by this setup.'}
  continue
 }
 throw "Unexpected existing file change: $($file.path)"
}
$zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $PSScriptRoot 'before-source.zip'))
$verified=0
try{
 foreach($file in @($before|Where-Object {$_.path -notlike 'evidence/*'})){
  $entry=$zip.GetEntry($file.path);Require ($null -ne $entry) "Backup entry missing: $($file.path)"
  $stream=$entry.Open();try{$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
  Require ($hash -eq $file.sha256) "Backup restore hash mismatch: $($file.path)";$verified++
 }
}finally{$zip.Dispose()}
Require ((git rev-parse HEAD) -eq $git.head -and (git branch --show-current) -eq $git.branch -and
 (git rev-parse refs/stash) -eq $git.stash -and (git rev-parse refs/heads/codex/preserve-grid-map-20261023) -eq $git.preserved) 'Git references changed.'
Require (@(git diff --cached --name-only).Count -eq 0) 'Unexpected staged changes.'
git diff --check;Require ($LASTEXITCODE -eq 0) 'Diff check failed.'
$beforeContainers=@(Get-Content "$PSScriptRoot/containers-before.jsonl"|Where-Object {$_}|ForEach-Object {$_|ConvertFrom-Json})
$afterContainers=@(Get-Content "$PSScriptRoot/containers-after.jsonl"|Where-Object {$_}|ForEach-Object {$_|ConvertFrom-Json})
foreach($item in $beforeContainers){Require (@($afterContainers|Where-Object {$_.ID -eq $item.ID -and $_.State -eq $item.State}).Count -eq 1) 'Existing container state changed.'}
$result=@{head=$git.head;branch=$git.branch;originalFiles=$before.Count;unchangedFiles=$same;authorizedExistingChanges=$changed;
 existingLiveLogGrowth=$liveChanges;restorableSourceEntries=$verified;beforeSourceZipSha256=(Hash-Shared "$PSScriptRoot/before-source.zip");
 existingContainersPreserved=$true;stashAndPreservedBranchUnchanged=$true;stagedFiles=0;committed=$false;pushed=$false;prCreated=$false;merged=$false}
$result|ConvertTo-Json -Depth 7|Set-Content "$PSScriptRoot/final-audit.json"
Write-Output "Audit passed: $same unchanged existing files; $($changed.Count) authorized documentation changes; $($liveChanges.Count) existing active log; $verified backup entries verified; Git refs/index and containers preserved."
