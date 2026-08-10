[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $RuntimeIdentifier = "win-x64",
    [string] $OutputPath = "C:\Programs\Buddy.exe"
)

$ErrorActionPreference = "Stop"
$publishScript = Join-Path $PSScriptRoot "scripts\publish.ps1"
& $publishScript `
    -Configuration $Configuration `
    -RuntimeIdentifier $RuntimeIdentifier `
    -OutputPath $OutputPath
