$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../../..").Path
Set-Location $repo
function Require([bool]$ok,[string]$message){if(!$ok){throw $message}}
function Hash([string]$path){
 $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
 try{[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
}
$before=Get-Content "$PSScriptRoot/before-files.json" -Raw|ConvertFrom-Json
$git=Get-Content "$PSScriptRoot/before-git.json" -Raw|ConvertFrom-Json
$allowed=@('README.md','BUILDING.md','AmrControlSystem.slnx','AmrControlSystem.slnLaunch',
 'src/Amr.Communication/Mqtt/Sessions/ControlCommunicationSession.cs','src/Amr.ControlGui/GridMapDataProvider.cs',
 'src/Amr.ControlGui/MainForm.Grid.cs','src/Amr.ControlGui/MainForm.cs','tools/Amr.Simulator.Map/Amr.Simulator.Map.csproj',
 'tools/Amr.Simulator.Map/Program.cs','tools/Amr.Simulator.Map/Properties/launchSettings.json',
 'tools/Amr.Simulator.Map/README.md','config/map-demo/README.md')
$same=0;$changed=@()
foreach($file in $before){
 Require (Test-Path -LiteralPath $file.path -PathType Leaf) "Missing original file: $($file.path)"
 if((Hash $file.path) -eq $file.sha256){$same++;continue}
 Require ($file.path -in $allowed) "Unexpected original change: $($file.path)"
 $changed+=$file.path
}
$zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $PSScriptRoot 'before-source.zip'))
$restorable=0
try{
 foreach($file in @($before|Where-Object{$_.path -notlike 'evidence/*'})){
  $entry=$zip.GetEntry($file.path);Require ($null -ne $entry) "Missing backup entry: $($file.path)"
  $stream=$entry.Open();try{$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
  Require ($hash -eq $file.sha256) "Backup mismatch: $($file.path)";$restorable++
 }
 function Read-Backup([string]$name){$reader=[IO.StreamReader]::new($zip.GetEntry($name).Open());try{$reader.ReadToEnd()}finally{$reader.Dispose()}}
 $oldLaunch=Read-Backup 'AmrControlSystem.slnLaunch'|ConvertFrom-Json
 $newLaunch=Get-Content AmrControlSystem.slnLaunch -Raw|ConvertFrom-Json
 Require (($oldLaunch[0]|ConvertTo-Json -Depth 8 -Compress) -eq ($newLaunch[0]|ConvertTo-Json -Depth 8 -Compress)) 'Original F5 profile changed.'
 $oldMap=Read-Backup 'tools/Amr.Simulator.Map/Properties/launchSettings.json'|ConvertFrom-Json
 $newMap=Get-Content tools/Amr.Simulator.Map/Properties/launchSettings.json -Raw|ConvertFrom-Json
 Require (($oldMap.profiles.'Grid MQTT'|ConvertTo-Json -Compress) -eq ($newMap.profiles.'Grid MQTT'|ConvertTo-Json -Compress)) 'Original Map launch profile changed.'
}finally{$zip.Dispose()}
$old=Get-Content evidence/GIT/20261027-preservation-backup-01/source-snapshot.json -Raw|ConvertFrom-Json
foreach($file in $old.files){Require ((Hash $file.path) -eq $file.sha256) "Original 50 mismatch: $($file.path)"}
Require ((git rev-parse HEAD) -eq $git.head -and (git branch --show-current) -eq $git.branch -and (git remote get-url origin) -eq $git.origin -and
 (git rev-parse refs/stash) -eq $git.stash -and (git rev-parse refs/heads/codex/preserve-grid-map-20261023) -eq $git.preserved) 'Git references changed.'
Require (@(git diff --cached --name-only).Count -eq 0) 'Unexpected staged changes.'
git diff --check;Require ($LASTEXITCODE -eq 0) 'Diff check failed.'
$containersBefore=@(Get-Content "$PSScriptRoot/containers-before.jsonl"|Where-Object{$_}|ForEach-Object{$_|ConvertFrom-Json})
$containersAfter=@(Get-Content "$PSScriptRoot/containers-after.jsonl"|Where-Object{$_}|ForEach-Object{$_|ConvertFrom-Json})
Require ($containersBefore.Count -eq $containersAfter.Count) 'Test container remains.'
foreach($container in $containersBefore){Require (@($containersAfter|Where-Object{$_.ID -eq $container.ID -and $_.State -eq $container.State}).Count -eq 1) "Existing container changed: $($container.Names)"}
git ls-files --cached --others --exclude-standard|Where-Object{$_ -notlike 'evidence/*'}|Sort-Object -Unique|ForEach-Object{@{path=$_;sha256=(Hash $_)}}|ConvertTo-Json -Depth 4|Set-Content "$PSScriptRoot/final-source.json"
@{head=$git.head;branch=$git.branch;originalFiles=$before.Count;unchangedFiles=$same;authorizedExistingChanges=$changed;
 restorableSourceEntries=$restorable;original50Verified=$old.files.Count;originalF5ProfilePreserved=$true;originalGridMqttProfilePreserved=$true;
 beforeSourceZipSha256=(Hash "$PSScriptRoot/before-source.zip");existingContainersPreserved=$true;ownedTestContainersRemaining=0;
 stashAndPreservedBranchUnchanged=$true;stagedFiles=0;committed=$false;pushed=$false;prCreated=$false;merged=$false}|ConvertTo-Json -Depth 6|Set-Content "$PSScriptRoot/final-audit.json"
Write-Output "Audit passed: $same original files unchanged; $($changed.Count) authorized edits; $restorable ZIP entries restorable; original $($old.files.Count) files, F5, Git refs and containers preserved."
