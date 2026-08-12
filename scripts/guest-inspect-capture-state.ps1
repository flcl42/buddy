$ErrorActionPreference = 'Continue'
$processes = Get-CimInstance Win32_Process |
    Where-Object {
        $_.Name -match 'Buddy|powershell'
    } |
    Select-Object ProcessId, ParentProcessId, SessionId, Name, ExecutablePath, CommandLine

$startupLog = Join-Path $env:LOCALAPPDATA 'Buddy\logs\startup.log'
[pscustomobject]@{
    Processes = @($processes)
    StartupLogTail = if (Test-Path -LiteralPath $startupLog) {
        @(Get-Content -LiteralPath $startupLog -Tail 80)
    }
    else {
        @('Startup log is absent.')
    }
} | ConvertTo-Json -Depth 6
