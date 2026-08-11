[CmdletBinding()]
param(
    [string] $ExecutablePath = "C:\Programs\Buddy.exe",
    [switch] $NoLaunch
)

$ErrorActionPreference = "Stop"
$resolvedExecutable = [System.IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $resolvedExecutable -PathType Leaf)) {
    throw "Build Buddy first; '$resolvedExecutable' does not exist."
}

$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
[System.IO.Directory]::CreateDirectory($startMenu) | Out-Null
$shortcutPath = Join-Path $startMenu "Chitchat Buddy.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $resolvedExecutable
$shortcut.WorkingDirectory = Split-Path -Parent $resolvedExecutable
$shortcut.IconLocation = "$resolvedExecutable,0"
$shortcut.Description = "Chitchat Buddy speech recorder, trainer, and AI dialog"
$shortcut.Save()

Write-Host "Buddy executable: $resolvedExecutable"
Write-Host "Start Menu shortcut: $shortcutPath"

if (-not $NoLaunch) {
    Start-Process -FilePath $resolvedExecutable
}
