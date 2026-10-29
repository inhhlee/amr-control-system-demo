param([Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]{1,48}$')][string]$RunId)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../../..").Path
Set-Location $repo
$root=Join-Path $repo "evidence/COM-02/$RunId"
$base=Get-Content "$root/baseline.json" -Raw|ConvertFrom-Json
$preservation=Join-Path $repo 'evidence/GIT/20261027-preservation-backup-01'
$old=Get-Content "$preservation/source-snapshot.json" -Raw|ConvertFrom-Json
function Require([bool]$ok,[string]$message){if(!$ok){throw $message}}
function Hash([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
$allowed=@(
 'src/Amr.ControlGui/GridMapDataProvider.cs','src/Amr.ControlGui/MainForm.Grid.cs','src/Amr.ControlGui/MainForm.cs',
 'src/Amr.Simulator/Navigation/GridSimulation.cs','src/Amr.Simulator/README.md',
 'tests/Amr.ControlGui.Tests/GridDashboardTests.cs','tests/Amr.Simulator.Map.Tests/GridMqttTests.cs',
 'tests/Amr.Simulator.Tests/GridSimulationTests.cs','tests/manual/AMR01/Start-MqttMap.ps1',
 'tests/manual/AMR01/Verify-GridMqttMap.ps1','tests/manual/AMR01/Verify-MapConfiguration.ps1',
 'tools/Amr.Simulator.Map/Control/GridControlConnection.cs','tools/Amr.Simulator.Map/Control/GridMapSession.cs',
 'tools/Amr.Simulator.Map/MqttMapReceiver.cs','tools/Amr.Simulator.Map/README.md','tools/Amr.Simulator.Map/wwwroot/grid.js',
 'BUILDING.md','README.md','src/Amr.Communication/Mqtt/Subscriptions/MessageSubscription.cs',
 'tests/Amr.Communication.Tests/Mqtt/Subscriptions/MessageSubscriptionTests.cs'
)
$changed=@();$unchanged=@()
foreach($file in $base.files){
 Require (Test-Path -LiteralPath $file.path) "Missing baseline source: $($file.path)"
 if((Hash $file.path) -eq $file.sha256){$unchanged+=$file.path}else{$changed+=$file.path;Require ($file.path -in $allowed) "Unexpected modification: $($file.path)"}
}
foreach($file in $old.files){Require ((Hash $file.path) -eq $file.sha256) "Original untracked file changed: $($file.path)"}
foreach($file in (Get-Content "$preservation/artifact-hashes.json" -Raw|ConvertFrom-Json)){
 Require ((Hash (Join-Path $preservation $file.path)) -eq $file.sha256) "Backup artifact changed: $($file.path)"
}
$zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $root 'before-source.zip'))
try{
 foreach($file in $base.files){
  $entry=$zip.GetEntry($file.path);Require ($null -ne $entry) "Snapshot entry missing: $($file.path)"
  $stream=$entry.Open()
  try{$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
  Require ($hash -eq $file.sha256) "Snapshot restore hash mismatch: $($file.path)"
 }
 $snapshotEntries=$zip.Entries.Count
}finally{$zip.Dispose()}
$head=git rev-parse HEAD;$branch=git branch --show-current;$stash=git rev-parse refs/stash
$preserved=git rev-parse refs/heads/codex/preserve-grid-map-20261023
Require ($head -eq $base.head -and $branch -eq $base.branch -and $stash -eq $base.stash -and $preserved -eq $old.preserved) 'Git references changed.'
$staged=@(git diff --cached --name-only);Require ($staged.Count -eq 0) 'Index contains staged changes.'
git diff --check;Require ($LASTEXITCODE -eq 0) 'Diff whitespace check failed.'
$files=@(git ls-files --cached --others --exclude-standard -- src tests tools config README.md BUILDING.md AmrControlSystem.slnx global.json)
$manifest=@(foreach($file in $files){@{path=$file;sha256=Hash $file}})
$manifest|ConvertTo-Json -Depth 6|Set-Content "$root/final-source-manifest.json" -Encoding utf8
$audit=@{head=$head;branch=$branch;stash=$stash;preservedBranch=$preserved;stagedFiles=0;
 baselineSources=$base.files.Count;unchangedSources=$unchanged.Count;authorizedChangedSources=$changed;
 originalUntrackedFilesPreserved=$old.files.Count;backupArtifactsPreserved=$true;
 beforeSnapshotRestorableEntries=$snapshotEntries;beforeSnapshotSha256=Hash "$root/before-source.zip";
 coreAndMockSourcesPreserved=@($unchanged|Where-Object {$_ -like 'src/Amr.Core/*' -or $_ -match '/Mock[^/]*\.cs$'});
 finalSourceFiles=$manifest.Count;commitsPushesPrMergesPerformed=$false}
$audit|ConvertTo-Json -Depth 8|Set-Content "$root/final-audit.json" -Encoding utf8
Write-Output "Audit passed: $($unchanged.Count) unchanged / $($changed.Count) authorized changed; original $($old.files.Count) untracked + backup artifacts + refs preserved; snapshot $snapshotEntries entries verified; index empty."
