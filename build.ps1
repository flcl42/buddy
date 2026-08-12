[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $RuntimeIdentifier = "win-x64",
    [string] $OutputPath = (Join-Path $env:LOCALAPPDATA "Programs\Chitchat Buddy\Buddy.exe")
)

$ErrorActionPreference = "Stop"
$publishScript = Join-Path $PSScriptRoot "scripts\publish.ps1"
& $publishScript `
    -Configuration $Configuration `
    -RuntimeIdentifier $RuntimeIdentifier `
    -OutputPath $OutputPath
