param(
    [Parameter(Mandatory)][ValidateRange(1,65535)][int]$BrokerPort,
    [Parameter(Mandatory)][string]$RunDirectory
)
$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $RunDirectory).Path
Add-Type -Path (Join-Path $PSScriptRoot 'ResponseGateProxy.cs')
[ResponseGateProxy]::RunAsync($BrokerPort, $directory).GetAwaiter().GetResult()
