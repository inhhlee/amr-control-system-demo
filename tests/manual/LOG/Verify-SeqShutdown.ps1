param([Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunId)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location -LiteralPath $repository
$run = Join-Path $repository "evidence/LOG/$RunId"
if (Test-Path -LiteralPath $run) { throw 'Execution ID exists.' }
New-Item -ItemType Directory -Path $run | Out-Null
$previousUrl = $env:AMR_SEQ_URL
$previousRun = $env:AMR_LOG_RUN_ID
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$process = $null
$client = $null
try {
    $listener.Start()
    $env:AMR_SEQ_URL = "http://127.0.0.1:$($listener.LocalEndpoint.Port)"
    $env:AMR_LOG_RUN_ID = $RunId
    $accept = $listener.AcceptTcpClientAsync()
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath "$repository/src/Amr.Communication.Console/bin/Debug/net10.0/Amr.Communication.Console.exe" `
        -ArgumentList @('--port','0') -WindowStyle Hidden -RedirectStandardOutput "$run/stdout.log" `
        -RedirectStandardError "$run/stderr.log" -PassThru
    $null = $process.Handle
    $client = $accept.WaitAsync([TimeSpan]::FromSeconds(5)).GetAwaiter().GetResult()
    # Read the incoming HTTP request but deliberately never return a response.
    $stream = $client.GetStream()
    $stream.ReadTimeout = 5000
    $buffer = [byte[]]::new(8192)
    $received = 0
    do { $n = $stream.Read($buffer,0,$buffer.Length); $received += $n } while ($n -gt 0)
    if (!$process.WaitForExit(7000)) { throw 'Process exceeded bounded shutdown.' }
    $process.WaitForExit()
    $watch.Stop()
    $errors = Get-Content "$run/stderr.log" -Raw
    if ($process.ExitCode -ne 1 -or $received -eq 0 -or $watch.Elapsed.TotalSeconds -gt 7 -or
        $errors -notmatch 'InputRejected Reason=InvalidSettings' -or
        $errors -notmatch 'SeqDeliverySummary .* Sent=0 Failed=[1-9]\d* Dropped=\d+') { throw 'Slow endpoint isolation failed.' }
    [ordered]@{result='passed';baseCommit=(git rev-parse HEAD);sourceState='uncommitted #26';
        completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');elapsedSeconds=$watch.Elapsed.TotalSeconds;
        processExitCode=$process.ExitCode;httpRequestBytes=$received;serverResponseSent=$false;
        clientClosedSocket=$true;allowedProcessSeconds=7;expectedApplicationExitCode=1} |
        ConvertTo-Json | Set-Content "$run/verification.json" -Encoding utf8
    Write-Output "PASS: no HTTP response, socket EOF, bounded process shutdown. $run"
} catch {
    $_ | Out-String | Set-Content "$run/failure.log" -Encoding utf8
    throw
} finally {
    if ($null -ne $client) { $client.Dispose() }
    $listener.Stop()
    if ($null -ne $process) {
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
    $env:AMR_SEQ_URL = $previousUrl
    $env:AMR_LOG_RUN_ID = $previousRun
}
