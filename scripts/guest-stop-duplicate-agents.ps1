[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [int[]] $ProcessIds
)

$ErrorActionPreference = 'Stop'
$stopped = @()
foreach ($processId in $ProcessIds) {
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($null -eq $process -or $process.ProcessName -ne 'powershell') {
        continue
    }
    Stop-Process -Id $processId -Force
    $stopped += $processId
}

[pscustomobject]@{
    Success = $true
    StoppedProcessIds = $stopped
    CurrentProcessId = $PID
} | ConvertTo-Json
