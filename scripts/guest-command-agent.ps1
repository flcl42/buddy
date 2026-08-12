[CmdletBinding()]
param(
    [string] $Inbox = '',
    [string] $AllowedScriptRoot = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Inbox)) {
    $Inbox = Join-Path $PSScriptRoot '..\artifacts\vm\agent'
}
if ([string]::IsNullOrWhiteSpace($AllowedScriptRoot)) {
    $AllowedScriptRoot = $PSScriptRoot
}
$allowedRoot = [System.IO.Path]::GetFullPath($AllowedScriptRoot)
    .TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
$pollDelayMilliseconds = 250

New-Item -ItemType Directory -Path $Inbox -Force | Out-Null

function Write-AgentJson {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $Value
    )

    $temporaryPath = "$Path.tmp-$PID"
    $Value |
        ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $temporaryPath -Encoding UTF8
    Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
}

$readyPath = Join-Path $Inbox 'ready.json'
Write-AgentJson -Path $readyPath -Value ([ordered]@{
    Ready = $true
    ProcessId = $PID
    ComputerName = $env:COMPUTERNAME
    UserName = $env:USERNAME
    SessionId = (Get-Process -Id $PID).SessionId
    StartedAt = (Get-Date).ToString('o')
})

while (-not (Test-Path -LiteralPath (Join-Path $Inbox 'stop'))) {
    $requests = @(
        Get-ChildItem -LiteralPath $Inbox -Filter 'request-*.json' -File |
            Sort-Object Name
    )

    foreach ($requestFile in $requests) {
        $request = $null
        $requestId = $requestFile.BaseName.Substring('request-'.Length)
        $responsePath = Join-Path $Inbox "response-$requestId.json"
        try {
            $request = Get-Content -LiteralPath $requestFile.FullName -Raw |
                ConvertFrom-Json
            $scriptPath = [System.IO.Path]::GetFullPath(
                [string] $request.ScriptPath)
            if (-not $scriptPath.StartsWith(
                    $allowedRoot,
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Script is outside the approved host scripts directory: $scriptPath"
            }
            if ([System.IO.Path]::GetExtension($scriptPath) -ne '.ps1') {
                throw "Only PowerShell scripts can be run: $scriptPath"
            }
            if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
                throw "Script does not exist: $scriptPath"
            }

            $arguments = @($request.Arguments | ForEach-Object { [string] $_ })
            $output = @(
                & powershell.exe `
                    -NoLogo `
                    -NoProfile `
                    -ExecutionPolicy Bypass `
                    -File $scriptPath `
                    @arguments 2>&1 |
                    ForEach-Object { $_.ToString() }
            )
            $exitCode = $LASTEXITCODE
            Write-AgentJson -Path $responsePath -Value ([ordered]@{
                Success = ($exitCode -eq 0)
                ExitCode = $exitCode
                RequestId = $requestId
                CompletedAt = (Get-Date).ToString('o')
                Output = $output
            })
        }
        catch {
            Write-AgentJson -Path $responsePath -Value ([ordered]@{
                Success = $false
                ExitCode = 1
                RequestId = $requestId
                CompletedAt = (Get-Date).ToString('o')
                Error = $_.Exception.Message
            })
        }
        finally {
            Remove-Item -LiteralPath $requestFile.FullName -Force -ErrorAction SilentlyContinue
        }
    }

    Start-Sleep -Milliseconds $pollDelayMilliseconds
}

Remove-Item -LiteralPath $readyPath -Force -ErrorAction SilentlyContinue
