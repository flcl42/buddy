#Requires -Assembly UIAutomationClient
#Requires -Assembly UIAutomationTypes

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $HostExecutable,
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $HostResultRoot,
    [string] $Workspace = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Workspace)) {
    $Workspace = Join-Path $env:TEMP 'BuddyWindowAcceptance'
}
$resolvedWorkspace = [System.IO.Path]::GetFullPath($Workspace)
$temporaryRoot = [System.IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
if (-not $resolvedWorkspace.StartsWith(
        $temporaryRoot,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected window acceptance workspace: $resolvedWorkspace"
}
if (-not (Test-Path -LiteralPath $HostExecutable -PathType Leaf)) {
    throw "Acceptance executable is unavailable: $HostExecutable"
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class BuddyDefaultWindowNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        out RECT rectangle,
        uint flags);
}
'@

function Wait-ForWindow {
    param(
        [Parameter(Mandatory)] [Diagnostics.Process] $Launcher,
        [Parameter(Mandatory)] [string] $ExecutablePath
    )

    $resolvedExecutable = [System.IO.Path]::GetFullPath($ExecutablePath)
    $deadline = (Get-Date).AddMinutes(2)
    do {
        Start-Sleep -Milliseconds 250
        $candidates = @()
        if (-not $Launcher.HasExited) {
            $candidates += $Launcher
        }
        $candidates += @(Get-Process -Name Buddy -ErrorAction SilentlyContinue)
        foreach ($candidate in $candidates | Sort-Object Id -Unique) {
            try {
                $candidate.Refresh()
                if ([System.IO.Path]::GetFullPath($candidate.Path).Equals(
                        $resolvedExecutable,
                        [System.StringComparison]::OrdinalIgnoreCase) -and
                    $candidate.MainWindowHandle -ne [IntPtr]::Zero) {
                    return $candidate
                }
            }
            catch [System.ComponentModel.Win32Exception] {
            }
            catch [System.InvalidOperationException] {
            }
        }
    } while ((Get-Date) -lt $deadline)
    throw 'Buddy did not expose its default window.'
}

function Find-ByAutomationId {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement] $Root,
        [Parameter(Mandatory)]
        [string] $AutomationId
    )

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    $deadline = (Get-Date).AddSeconds(30)
    do {
        $element = $Root.FindFirst(
            [System.Windows.Automation.TreeScope]::Subtree,
            $condition)
        if ($null -eq $element) {
            Start-Sleep -Milliseconds 200
        }
    } while ($null -eq $element -and (Get-Date) -lt $deadline)
    if ($null -eq $element) {
        throw "Could not find '$AutomationId'."
    }
    $element
}

