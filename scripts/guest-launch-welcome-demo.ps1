[CmdletBinding()]
param(
    [string] $Workspace = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Workspace)) {
    $Workspace = Join-Path $env:TEMP 'BuddyWebsiteCapture'
}
$Workspace = [System.IO.Path]::GetFullPath($Workspace)
$temporaryRoot = [System.IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
if (-not $Workspace.StartsWith(
        $temporaryRoot,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected welcome demo workspace: $Workspace"
}
$guestExecutable = Join-Path $workspace 'Buddy.exe'
$welcomeData = Join-Path $workspace 'welcome-data'

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WelcomeWindowNative
{
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);
}
'@

Get-Process -Name Buddy -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
if (-not (Test-Path -LiteralPath $guestExecutable -PathType Leaf)) {
    throw 'The website capture executable is not installed in the guest workspace.'
}

$expectedPrefix = [System.IO.Path]::GetFullPath($workspace).TrimEnd('\') + '\'
$resolvedData = [System.IO.Path]::GetFullPath($welcomeData)
if (-not $resolvedData.StartsWith(
        $expectedPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The welcome data path escaped the guest workspace: $resolvedData"
}
if (Test-Path -LiteralPath $resolvedData) {
    Remove-Item -LiteralPath $resolvedData -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedData -Force | Out-Null

$previousDataRoot = $env:BUDDY_DATA_ROOT
$previousAiRoot = $env:BUDDY_AI_ROOT
try {
    $env:BUDDY_DATA_ROOT = $resolvedData
    $env:BUDDY_AI_ROOT = Join-Path $workspace 'language-models'
    $launcher = Start-Process -FilePath $guestExecutable -PassThru
}
finally {
    if ($null -eq $previousDataRoot) {
        Remove-Item Env:BUDDY_DATA_ROOT -ErrorAction SilentlyContinue
    }
    else {
        $env:BUDDY_DATA_ROOT = $previousDataRoot
    }
    if ($null -eq $previousAiRoot) {
        Remove-Item Env:BUDDY_AI_ROOT -ErrorAction SilentlyContinue
    }
    else {
        $env:BUDDY_AI_ROOT = $previousAiRoot
    }
}

$deadline = (Get-Date).AddMinutes(2)
$process = $null
$window = [IntPtr]::Zero
do {
    Start-Sleep -Milliseconds 250
    foreach ($candidate in @(Get-Process -Name Buddy -ErrorAction SilentlyContinue)) {
        try {
            $candidate.Refresh()
            if ($candidate.Path -eq $guestExecutable -and
                $candidate.MainWindowHandle -ne [IntPtr]::Zero) {
                $process = $candidate
                $window = $candidate.MainWindowHandle
                break
            }
        }
        catch {
        }
    }
} while ($window -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline)
if ($window -eq [IntPtr]::Zero) {
    throw 'Buddy did not open its welcome window.'
}

[void] [WelcomeWindowNative]::SetWindowPos(
    $window, [IntPtr](-1), 150, 70, 1300, 850, 0x0040)
[void] [WelcomeWindowNative]::SetWindowPos(
    $window, [IntPtr](-2), 150, 70, 1300, 850, 0x0040)
[void] [WelcomeWindowNative]::SetForegroundWindow($window)
Start-Sleep -Seconds 3

[pscustomobject]@{
    ProcessId = $process.Id
    WindowHandle = $window.ToInt64()
    WindowTitle = $process.MainWindowTitle
} | ConvertTo-Json
