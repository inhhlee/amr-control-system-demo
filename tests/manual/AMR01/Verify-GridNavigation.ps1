param(
    [Parameter(Mandatory)][string]$BaseUrl,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_-]+$')][string]$RunId
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$directory = Join-Path $repository "evidence/AMR/$RunId"
if (Test-Path -LiteralPath $directory) { throw 'RunId must be new.' }
$base = [uri]$BaseUrl
if ($base.Scheme -ne 'http' -or $base.Host -ne '127.0.0.1') { throw 'Use the local map server at http://127.0.0.1:<port>.' }
New-Item -ItemType Directory -Path $directory | Out-Null
$results = [System.Collections.Generic.List[object]]::new()
$headers = @{ 'X-Simulator-Map' = '1' }
function State { Invoke-RestMethod "$BaseUrl/api/grid/state" -TimeoutSec 5 }
function Post([string]$action, $body = $null) {
    $parameters = @{ Uri = "$BaseUrl/api/grid/$action"; Method = 'Post'; Headers = $headers; TimeoutSec = 5 }
    if ($null -ne $body) { $parameters.ContentType = 'application/json'; $parameters.Body = ConvertTo-Json $body -Compress }
    Invoke-RestMethod @parameters
}
function Require([bool]$condition, [string]$message) { if (!$condition) { throw $message } }
function Wait-State([scriptblock]$condition) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        $s = State
        if (& $condition $s.navigation) { return $s }
        Start-Sleep -Milliseconds 50
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "State timeout: $($s.navigation | ConvertTo-Json -Compress -Depth 6)"
}
function Save([string]$name, $actual) {
    $results.Add([ordered]@{ name = $name; verdict = 'passed'; actual = $actual })
    $actual | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $directory "$name.json") -Encoding utf8
}
try {
    Post reset | Out-Null
    $s = State
    Require ($s.navigation.remainingDistance -eq 9 -and $s.navigation.phase -eq 'Ready') 'Initial path must be nine steps.'
    Save 'initial-nine' $s

    $response = Invoke-WebRequest "$BaseUrl/api/grid/start" -Method Post -SkipHttpErrorCheck
    Require ($response.StatusCode -eq 403) 'Unmarked mutation should be rejected.'
    Save 'request-header' @{ statusCode = [int]$response.StatusCode }
    foreach ($body in @(@{}, @{row=-1;col=0;blocked=$true}, @{row=0;col=2;blocked=$false}, @{row=0;col=0;blocked=$true}, @{row=4;col=5;blocked=$true})) {
        $response = Invoke-WebRequest "$BaseUrl/api/grid/obstacle" -Method Post -Headers $headers -ContentType application/json -Body (ConvertTo-Json $body) -SkipHttpErrorCheck
        Require ($response.StatusCode -eq 400) 'Invalid edit should be rejected.'
    }
    Save 'invalid-edits' @{ rejected = 5; expectedStatus = 400 }

    Post start | Out-Null
    $s = Wait-State { param($n) $n.phase -eq 'Completed' }
    Require ($s.navigation.moves -eq 9 -and $s.navigation.current.row -eq 4 -and $s.navigation.current.col -eq 5) 'Original run must arrive in nine moves.'
    Save 'original-arrival' $s

    Post reset | Out-Null
    Post start | Out-Null
    $s = Wait-State { param($n) $n.moves -ge 1 }
    Require ($s.navigation.moves -lt 4) 'Must edit before reaching (2,2).'
    Post obstacle @{row=2;col=2;blocked=$true} | Out-Null
    $s = Wait-State { param($n) $n.phase -eq 'Completed' }
    Require ($s.navigation.moves -eq 11) 'Detour must arrive in eleven moves.'
    Require (@($s.navigation.traveled | Where-Object { $_.row -eq 2 -and $_.col -eq 2 }).Count -eq 0) 'Robot entered a blocked cell.'
    Save 'moving-detour' $s

    Post reset | Out-Null
    Post start | Out-Null
    $s = Wait-State { param($n) $n.moves -ge 1 }
    Post obstacle @{row=1;col=1;blocked=$true} | Out-Null
    $s = Wait-State { param($n) $n.phase -eq 'Waiting' }
    Start-Sleep -Milliseconds 1300
    $still = State
    Require ($still.navigation.phase -eq 'Waiting' -and $still.navigation.moves -eq 1 -and $still.navigation.current.row -eq 0 -and $still.navigation.current.col -eq 1) 'Blocked robot should stay at (0,1).'
    Save 'blocked-waiting' $still
    Post obstacle @{row=1;col=1;blocked=$false} | Out-Null
    $s = Wait-State { param($n) $n.phase -eq 'Completed' }
    Require ($s.navigation.moves -eq 9) 'Removing obstacle should automatically resume and arrive.'
    Save 'automatic-resume' $s

    Post reset | Out-Null
    Post start | Out-Null
    $s = Wait-State { param($n) $n.moves -ge 1 }
    Post cancel | Out-Null
    $before = State
    Start-Sleep -Milliseconds 800
    $s = State
    Require ($s.navigation.phase -eq 'Canceled' -and $s.navigation.moves -eq $before.navigation.moves) 'Canceled robot must remain stopped.'
    Save 'cancel' $s
    Post reset | Out-Null
    $s = State
    Require ($s.navigation.phase -eq 'Ready' -and $s.navigation.remainingDistance -eq 9 -and $s.navigation.moves -eq 0) 'Reset must restore the original map.'
    Save 'reset' $s
    $old = Invoke-RestMethod "$BaseUrl/api/state" -TimeoutSec 5
    Require ($old.source -eq 'simulator-output' -or $old.source -eq 'mqtt-control-receiver') 'Existing map API changed.'
    Save 'existing-api' @{ source = $old.source; phase = $old.phase }
}
catch {
    $results.Add([ordered]@{ name = 'failure'; verdict = 'failed'; reason = $_.Exception.Message })
    throw
}
finally {
    $results | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $directory 'verification.json') -Encoding utf8
}
Write-Output "Passed $($results.Count) scenarios. Evidence=$directory"