function Capture-Window {
    param(
        [Parameter(Mandatory)] [IntPtr] $Window,
        [Parameter(Mandatory)] [string] $OutputPath,
        [Parameter(Mandatory)]
        [BuddyDefaultWindowNative+RECT] $Rectangle
    )

    $width = $Rectangle.Right - $Rectangle.Left
    $height = $Rectangle.Bottom - $Rectangle.Top
    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen(
            $Rectangle.Left,
            $Rectangle.Top,
            0,
            0,
            [System.Drawing.Size]::new($width, $height))
        $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$guestExecutable = Join-Path $resolvedWorkspace 'Buddy.exe'
$dataRoot = Join-Path $resolvedWorkspace 'data'
$runningIds = @(
    Get-Process -Name Buddy -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Id
)
if ($runningIds.Count -gt 0) {
    Get-Process -Id $runningIds -ErrorAction SilentlyContinue |
        Stop-Process -Force
    $exitDeadline = (Get-Date).AddSeconds(15)
    while ((Get-Process -Id $runningIds -ErrorAction SilentlyContinue) -and
        (Get-Date) -lt $exitDeadline) {
        Start-Sleep -Milliseconds 250
    }
    if (Get-Process -Id $runningIds -ErrorAction SilentlyContinue) {
        throw 'The previous Buddy process did not exit.'
    }
}
Start-Sleep -Milliseconds 500
if (Test-Path -LiteralPath $resolvedWorkspace) {
    Remove-Item -LiteralPath $resolvedWorkspace -Recurse -Force
}
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
New-Item -ItemType Directory -Path $HostResultRoot -Force | Out-Null
Copy-Item -LiteralPath $HostExecutable -Destination $guestExecutable -Force

$previousDataRoot = $env:BUDDY_DATA_ROOT
$previousAiRoot = $env:BUDDY_AI_ROOT
try {
    $env:BUDDY_DATA_ROOT = $dataRoot
    $env:BUDDY_AI_ROOT = Join-Path $resolvedWorkspace 'language-models'
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
$process = Wait-ForWindow `
    -Launcher $launcher `
    -ExecutablePath $guestExecutable
$window = $process.MainWindowHandle
[void] [BuddyDefaultWindowNative]::SetForegroundWindow($window)
Start-Sleep -Milliseconds 750

$rectangle = New-Object BuddyDefaultWindowNative+RECT
if (-not [BuddyDefaultWindowNative]::GetWindowRect(
        $window,
        [ref] $rectangle)) {
    throw 'Could not measure the default Buddy window.'
}
$workArea = New-Object BuddyDefaultWindowNative+RECT
[void] [BuddyDefaultWindowNative]::SystemParametersInfo(
    0x0030,
    0,
    [ref] $workArea,
    0)
$root = [System.Windows.Automation.AutomationElement]::FromHandle($window)
$setupButton = Find-ByAutomationId `
    -Root $root `
    -AutomationId 'OnboardingSetupButton'
$buttonRectangle = $setupButton.Current.BoundingRectangle

$visibleVerticalScrollers = @()
$elements = $root.FindAll(
    [System.Windows.Automation.TreeScope]::Subtree,
    [System.Windows.Automation.Condition]::TrueCondition)
foreach ($element in $elements) {
    try {
        if ($element.Current.IsOffscreen) {
            continue
        }
        $pattern = $null
        if ($element.TryGetCurrentPattern(
                [System.Windows.Automation.ScrollPattern]::Pattern,
                [ref] $pattern) -and
            ([System.Windows.Automation.ScrollPattern]$pattern).Current.VerticallyScrollable) {
            $visibleVerticalScrollers += $element
        }
    }
    catch [System.Windows.Automation.ElementNotAvailableException] {
    }
}

$screenshot = Join-Path $HostResultRoot 'default-window-setup.png'
Capture-Window `
    -Window $window `
    -OutputPath $screenshot `
    -Rectangle $rectangle

$windowWidth = $rectangle.Right - $rectangle.Left
$windowHeight = $rectangle.Bottom - $rectangle.Top
$buttonFullyInside = -not $setupButton.Current.IsOffscreen -and
    $buttonRectangle.Bottom -le $rectangle.Bottom -and
    $buttonRectangle.Top -ge $rectangle.Top
$result = [ordered]@{
    Success = $buttonFullyInside -and $visibleVerticalScrollers.Count -eq 0
    WindowWidth = $windowWidth
    WindowHeight = $windowHeight
    WindowLeft = $rectangle.Left
    WindowTop = $rectangle.Top
    WindowRight = $rectangle.Right
    WindowBottom = $rectangle.Bottom
    WorkAreaRight = $workArea.Right
    WorkAreaBottom = $workArea.Bottom
    SetupButtonVisible = -not $setupButton.Current.IsOffscreen
    SetupButtonFullyInsideWindow = $buttonFullyInside
    VisibleVerticalScrollableControls = $visibleVerticalScrollers.Count
    ProcessId = $process.Id
    WindowTitle = $process.MainWindowTitle
    Responding = $process.Responding
    Screenshot = $screenshot
}
$result | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (
        Join-Path $HostResultRoot 'default-window-check.json') -Encoding UTF8
$result | ConvertTo-Json -Depth 4
if (-not $result.Success) {
    exit 1
}
