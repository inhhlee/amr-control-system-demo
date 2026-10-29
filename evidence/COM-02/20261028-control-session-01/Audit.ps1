$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../../..").Path
Set-Location $repo
function Require([bool]$ok,[string]$message){if(!$ok){throw $message}}
function Hash([string]$path,[long]$prefix=-1){
 $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
 try{
  if($prefix -lt 0){return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}
  Require ($stream.Length -ge $prefix) 'Existing file shrank.'
  $digest=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
  try{$buffer=[byte[]]::new(65536);$remaining=$prefix;while($remaining -gt 0){$read=$stream.Read($buffer,0,[int][Math]::Min($buffer.Length,$remaining));Require ($read -gt 0) 'Unexpected end of file.';$digest.AppendData($buffer,0,$read);$remaining-=$read};[Convert]::ToHexString($digest.GetHashAndReset())}finally{$digest.Dispose()}
 }finally{$stream.Dispose()}
}
$before=Get-Content "$PSScriptRoot/before-files.json" -Raw|ConvertFrom-Json
$git=Get-Content "$PSScriptRoot/before-git.json" -Raw|ConvertFrom-Json
$allowed=@('README.md','BUILDING.md','src/Amr.Communication.Console/ConsoleRunner.cs','tools/Amr.Simulator.Map/MqttMapReceiver.cs','tools/Amr.Simulator.Map/Control/GridControlConnection.cs','tools/Amr.Simulator.Map/README.md','tests/manual/COM02-A/Verify-Heartbeat.ps1')
$same=0;$changed=@();$appended=@()
foreach($file in $before){
 Require (Test-Path -LiteralPath $file.path -PathType Leaf) "Missing original file: $($file.path)"
 if((Hash $file.path) -eq $file.sha256){$same++;continue}
 if($file.path -in $allowed){$changed+=$file.path;continue}
 if($file.path -like 'evidence/*' -and $file.path -match '\.(jsonl|log)$' -and (Hash $file.path $file.bytes) -eq $file.sha256){$appended+=@{path=$file.path;preservedPrefixBytes=$file.bytes;currentBytes=(Get-Item -LiteralPath $file.path).Length};continue}
 throw "Unexpected original file change: $($file.path)"
}
$zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $PSScriptRoot 'before-source.zip'))
$restorable=0
try{foreach($file in @($before|Where-Object {$_.path -notlike 'evidence/*'})){
 $entry=$zip.GetEntry($file.path);Require ($null -ne $entry) "Missing ZIP entry $($file.path)"
 $stream=$entry.Open();try{$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
 Require ($hash -eq $file.sha256) "ZIP restore mismatch $($file.path)";$restorable++
}}finally{$zip.Dispose()}
$old=Get-Content evidence/GIT/20261027-preservation-backup-01/source-snapshot.json -Raw|ConvertFrom-Json
foreach($file in $old.files){Require ((Hash $file.path) -eq $file.sha256) "Original 50 mismatch: $($file.path)"}
Require ((git rev-parse HEAD) -eq $git.head -and (git branch --show-current) -eq $git.branch -and (git rev-parse refs/stash) -eq $git.stash -and (git rev-parse refs/heads/codex/preserve-grid-map-20261023) -eq $git.preserved) 'Git refs changed.'
Require (@(git diff --cached --name-only).Count -eq 0) 'Unexpected staged changes.'
git diff --check;Require ($LASTEXITCODE -eq 0) 'Diff check failed.'
$containersBefore=@(Get-Content "$PSScriptRoot/containers-ready-baseline.jsonl"|Where-Object {$_}|ForEach-Object {$_|ConvertFrom-Json})
$containersAfter=@(Get-Content "$PSScriptRoot/containers-after.jsonl"|Where-Object {$_}|ForEach-Object {$_|ConvertFrom-Json})
foreach($item in $containersBefore){Require (@($containersAfter|Where-Object {$_.ID -eq $item.ID -and $_.State -eq $item.State}).Count -eq 1) "Existing container changed: $($item.Names)"}
Require ($containersBefore.Count -eq $containersAfter.Count) 'Owned test container remains.'
$source= @(git ls-files --cached --others --exclude-standard | Where-Object {$_ -notlike 'evidence/*'} | Sort-Object -Unique | ForEach-Object {@{path=$_;sha256=(Hash $_)}})
$source|ConvertTo-Json -Depth 4|Set-Content "$PSScriptRoot/final-source.json"
@{head=$git.head;branch=$git.branch;originalFiles=$before.Count;unchangedFiles=$same;authorizedExistingChanges=$changed;preservedAppendOnlyFiles=$appended;restorableSourceEntries=$restorable;original50Verified=$old.files.Count;beforeSourceZipSha256=(Hash "$PSScriptRoot/before-source.zip");existingContainersPreserved=$true;ownedTestContainersRemaining=0;stashAndPreservedBranchUnchanged=$true;stagedFiles=0;committed=$false;pushed=$false;prCreated=$false;merged=$false}|ConvertTo-Json -Depth 7|Set-Content "$PSScriptRoot/final-audit.json"
Write-Output "Audit passed: $same original files unchanged, $($changed.Count) authorized changes, $($appended.Count) verified append-only logs, $restorable restorable source entries, original $($old.files.Count) files and Git refs preserved."
