[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $HostExecutable,
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $HostSeed,
    [string] $Workspace = '',
    [switch] $ForceExecutableCopy
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
    throw "Unexpected website demo workspace: $Workspace"
}
$guestExecutable = Join-Path $workspace 'Buddy.exe'
$demoData = Join-Path $workspace 'demo-data'

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DemoWindowNative
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
New-Item -ItemType Directory -Path $workspace -Force | Out-Null
$hostExecutableInfo = Get-Item -LiteralPath $HostExecutable
$copyExecutable = $ForceExecutableCopy `
    -or -not (Test-Path -LiteralPath $guestExecutable)
if (-not $copyExecutable) {
    $guestExecutableInfo = Get-Item -LiteralPath $guestExecutable
    $copyExecutable = $guestExecutableInfo.Length -ne $hostExecutableInfo.Length
}
if ($copyExecutable) {
    Copy-Item -LiteralPath $HostExecutable -Destination $guestExecutable -Force
}
if (Test-Path -LiteralPath $demoData) {
    Remove-Item -LiteralPath $demoData -Recurse -Force
}
New-Item -ItemType Directory -Path $demoData -Force | Out-Null
Copy-Item -Path (Join-Path $HostSeed '*') -Destination $demoData -Recurse -Force

$previousDataRoot = $env:BUDDY_DATA_ROOT
$previousAiRoot = $env:BUDDY_AI_ROOT
$previousDemoWord = $env:BUDDY_DEMO_WORD_CARD
$previousDemoPhonetic = $env:BUDDY_DEMO_WORD_PHONETIC
$previousDemoPartOfSpeech = $env:BUDDY_DEMO_WORD_PART_OF_SPEECH
$previousDemoDefinition = $env:BUDDY_DEMO_WORD_DEFINITION
$previousDemoSetupSuppression = $env:BUDDY_DEMO_SUPPRESS_LOCAL_SETUP
$previousDemoPreviewOnly = $env:BUDDY_DEMO_PREVIEW_ONLY
try {
    $env:BUDDY_DATA_ROOT = $demoData
    $env:BUDDY_AI_ROOT = Join-Path $workspace 'language-models'
    $env:BUDDY_DEMO_WORD_CARD = 'nuance'
    $env:BUDDY_DEMO_WORD_PHONETIC = '/' `
        + [char]0x02C8 + 'nu' + [char]0x02D0 + '.' `
        + [char]0x0251 + [char]0x02D0 + 'ns/'
    $env:BUDDY_DEMO_WORD_PART_OF_SPEECH = 'noun'
    $env:BUDDY_DEMO_WORD_DEFINITION =
        'A subtle distinction or qualification that adds precision to an idea.'
    $env:BUDDY_DEMO_SUPPRESS_LOCAL_SETUP = '1'
    $env:BUDDY_DEMO_PREVIEW_ONLY = '1'
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
    if ($null -eq $previousDemoWord) {
        Remove-Item Env:BUDDY_DEMO_WORD_CARD -ErrorAction SilentlyContinue
    }
    else {
        $env:BUDDY_DEMO_WORD_CARD = $previousDemoWord
    }
    if ($null -eq $previousDemoPhonetic) {
        Remove-Item Env:BUDDY_DEMO_WORD_PHONETIC -ErrorAction SilentlyContinue
    }
    else {
        $env:BUDDY_DEMO_WORD_PHONETIC = $previousDemoPhonetic
    }
    if ($null -eq $previousDemoPartOfSpeech) {
        Remove-Item Env:BUDDY_DEMO_WORD_PART_OF_SPEECH `
            -ErrorAction SilentlyContinue
    }
    else {
        $env:BUDDY_DEMO_WORD_PART_OF_SPEECH = $previousDemoPartOfSpeech
    }
    if ($null -eq $previousDemoDefinition) {
        Remove-Item Env:BUDDY_DEMO_WORD_DEFINITION -ErrorAction SilentlyContinue
    }
    else {
        $env:BUDDY_DEMO_WORD_DEFINITION = $previousDemoDefinition
    }
    if ($null -eq $previousDemoSetupSuppression) {
        Remove-Item Env:BUDDY_DEMO_SUPPRESS_LOCAL_SETUP `
            -ErrorAction SilentlyContinue
    }
    else {
        $env:BUDDY_DEMO_SUPPRESS_LOCAL_SETUP = $previousDemoSetupSuppression
    }
    if ($null -eq $previousDemoPreviewOnly) {
        Remove-Item Env:BUDDY_DEMO_PREVIEW_ONLY -ErrorAction SilentlyContinue
    }
    else {
        $env:BUDDY_DEMO_PREVIEW_ONLY = $previousDemoPreviewOnly
    }
}

$deadline = (Get-Date).AddMinutes(3)
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
    throw 'Buddy did not open its demo window.'
}

[void] [DemoWindowNative]::SetWindowPos(
    $window, [IntPtr](-1), 150, 70, 1300, 850, 0x0040)
[void] [DemoWindowNative]::SetWindowPos(
    $window, [IntPtr](-2), 150, 70, 1300, 850, 0x0040)
[void] [DemoWindowNative]::SetForegroundWindow($window)

Start-Sleep -Seconds 3
$process.Refresh()
[pscustomobject]@{
    ProcessId = $process.Id
    WindowHandle = $window.ToInt64()
    WindowTitle = $process.MainWindowTitle
} | ConvertTo-Json -Depth 5
