[CmdletBinding()]
param(
    [string] $AgentScript = '',
    [string] $Inbox = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($AgentScript)) {
    $AgentScript = Join-Path $PSScriptRoot 'guest-command-agent.ps1'
}
if ([string]::IsNullOrWhiteSpace($Inbox)) {
    $Inbox = Join-Path $PSScriptRoot '..\artifacts\vm\agent-admin'
}
$arguments = @(
    '-NoLogo',
    '-NoProfile',
    '-ExecutionPolicy',
    'Bypass',
    '-File',
    ('"{0}"' -f $AgentScript),
    '-Inbox',
    ('"{0}"' -f $Inbox)
)

Start-Process `
    -FilePath powershell.exe `
    -Verb RunAs `
    -WindowStyle Hidden `
    -ArgumentList $arguments
