[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ScriptPath,

    [string[]] $Arguments = @(),

    [ValidateRange(1, 600)]
    [int] $TimeoutSeconds = 120,

    [string] $Inbox = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Inbox)) {
    $Inbox = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) `
        '..\artifacts\vm\agent'
}
$requestId = [Guid]::NewGuid().ToString('N')
$requestPath = Join-Path $Inbox "request-$requestId.json"
$responsePath = Join-Path $Inbox "response-$requestId.json"
$temporaryPath = "$requestPath.tmp-$PID"

try {
    [ordered]@{
        ScriptPath = $ScriptPath
        Arguments = @($Arguments)
    } |
        ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $temporaryPath -Encoding UTF8
    Move-Item -LiteralPath $temporaryPath -Destination $requestPath -Force

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (Test-Path -LiteralPath $responsePath) -and
        (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-Path -LiteralPath $responsePath)) {
        throw "The guest command timed out after $TimeoutSeconds seconds ($requestId)."
    }

    $response = Get-Content -LiteralPath $responsePath -Raw | ConvertFrom-Json
    $response | ConvertTo-Json -Depth 8
    if (-not $response.Success) {
        exit 1
    }
}
finally {
    Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $requestPath -Force -ErrorAction SilentlyContinue
}
